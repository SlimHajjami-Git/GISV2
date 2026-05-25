using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Standalone tow detector — independent of accidents.
///
/// <para>Operator rule: a vehicle whose engine is OFF (<c>ignition_on=false</c>)
/// that nevertheless reports a speed above <see cref="MinSpeedKph"/> km/h AND a
/// real change of position is being towed. To reject a single jittery GPS fix
/// we require a CLUSTER (the "nuage de points"): at least
/// <see cref="MinConsecutiveFrames"/> consecutive qualifying frames and a
/// cumulative displacement over <see cref="MinDistanceMeters"/> m.</para>
///
/// <para>One <see cref="TowEvent"/> row = one tow trip. It is created when the
/// cluster is first confirmed (with a notification to company admins), then
/// extended live while the motion continues, and finally flipped to
/// <c>ended</c> once the vehicle stops producing tow-like frames for
/// <see cref="EndStaleMinutes"/> minutes.</para>
///
/// <para>Immobilised vehicles are excluded (operator choice): a damaged car
/// being shuffled around a workshop yard must not raise a tow alert.</para>
/// </summary>
public class TowDetectionService : BackgroundService
{
    private const int ScanIntervalMinutes = 3;
    private const int StartupDelayMinutes = 2;

    /// <summary>How far back each scan looks. Must exceed the scan interval so
    /// late-arriving ingest frames are still picked up.</summary>
    private const int WindowMinutes = 20;

    private const double MinSpeedKph = 15.0;
    private const int MinConsecutiveFrames = 3;
    private const double MinDistanceMeters = 100.0;

    /// <summary>Frames more than this far apart belong to different runs.</summary>
    private const int RunGapMinutes = 5;

    /// <summary>A new cluster within this delay of an active trip's last frame
    /// extends that trip instead of opening a new one.</summary>
    private const int ContinuationCooldownMinutes = 30;

    /// <summary>An active trip with no fresh frame for this long is closed.</summary>
    private const int EndStaleMinutes = 15;

    private const string NotificationType = "tow_detected";

    private readonly IServiceProvider _serviceProvider;
    private readonly IGeocodingService _geocoding;
    private readonly ILogger<TowDetectionService> _logger;

