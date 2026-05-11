using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Background watcher that detects a sustained drop in <c>power_voltage</c>
/// on NEMS L boîtiers (<c>protocol_type = 'gps_type_1'</c>) — the only
/// devices currently deployed whose voltage reading maps linearly to the
/// vehicle's battery state. NEMS L has <b>no internal backup cell</b>, so
/// every frame the DB receives is drawn from the vehicle battery, and a
/// low voltage on several consecutive frames with the ignition on is a
/// reliable proxy for a dying battery.
///
/// <para><b>Detection rule</b> — on the 5 most recent positions of each
/// eligible device:</para>
/// <list type="bullet">
///   <item><description>At least <see cref="LowVoltageHits"/> frames with
///     <c>0 &lt; power_voltage &lt; <see cref="LowVoltageThreshold"/></c>
///     (empirically calibrated — the real-world p5 in our DB is 42, so
///     values under 35 are statistically anomalous).</description></item>
///   <item><description>At least <see cref="IgnitionOnHits"/> frames with
///     <c>ignition_on = true</c> — a parked contact-off vehicle can dip
///     low legitimately, so we only flag when the driver is actively
///     drawing from the battery.</description></item>
/// </list>
///
/// <para><b>Cooldown</b>: <see cref="CooldownHours"/> hours via
/// <c>GpsDevice.LastBatteryAlertAt</c>. Without it the service would
/// hammer the admins every 5 minutes while the battery stays weak.</para>
///
/// <para><b>Fan-out</b>: publishes a <see cref="BatteryAlertNotificationEvent"/>
/// via MediatR; <c>BatteryAlertNotificationHandler</c> resolves the
/// active company admins and calls
/// <see cref="Application.Common.Interfaces.INotificationService.CreateAndSendAsync"/>
/// (persists row + pushes via SignalR + FCM).</para>
///
/// <para><b>Deliberately excluded</b>: the <c>power_source_rescue</c>
/// metadata flag produced by the Rust parser. The NEMS L protocol byte
/// is decoded as "MSB = on backup / low 7 bits = voltage", but since the
/// boîtier has no backup cell the MSB is in fact part of a larger
/// voltage value (0xAB = 171 is a normal reading, not a backup indicator).
/// Trusting that flag would raise alerts on perfectly healthy devices.</para>
/// </summary>
public class BatteryMonitoringService : BackgroundService
{
    private const int CycleMinutes = 5;
    private const int StartupDelayMinutes = 2;
    private const int CooldownHours = 24;
    private const int LowVoltageThreshold = 35;  // raw units — empirical
    private const int RecentFramesWindow = 5;
    private const int LowVoltageHits = 4;
    private const int IgnitionOnHits = 3;
    private const string NemsLProtocol = "gps_type_1";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BatteryMonitoringService> _logger;

    public BatteryMonitoringService(
        IServiceProvider serviceProvider,
        ILogger<BatteryMonitoringService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Give the rest of the app a head start so we don't fight the
        // EF migration + DI registration at cold boot.
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), ct); }
        catch (TaskCanceledException) { return; }

        _logger.LogInformation(
            "BatteryMonitoringService started (cycle={CycleMin}min, threshold<{Threshold}, {LowHits}/{Window} frames, cooldown={Cooldown}h)",
            CycleMinutes, LowVoltageThreshold, LowVoltageHits, RecentFramesWindow, CooldownHours);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "BatteryMonitoringService cycle failed");
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

        var cooldownCutoff = DateTime.UtcNow.AddHours(-CooldownHours);

        // Eligible devices: NEMS L protocol, currently assigned to a
        // vehicle (otherwise there is no one to alert and the vehicle
        // label would be empty), and either never-alerted or past the
        // 24h cooldown window.
        var candidates = await context.GpsDevices
            .IgnoreQueryFilters()
            .Include(d => d.Vehicle)
            .Where(d => d.ProtocolType == NemsLProtocol
                     && d.Vehicle != null
                     && !d.Vehicle.IsImmobilized
                     && (d.LastBatteryAlertAt == null
                         || d.LastBatteryAlertAt < cooldownCutoff))
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        _logger.LogDebug(
            "BatteryMonitoringService: scanning {Count} NEMS L device(s) for low voltage",
            candidates.Count);

        int flagged = 0;
        foreach (var device in candidates)
        {
            try
            {
                if (await IsBatteryLowAsync(context, device.Id, ct))
                {
                    device.LastBatteryAlertAt = DateTime.UtcNow;
                    device.UpdatedAt = DateTime.UtcNow;
                    await context.SaveChangesAsync(ct);
                    flagged++;

                    _logger.LogInformation(
                        "BatteryMonitoringService: low voltage detected on device {DeviceId} ({Plate}) — alert dispatched",
                        device.Id, device.Vehicle?.Plate ?? device.Vehicle?.Name ?? "?");

                    await mediator.Publish(new BatteryAlertNotificationEvent(
                        CompanyId: device.CompanyId,
                        DeviceId: device.Id,
                        VehicleId: device.Vehicle?.Id,
                        VehicleName: VehicleLabel(device.Vehicle),
                        VoltageRaw: LowVoltageThreshold,      // last observed ceiling — the handler
                                                              // just displays "raw<35", not the exact reading
                        DetectedAt: DateTime.UtcNow
                    ), ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "BatteryMonitoringService: failed to evaluate device {DeviceId}",
                    device.Id);
            }
        }

        if (flagged > 0)
        {
            _logger.LogInformation(
                "BatteryMonitoringService: flagged {Flagged}/{Total} device(s) this cycle",
                flagged, candidates.Count);
        }
    }

    /// <summary>
    /// Pulls the 5 most recent GPS positions for the given device and
    /// applies the threshold rule in-memory. We deliberately do the
    /// predicate count in LINQ-to-Objects rather than SQL because the
    /// window is tiny (5 rows) and this keeps the query a single
    /// indexed lookup on <c>(device_id, recorded_at DESC)</c>.
    /// </summary>
    private static async Task<bool> IsBatteryLowAsync(
        GisDbContext context,
        int deviceId,
        CancellationToken ct)
    {
        var recent = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId)
            .OrderByDescending(p => p.RecordedAt)
            .Take(RecentFramesWindow)
            .Select(p => new { p.PowerVoltage, p.IgnitionOn })
            .ToListAsync(ct);

        // Don't raise an alert on a device that has barely streamed
        // anything — we need enough frames to trust the signal.
        if (recent.Count < RecentFramesWindow) return false;

        int lowHits = recent.Count(p =>
            p.PowerVoltage.HasValue
            && p.PowerVoltage.Value > 0
            && p.PowerVoltage.Value < LowVoltageThreshold);

        int ignitionHits = recent.Count(p => p.IgnitionOn == true);

        return lowHits >= LowVoltageHits && ignitionHits >= IgnitionOnHits;
    }

    private static string? VehicleLabel(Vehicle? vehicle)
    {
        if (vehicle == null) return null;
        if (!string.IsNullOrWhiteSpace(vehicle.Plate)) return vehicle.Plate;
        if (!string.IsNullOrWhiteSpace(vehicle.Name)) return vehicle.Name;
        return null;
    }
}
