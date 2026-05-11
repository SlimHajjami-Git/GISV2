using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Battery-death watcher for NEMS L (<c>protocol_type = 'gps_type_1'</c>)
/// devices. Single, deliberately conservative signal: notify only when
/// the vehicle's battery is <b>actually dead</b> — not weakening, not
/// charging slowly, not silent. Just dead.
///
/// <para>The rule: when ≥20 % of recent (last 7 d) ignition-off frames
/// report voltage <b>below 11.9 V</b>, the battery has lost the
/// ability to hold charge and cold-cranking is essentially impossible
/// (lead-acid industry threshold — Midtronics, Optima, AAMCO). That is
/// the only condition we push a notification for.</para>
///
/// <para><b>What we deliberately do NOT do</b>:</para>
/// <list type="bullet">
///   <item><description>No "saturated silence" alert. A vehicle silent
///     for 24 h+ might genuinely have a dead boîtier — or it might be in
///     a long-term parking, in the workshop, or at the dealership
///     waiting for sale. The frontend already has a passive
///     "Véhicules hors ligne" bell for that; pushing FCM notifications
///     for it created false alarms on perfectly healthy parked
///     vehicles.</description></item>
///   <item><description>No "alternator suspect" / "charging voltage low"
///     signal. The firmware saturates at ~12.9 V so we cannot reliably
///     measure charging behaviour from this sensor.</description></item>
///   <item><description>No "resting voltage decline" / "battery aging"
///     signal. With a saturated firmware the baseline is always 12.9 V,
///     so the delta-from-baseline check never fires meaningfully.</description></item>
/// </list>
///
/// <para><b>Cooldown</b>: 48 h via <c>GpsDevice.LastVoltageHealthAlertAt</c>.
/// <b>Fan-out</b>: <see cref="BatteryHealthAlertEvent"/> via MediatR.
/// <b>Vehicle filter</b>: skips vehicles where <c>Vehicle.IsImmobilized = true</c>
/// (operator-set "out of service" flag — see Vehicle entity).</para>
/// </summary>
public class VoltageHealthMonitoringService : BackgroundService
{
    // Raw byte → volts conversion (matches the Rust ingest's 0.3 factor
    // in redis_cache.rs).
    private const double RawToVoltsFactor = 0.3;

    // Industry "battery dead" threshold: below 11.9 V at rest a 12 V
    // lead-acid battery cannot reliably crank the engine.
    // 11.9 V / 0.3 = 39.67 → byte ≤ 39 means voltage ≤ 11.7 V which is
    // strictly under 11.9 V. Byte 40 corresponds to 12.0 V which we
    // consider weak but not yet dead.
    private const int RestingDeadByteThreshold = 39;       // 11.7 V

    // A handful of bad readings doesn't condemn a battery — we need a
    // sustained pattern. Empirical: a healthy vehicle with one cold
    // morning shows < 5 low readings on a 7-day window; a dead battery
    // shows 30 %+ low.
    private const int MinDeadFramesForAlert = 10;
    private const double DeadFramesMinShare = 0.20;

    // Statistical floor — don't fire on a device that has barely streamed.
    private const int MinRestingFrames = 50;

    // Recent-window for the analysis.
    private const int RecentDays = 7;

    // Service cadence.
    private const int CycleMinutes = 60;
    private const int StartupDelayMinutes = 3;
    private const int CooldownHours = 48;
    private const string NemsLProtocol = "gps_type_1";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VoltageHealthMonitoringService> _logger;

