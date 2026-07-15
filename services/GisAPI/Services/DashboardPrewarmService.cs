using GisAPI.Application.Common.Interfaces;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Pré-chauffage du dashboard : toutes les ~5 minutes, recalcule en arrière-plan
/// le dashboard « admin » (company-wide) de chaque société active pour les
/// périodes courantes, et le range dans le cache.
///
/// Objectif : l'utilisateur ne paie JAMAIS le coût de calcul (mesuré ~7 s sur la
/// plus grosse flotte) — il reçoit toujours la version en cache, instantanément.
/// Le coût se paie ici, en tâche de fond, hors du chemin de la requête. Les admins
/// d'une même société partagent la clé de cache (dashboard_all_{companyId}_{period}),
/// donc un seul recalcul par société les couvre tous.
/// </summary>
public class DashboardPrewarmService : BackgroundService
{
    private const int RefreshMinutes = 5;
    private const int StartupDelaySeconds = 45;
    // TTL du cache légèrement supérieur à l'intervalle de rafraîchissement pour
    // qu'une clé ne « retombe » jamais froide entre deux passes.
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(RefreshMinutes + 3);

    // Les périodes réellement consultées par le dashboard (defaut = year).
    private static readonly string[] Periods = { "year", "month", "week", "today" };

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DashboardPrewarmService> _logger;

    public DashboardPrewarmService(IServiceProvider serviceProvider, ILogger<DashboardPrewarmService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(StartupDelaySeconds), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Dashboard prewarm cycle failed"); }

            try { await Task.Delay(TimeSpan.FromMinutes(RefreshMinutes), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var dashboard = scope.ServiceProvider.GetRequiredService<IDashboardService>();
        var cache = scope.ServiceProvider.GetRequiredService<IDashboardCache>();

        // Sociétés « actives » : abonnement non suspendu/annulé (inutile de chauffer
        // un dashboard que personne ne peut ouvrir) ET ayant au moins un utilisateur.
        var activeCompanyIds = await db.Users.AsNoTracking()
            .Where(u => u.Status == "active")
            .Select(u => u.CompanyId)
            .Distinct()
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var today = now.Date;
        int warmed = 0;

        foreach (var companyId in activeCompanyIds)
        {
            foreach (var period in Periods)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    var (periodStart, periodEnd, prevStart, prevEnd) = DashboardService.GetPeriodRange(now, period);
                    var result = await dashboard.ComputeDashboardAllAsync(
                        companyId, userId: 0, isAdmin: true, period,
                        isCustomRange: false, periodStart, periodEnd, prevStart, prevEnd, now, today, ct);

                    // Même clé que le contrôleur pour les admins.
                    cache.Set($"dashboard_all_{companyId}_{period}", result, CacheTtl);
                    warmed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Prewarm failed for company {CompanyId} period {Period}", companyId, period);
                }
            }
        }

        _logger.LogInformation("Dashboard prewarm: {Warmed} entrées chauffées ({Companies} sociétés)", warmed, activeCompanyIds.Count);
    }
}