    public TowDetectionService(
        IServiceProvider serviceProvider,
        IGeocodingService geocoding,
        ILogger<TowDetectionService> logger)
    {
        _serviceProvider = serviceProvider;
        _geocoding = geocoding;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), ct); }
        catch (TaskCanceledException) { return; }

        _logger.LogInformation(
            "TowDetectionService started (scan={Scan}min, window={Window}min, minSpeed={Speed}km/h, minFrames={Frames}, minDist={Dist}m)",
            ScanIntervalMinutes, WindowMinutes, MinSpeedKph, MinConsecutiveFrames, MinDistanceMeters);

        while (!ct.IsCancellationRequested)
        {
            try { await ScanAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "TowDetectionService scan failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(ScanIntervalMinutes), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var notifService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var now = DateTime.UtcNow;

        await CloseStaleTripsAsync(context, now, ct);

        var windowStart = now.AddMinutes(-WindowMinutes);
        var frames = await LoadQualifyingFramesAsync(context, windowStart, now, ct);
        if (frames.Count == 0) return;

        foreach (var deviceFrames in frames.GroupBy(f => f.DeviceId))
        {
            try
            {
                await HandleDeviceAsync(context, notifService, deviceFrames.Key,
                    deviceFrames.OrderBy(f => f.RecordedAt).ToList(), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "TowDetectionService: failed handling device {DeviceId}", deviceFrames.Key);
            }
        }
    }

    /// <summary>Flips active trips with no recent frame to <c>ended</c>.</summary>
    private static async Task CloseStaleTripsAsync(GisDbContext context, DateTime now, CancellationToken ct)
    {
        var staleBefore = now.AddMinutes(-EndStaleMinutes);
        var stale = await context.TowEvents
            .IgnoreQueryFilters()
            .Where(t => t.Status == "active" && t.LastSeenAt < staleBefore)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        foreach (var t in stale)
        {
            t.Status = "ended";
            t.EndedAt = t.LastSeenAt;
            t.UpdatedAt = now;
        }
        await context.SaveChangesAsync(ct);
    }

    private static async Task<List<TowFrame>> LoadQualifyingFramesAsync(
        GisDbContext context, DateTime from, DateTime to, CancellationToken ct)
    {
        // Engine OFF + moving fast + valid fix. The displacement check is done
        // in C# over the returned cluster.
        const string sql = @"
SELECT
    device_id   AS ""DeviceId"",
    recorded_at AS ""RecordedAt"",
    latitude    AS ""Latitude"",
    longitude   AS ""Longitude"",
    COALESCE(speed_kph, 0)::double precision AS ""SpeedKph""
FROM gps_positions
WHERE recorded_at >= {0}
  AND recorded_at <= {1}
  AND is_valid = TRUE
  AND ignition_on = FALSE
  AND speed_kph > {2}
ORDER BY device_id, recorded_at;
";
        return await context.Database
            .SqlQueryRaw<TowFrame>(sql, from, to, MinSpeedKph)
            .ToListAsync(ct);
    }

    private async Task HandleDeviceAsync(
        GisDbContext context,
        INotificationService notifService,
        int deviceId,
        List<TowFrame> deviceFrames,
        CancellationToken ct)
    {
        // The latest contiguous run of qualifying frames.
        var run = LatestRun(deviceFrames);
        if (run.Count < MinConsecutiveFrames) return;
        if (run.DistanceMeters < MinDistanceMeters) return;

        // Resolve the vehicle. Skip unassigned devices and — per operator
        // choice — immobilised vehicles (workshop-yard shuffles aren't tows).
        var vehicle = await context.Vehicles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.GpsDeviceId == deviceId, ct);
        if (vehicle == null) return;
        if (vehicle.IsImmobilized) return;

        var existing = await context.TowEvents
            .IgnoreQueryFilters()
            .OrderByDescending(t => t.LastSeenAt)
            .FirstOrDefaultAsync(t => t.DeviceId == deviceId, ct);

        var isContinuation = existing != null
            && existing.Status == "active"
            && (run.FirstAt - existing.LastSeenAt).TotalMinutes <= ContinuationCooldownMinutes;

        if (isContinuation)
        {
            await ExtendTripAsync(context, existing!, run, ct);
            return;
        }

        await CreateTripAsync(context, notifService, vehicle, deviceId, run, ct);
    }

    private static async Task ExtendTripAsync(
        GisDbContext context, TowEvent trip, TowRun run, CancellationToken ct)
    {
        // Only count frames newer than what we've already recorded so distance
        // isn't double-counted across overlapping scan windows.
        var fresh = run.Frames.Where(f => f.RecordedAt > trip.LastSeenAt).ToList();
        if (fresh.Count == 0) return;

        double prevLat = trip.LastLat ?? fresh[0].Latitude;
        double prevLon = trip.LastLon ?? fresh[0].Longitude;
        double added = 0;
        double maxSpeed = trip.MaxSpeedKph;
        foreach (var f in fresh)
        {
            added += Haversine(prevLat, prevLon, f.Latitude, f.Longitude);
            prevLat = f.Latitude;
            prevLon = f.Longitude;
            if (f.SpeedKph > maxSpeed) maxSpeed = f.SpeedKph;
        }

        trip.LastSeenAt = fresh[^1].RecordedAt;
        trip.LastLat = fresh[^1].Latitude;
        trip.LastLon = fresh[^1].Longitude;
        trip.DistanceMeters += added;
        trip.FrameCount += fresh.Count;
        trip.MaxSpeedKph = maxSpeed;
        trip.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(ct);
    }

    private async Task CreateTripAsync(
        GisDbContext context,
        INotificationService notifService,
        Vehicle vehicle,
        int deviceId,
        TowRun run,
        CancellationToken ct)
    {
        string? address = null;
        try { address = await _geocoding.ReverseGeocodeAsync(run.FirstLat, run.FirstLon); }
        catch { /* best-effort */ }

        var device = await context.GpsDevices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId, ct);

        var now = DateTime.UtcNow;
        var trip = new TowEvent
        {
            CompanyId = vehicle.CompanyId,
            VehicleId = vehicle.Id,
            DeviceId = deviceId,
            DeviceUid = device?.DeviceUid,
            StartedAt = run.FirstAt,
            LastSeenAt = run.LastAt,
            StartLat = run.FirstLat,
            StartLon = run.FirstLon,
            LastLat = run.LastLat,
            LastLon = run.LastLon,
            StartAddress = address,
            MaxSpeedKph = run.MaxSpeedKph,
            DistanceMeters = run.DistanceMeters,
            FrameCount = run.Count,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.TowEvents.Add(trip);
        await context.SaveChangesAsync(ct);

        _logger.LogWarning(
            "TowDetectionService: TOW detected — vehicle {VehicleId} ({Plate}), company {CompanyId}, {Dist:F0} m, max {Speed:F0} km/h, {Frames} frames from {At}",
            vehicle.Id, vehicle.Plate ?? vehicle.Name, vehicle.CompanyId,
            run.DistanceMeters, run.MaxSpeedKph, run.Count, run.FirstAt);

        await NotifyAsync(context, notifService, vehicle, trip, ct);
    }

    private async Task NotifyAsync(
        GisDbContext context,
        INotificationService notifService,
        Vehicle vehicle,
        TowEvent trip,
        CancellationToken ct)
    {
        var admins = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.CompanyId == vehicle.CompanyId
                     && u.Status == "active"
                     && u.Role != null
                     && u.Role.IsCompanyAdmin)
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (admins.Count == 0) return;

        var label = !string.IsNullOrWhiteSpace(vehicle.Plate)
            ? vehicle.Plate!
            : (!string.IsNullOrWhiteSpace(vehicle.Name) ? vehicle.Name : $"#{vehicle.Id}");
        var localTime = trip.StartedAt.ToString("dd/MM/yyyy 'à' HH'h'mm");
        var place = string.IsNullOrWhiteSpace(trip.StartAddress) ? "" : $" — {trip.StartAddress}";
        var message =
            $"Le véhicule {label} se déplace moteur coupé (jusqu'à {Math.Round(trip.MaxSpeedKph)} km/h) "
          + $"depuis le {localTime} UTC{place} : remorquage probable.";

        var metadata = new Dictionary<string, object>
        {
            ["towEventId"] = trip.Id,
            ["vehicleId"] = vehicle.Id,
            ["vehicleLabel"] = label,
            ["startedAt"] = trip.StartedAt.ToString("O"),
        };

        foreach (var adminId in admins)
        {
            try
            {
                await notifService.CreateAndSendAsync(
                    companyId: vehicle.CompanyId,
                    userId: adminId,
                    type: NotificationType,
                    title: "Remorquage détecté",
                    message: message,
                    priority: "high",
                    referenceType: "tow_event",
                    referenceId: trip.Id,
                    actionUrl: "/remorquages",
                    metadata: metadata,
                    ct: ct);
            }
            catch { /* per-admin push miss is recoverable via the bell */ }
        }
    }

    // ── Run / cluster computation ───────────────────────────────────────────

    /// <summary>
    /// Splits the device's qualifying frames into runs (gap &gt; RunGap opens a
    /// new run) and returns the most recent run with its cumulative metrics.
    /// </summary>
    private static TowRun LatestRun(List<TowFrame> frames)
    {
        var run = new List<TowFrame>();
        var runs = new List<List<TowFrame>>();
        DateTime? prev = null;
        foreach (var f in frames)
        {
            if (prev != null && (f.RecordedAt - prev.Value).TotalMinutes > RunGapMinutes)
            {
                runs.Add(run);
                run = new List<TowFrame>();
            }
            run.Add(f);
            prev = f.RecordedAt;
        }
        if (run.Count > 0) runs.Add(run);

        var latest = runs.Count > 0 ? runs[^1] : new List<TowFrame>();
        return TowRun.From(latest);
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0; // metres
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private sealed class TowRun
    {
        public List<TowFrame> Frames { get; init; } = new();
        public int Count => Frames.Count;
        public DateTime FirstAt { get; init; }
        public DateTime LastAt { get; init; }
        public double FirstLat { get; init; }
        public double FirstLon { get; init; }
        public double LastLat { get; init; }
        public double LastLon { get; init; }
        public double MaxSpeedKph { get; init; }
        public double DistanceMeters { get; init; }

        public static TowRun From(List<TowFrame> frames)
        {
            if (frames.Count == 0) return new TowRun();
            double dist = 0, maxSpeed = 0;
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].SpeedKph > maxSpeed) maxSpeed = frames[i].SpeedKph;
                if (i > 0)
                    dist += Haversine(frames[i - 1].Latitude, frames[i - 1].Longitude,
                                      frames[i].Latitude, frames[i].Longitude);
            }
            return new TowRun
            {
                Frames = frames,
                FirstAt = frames[0].RecordedAt,
                LastAt = frames[^1].RecordedAt,
                FirstLat = frames[0].Latitude,
                FirstLon = frames[0].Longitude,
                LastLat = frames[^1].Latitude,
                LastLon = frames[^1].Longitude,
                MaxSpeedKph = maxSpeed,
                DistanceMeters = dist,
            };
        }
    }
}

/// <summary>Flat row for <c>SqlQueryRaw</c> — one qualifying telemetry frame.</summary>
public class TowFrame
{
    public int DeviceId { get; set; }
    public DateTime RecordedAt { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double SpeedKph { get; set; }
}