    public VoltageHealthMonitoringService(
        IServiceProvider serviceProvider,
        ILogger<VoltageHealthMonitoringService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), ct); }
        catch (TaskCanceledException) { return; }

        _logger.LogInformation(
            "VoltageHealthMonitoringService started (cycle={CycleMin}min, cooldown={Cooldown}h, threshold<11.9V)",
            CycleMinutes, CooldownHours);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "VoltageHealthMonitoringService cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(CycleMinutes), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var now = DateTime.UtcNow;
        var cooldownCutoff = now.AddHours(-CooldownHours);

        var candidates = await context.GpsDevices
            .IgnoreQueryFilters()
            .Include(d => d.Vehicle)
            .Where(d => d.ProtocolType == NemsLProtocol
                     && d.Vehicle != null
                     && !d.Vehicle.IsImmobilized
                     && (d.LastVoltageHealthAlertAt == null
                         || d.LastVoltageHealthAlertAt < cooldownCutoff))
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        _logger.LogDebug(
            "VoltageHealthMonitoringService: scanning {Count} NEMS L device(s)",
            candidates.Count);

        int flagged = 0;
        foreach (var device in candidates)
        {
            try
            {
                var hit = await EvaluateAsync(context, device, now, ct);
                if (hit == null) continue;

                device.LastVoltageHealthAlertAt = now;
                device.UpdatedAt = now;
                await context.SaveChangesAsync(ct);
                flagged++;

                _logger.LogInformation(
                    "VoltageHealth: {Signal} on device {DeviceId} ({Plate}) — observed={Observed:F2}V",
                    hit.SignalKind, device.Id,
                    VehicleLabel(device.Vehicle) ?? "?",
                    hit.VoltageObservedV ?? 0);

                await mediator.Publish(new BatteryHealthAlertEvent(
                    CompanyId: device.CompanyId,
                    DeviceId: device.Id,
                    VehicleId: device.Vehicle?.Id,
                    VehicleName: VehicleLabel(device.Vehicle),
                    SignalKind: hit.SignalKind,
                    Severity: hit.Severity,
                    Description: hit.Description,
                    VoltageObservedV: hit.VoltageObservedV,
                    VoltageBaselineV: hit.VoltageBaselineV,
                    DetectedAt: now
                ), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "VoltageHealthMonitoringService: failed to evaluate device {DeviceId}",
                    device.Id);
            }
        }

        if (flagged > 0)
        {
            _logger.LogInformation(
                "VoltageHealthMonitoringService: flagged {Flagged}/{Total} device(s) this cycle",
                flagged, candidates.Count);
        }
    }

    /// <summary>
    /// Single signal: ≥20 % of recent resting frames show voltage strictly
    /// under 11.9 V. Returns null when the battery is healthy (or there's
    /// not enough data to decide).
    /// </summary>
    private static async Task<HealthHit?> EvaluateAsync(
        GisDbContext context,
        GpsDevice device,
        DateTime now,
        CancellationToken ct)
    {
        var deviceId = device.Id;
        var recentStart = now.AddDays(-RecentDays);

        // Count BOTH the total resting frames and the subset reporting
        // ≤ 11.7 V (byte ≤ 39 = strictly under the 11.9 V "dead" threshold).
        var counts = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= recentStart
                     && p.IgnitionOn == false
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .GroupBy(p => p.PowerVoltage!.Value <= RestingDeadByteThreshold)
            .Select(g => new { IsDead = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var totalRest = counts.Sum(c => c.Count);
        var deadRest = counts.FirstOrDefault(c => c.IsDead)?.Count ?? 0;

        if (totalRest < MinRestingFrames
            || deadRest < MinDeadFramesForAlert
            || (double)deadRest / totalRest < DeadFramesMinShare)
        {
            return null;
        }

        // Pick the median voltage among the dead frames so the operator
        // sees how far gone the battery is, not just the threshold.
        var deadVoltagesRaw = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= recentStart
                     && p.IgnitionOn == false
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0
                     && p.PowerVoltage.Value <= RestingDeadByteThreshold)
            .Select(p => p.PowerVoltage!.Value)
            .ToListAsync(ct);

        var medianDeadV = Median(deadVoltagesRaw) * RawToVoltsFactor;
        var sharePct = 100.0 * deadRest / totalRest;

        return new HealthHit(
            SignalKind: "battery_dead",
            Severity: "critical",
            Description:
                $"Batterie morte: {deadRest}/{totalRest} trames sous 11.9 V " +
                $"({sharePct:F0}%), médiane {medianDeadV:F2} V",
            VoltageObservedV: medianDeadV,
            VoltageBaselineV: null);
    }

    private static string? VehicleLabel(Vehicle? vehicle)
    {
        if (vehicle == null) return null;
        if (!string.IsNullOrWhiteSpace(vehicle.Plate)) return vehicle.Plate;
        if (!string.IsNullOrWhiteSpace(vehicle.Name)) return vehicle.Name;
        return null;
    }

    /// <summary>
    /// Median of an unsorted integer list. Robust to spike outliers in a
    /// way that <c>Average()</c> isn't — a single byte=5 glitch can't
    /// shift a median computed over dozens of byte=38 readings.
    /// </summary>
    private static double Median(IReadOnlyList<int> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    private sealed record HealthHit(
        string SignalKind,
        string Severity,
        string Description,
        double? VoltageObservedV,
        double? VoltageBaselineV);
}
