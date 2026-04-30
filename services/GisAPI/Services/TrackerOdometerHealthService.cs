using GisAPI.Application.Common.Interfaces;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Calypso 7 (P-maint-couche4) — daily health check that flags vehicles
/// whose tracker stopped (or never started) reporting an FMS odometer.
///
/// <para>Detection rule (per active vehicle that has a <c>GpsDeviceId</c>):</para>
/// <list type="number">
///   <item><description>The vehicle has at least <c>MinPositionsForCheck</c>
///     GPS positions recorded in the last <c>WindowDays</c> days
///     (proves the tracker is alive and the truck is moving).</description></item>
///   <item><description>≥ <c>SilentRatioThreshold</c> of those positions have
///     <c>odometer_km IS NULL OR &lt;= 0 OR == 1048574</c> (the FMS sentinel
///     for "no CAN bus odometer"). This means the tracker is online but
///     not reporting a real odometer.</description></item>
/// </list>
///
/// <para>When the rule fires, every active company admin receives a notification
/// (type <c>tracker_odometer_silent</c>) explaining that:</para>
/// <list type="bullet">
///   <item><description>Scheduled maintenances on this vehicle now use the
///     <b>trips total</b> as their km source (the smart resolver in
///     <see cref="Application.Services.MaintenanceSchedulerService"/>).</description></item>
///   <item><description>The CAN bus / FMS link should be inspected if a
///     real odometer is needed (e.g. for fuel-rate computation, certain
///     report KPIs).</description></item>
/// </list>
///
/// <para>Dedup: an admin gets at most one notification per vehicle per
/// <c>DedupDays</c> days. A persistent silence is reported once a week, not
/// once an hour.</para>
///
/// <para>This service is the canary: when it fires, the operator is told the
/// fallback chain has kicked in. The maintenance system itself keeps working
/// thanks to the trips fallback in <c>GetCurrentMileageAsync</c>.</para>
/// </summary>
public class TrackerOdometerHealthService : BackgroundService
{
    private const int CycleHours = 24;
    private const int StartupDelayMinutes = 5;
    private const int WindowDays = 3;
    private const int DedupDays = 7;
    private const int MinPositionsForCheck = 30;
    private const double SilentRatioThreshold = 0.9;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TrackerOdometerHealthService> _logger;

    public TrackerOdometerHealthService(
        IServiceProvider serviceProvider,
        ILogger<TrackerOdometerHealthService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), ct); }
        catch (TaskCanceledException) { return; }

        _logger.LogInformation(
            "TrackerOdometerHealthService started (cycle={Cycle}h, window={Window}d, ratio={Ratio:P0})",
            CycleHours, WindowDays, SilentRatioThreshold);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "TrackerOdometerHealthService cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromHours(CycleHours), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var notifService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var since = DateTime.UtcNow.AddDays(-WindowDays);
        var dedupSince = DateTime.UtcNow.AddDays(-DedupDays);

        // Aggregate odometer health per device over the rolling window.
        // We do this in one round-trip with a GROUP BY rather than N queries.
        var perDevice = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.RecordedAt >= since)
            .GroupBy(p => p.DeviceId)
            .Select(g => new
            {
                DeviceId = g.Key,
                Total = g.Count(),
                Silent = g.Count(p => !p.OdometerKm.HasValue
                                   || p.OdometerKm <= 0
                                   || p.OdometerKm == 1048574),
            })
            .Where(x => x.Total >= MinPositionsForCheck)
            .ToListAsync(ct);

        if (perDevice.Count == 0)
        {
            _logger.LogDebug("TrackerOdometerHealthService: no devices with enough positions in the window");
            return;
        }

        var silentDeviceIds = perDevice
            .Where(x => (double)x.Silent / x.Total >= SilentRatioThreshold)
            .Select(x => x.DeviceId)
            .ToList();

        if (silentDeviceIds.Count == 0)
        {
            _logger.LogDebug("TrackerOdometerHealthService: all {Count} active trackers report odometer", perDevice.Count);
            return;
        }

        // Resolve vehicles linked to silent devices.
        var vehicles = await context.Vehicles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.GpsDeviceId.HasValue && silentDeviceIds.Contains(v.GpsDeviceId.Value))
            .Select(v => new
            {
                v.Id,
                v.CompanyId,
                v.Name,
                v.Plate,
                v.GpsDeviceId,
            })
            .ToListAsync(ct);

        if (vehicles.Count == 0)
        {
            _logger.LogDebug("TrackerOdometerHealthService: silent devices not linked to any vehicle");
            return;
        }

        // Group by company so we can fetch admins once per tenant.
        var byCompany = vehicles.GroupBy(v => v.CompanyId).ToList();
        var totalNotifs = 0;

        foreach (var companyGroup in byCompany)
        {
            var companyId = companyGroup.Key;

            var admins = await context.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(u => u.Role)
                .Where(u => u.CompanyId == companyId
                         && u.Status == "active"
                         && u.Role != null
                         && u.Role.IsCompanyAdmin)
                .Select(u => u.Id)
                .ToListAsync(ct);

            if (admins.Count == 0) continue;

            foreach (var v in companyGroup)
            {
                // Dedup: skip if any admin of this company already received a
                // tracker_odometer_silent notification for this vehicle within
                // the dedup window.
                var alreadyNotified = await context.Notifications
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(n => n.CompanyId == companyId
                                && n.Type == "tracker_odometer_silent"
                                && n.ReferenceType == "vehicle"
                                && n.ReferenceId == v.Id
                                && n.CreatedAt >= dedupSince, ct);

                if (alreadyNotified) continue;

                var label = string.IsNullOrWhiteSpace(v.Plate) ? (v.Name ?? "véhicule") : v.Plate;
                var title = $"Odomètre GPS muet — {label}";
                var message =
                    $"Le tracker du véhicule {label} reçoit des positions mais ne remonte plus l'odomètre " +
                    $"(>= {SilentRatioThreshold:P0} de trames sans odomètre sur les {WindowDays} derniers jours). " +
                    "Les entretiens programmables continuent de fonctionner via la somme des trajets GPS, " +
                    "mais le câblage CAN bus / FMS du véhicule doit être vérifié pour récupérer la source d'origine.";

                var metadata = new Dictionary<string, object>
                {
                    ["vehicleId"] = v.Id,
                    ["vehicleLabel"] = label,
                    ["gpsDeviceId"] = v.GpsDeviceId ?? 0,
                    ["windowDays"] = WindowDays,
                };

                foreach (var adminId in admins)
                {
                    try
                    {
                        await notifService.CreateAndSendAsync(
                            companyId: companyId,
                            userId: adminId,
                            type: "tracker_odometer_silent",
                            title: title,
                            message: message,
                            priority: "normal",
                            referenceType: "vehicle",
                            referenceId: v.Id,
                            actionUrl: $"/vehicles?vehicleId={v.Id}",
                            metadata: metadata,
                            ct: ct);
                        totalNotifs++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex,
                            "Failed to push tracker_odometer_silent to admin {Admin} for vehicle {Vehicle}",
                            adminId, v.Id);
                    }
                }
            }
        }

        if (totalNotifs > 0)
        {
            _logger.LogInformation(
                "TrackerOdometerHealthService: {Count} notification(s) sent across {Vehicles} silent vehicle(s)",
                totalNotifs, vehicles.Count);
        }
    }
}
