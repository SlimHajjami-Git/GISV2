using GisAPI.Application.Services;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Calypso 7 (P-maint-couche2) — periodic refresher for the persisted
/// <c>vehicle_maintenance_schedules.status</c> column.
///
/// <para>The maintenance subsystem has two notions of "status":</para>
/// <list type="bullet">
///   <item><description><b>Computed</b> — recalculated on-the-fly by every
///     <c>Get*</c> query handler against the latest mileage, returned in the
///     DTO. Always accurate.</description></item>
///   <item><description><b>Persisted</b> — the <c>status</c> column on the
///     row. Only ever rewritten by the explicit <c>MarkDone</c> command.
///     <c>PredictiveAlertService</c> (and any future alert pipeline) reads
///     the persisted value when it scans for "due" / "overdue" schedules
///     to fan notifications out.</description></item>
/// </list>
///
/// <para>Without this service, the persisted column drifts forever from the
/// computed reality: a vehicle quietly crosses its <c>NextDueKm</c>, but the
/// status stays "upcoming" because no <c>MarkDone</c> ever fires, and the
/// alert pipeline filters <c>WHERE status IN ('due', 'overdue', 'critical')</c>
/// — so the operator is never warned. It was the root cause behind the
/// 257 TU 6114 silent-failure scenario.</para>
///
/// <para>The service runs every 60 minutes:</para>
/// <list type="number">
///   <item><description>Resolve the company list</description></item>
///   <item><description>For each company, call
///     <see cref="IMaintenanceSchedulerService.UpdateAllScheduleStatusesAsync"/>
///     which re-reads <c>GetCurrentMileageAsync</c> for every active schedule
///     and writes <c>status</c> back when it changes. The method already
///     skips paused schedules and inactive templates.</description></item>
/// </list>
///
/// <para>The cycle interval is conservative on purpose — recalculating
/// <c>status</c> too often would create needless write traffic on
/// <c>vehicle_maintenance_schedules</c>. 60 minutes is plenty given that
/// vehicles typically take days to cross a maintenance threshold.</para>
/// </summary>
public class MaintenanceStatusRefreshService : BackgroundService
{
    private const int CycleMinutes = 60;
    private const int StartupDelayMinutes = 3;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MaintenanceStatusRefreshService> _logger;

    public MaintenanceStatusRefreshService(
        IServiceProvider serviceProvider,
        ILogger<MaintenanceStatusRefreshService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Stagger startup so we don't pile on the DB right after migrations.
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), ct); }
        catch (TaskCanceledException) { return; }

        _logger.LogInformation(
            "MaintenanceStatusRefreshService started (cycle={Cycle}min)",
            CycleMinutes);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshAllAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "MaintenanceStatusRefreshService cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(CycleMinutes), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RefreshAllAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var scheduler = scope.ServiceProvider.GetRequiredService<IMaintenanceSchedulerService>();

        var companies = await context.Societes
            .AsNoTracking()
            .Select(s => s.Id)
            .ToListAsync(ct);

        var totalUpdated = 0;
        foreach (var companyId in companies)
        {
            try
            {
                // UpdateAllScheduleStatusesAsync already filters out paused
                // schedules + inactive templates, recomputes via the smart
                // mileage resolver, and only writes when status changes.
                var updated = await scheduler.UpdateAllScheduleStatusesAsync(companyId, ct);
                totalUpdated += updated;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Failed to refresh maintenance statuses for company {CompanyId}",
                    companyId);
            }
        }

        if (totalUpdated > 0)
        {
            _logger.LogInformation(
                "MaintenanceStatusRefreshService: {Count} schedule status(es) transitioned across {Companies} compan(y/ies)",
                totalUpdated, companies.Count);
        }
        else
        {
            _logger.LogDebug(
                "MaintenanceStatusRefreshService: no transitions detected across {Companies} compan(y/ies)",
                companies.Count);
        }
    }
}
