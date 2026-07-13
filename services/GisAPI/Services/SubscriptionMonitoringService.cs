using GisAPI.Application.Common;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Supervision des abonnements côté plateforme : une fois par jour, envoie aux
/// comptes sys_admin un DIGEST des sociétés à surveiller —
/// <list type="bullet">
///   <item><description>abonnements qui expirent sous 30 jours (montant dû affiché) ;</description></item>
///   <item><description>abonnements expirés en période de grâce (7 j — voir
///     <see cref="SubscriptionPolicy"/>) = facture considérée IMPAYÉE ;</description></item>
///   <item><description>abonnements bloqués (grâce écoulée) toujours impayés.</description></item>
/// </list>
/// Une seule notification par sys_admin et par jour (déduplication par type
/// 'subscription_digest' sur la date du jour) — pas de spam, pas de table d'état.
/// Le détail temps réel est disponible dans l'admin via /api/admin/billing/overview.
/// </summary>
public class SubscriptionMonitoringService : BackgroundService
{
    private const int CycleHours = 6;          // re-vérifie 4×/jour, n'envoie qu'une fois
    private const int StartupDelayMinutes = 2;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SubscriptionMonitoringService> _logger;

    public SubscriptionMonitoringService(IServiceProvider serviceProvider, ILogger<SubscriptionMonitoringService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Subscription monitoring cycle failed"); }

            try { await Task.Delay(TimeSpan.FromHours(CycleHours), stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var now = DateTime.UtcNow;
        var societes = await db.Societes.AsNoTracking()
            .Where(s => s.SubscriptionExpiresAt != null)
            .ToListAsync(ct);

        var atRisk = societes
            .Select(s => (Societe: s, State: SubscriptionPolicy.Evaluate(s, now)))
            .Where(x => x.State.Level is "warning" or "danger" ||
                        (x.State.Level == "blocked" && x.State.Reason == "expired"))
            .OrderBy(x => x.State.DaysRemaining)
            .ToList();

        if (atRisk.Count == 0) return;

        var sysAdmins = await db.Users.AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.Status == "active" && u.Role != null && u.Role.IsSystemRole)
            .Select(u => new { u.Id, u.CompanyId })
            .ToListAsync(ct);
        if (sysAdmins.Count == 0) return;

        var today = now.Date;
        var lines = atRisk.Take(10).Select(x =>
        {
            var s = x.Societe;
            var amount = s.NextPaymentAmount is decimal a and > 0 ? $" — {a:N0} à encaisser" : "";
            return x.State.Reason switch
            {
                "grace" => $"{s.Name} : EXPIRÉ, grâce {x.State.GraceDaysLeft} j restants (impayé{amount})",
                "expired" => $"{s.Name} : BLOQUÉ, expiré depuis {-x.State.DaysRemaining} j (impayé{amount})",
                _ => $"{s.Name} : expire dans {x.State.DaysRemaining} j{amount}",
            };
        }).ToList();
        if (atRisk.Count > 10) lines.Add($"… et {atRisk.Count - 10} autres");

        var title = $"Abonnements : {atRisk.Count} société(s) à surveiller";
        var message = string.Join("\n", lines);

        foreach (var admin in sysAdmins)
        {
            // Une seule notification par admin et par jour.
            var alreadySent = await db.Notifications.AsNoTracking()
                .AnyAsync(n => n.UserId == admin.Id && n.Type == "subscription_digest" && n.CreatedAt >= today, ct);
            if (alreadySent) continue;

            try
            {
                await notifications.CreateAndSendAsync(
                    companyId: admin.CompanyId,
                    userId: admin.Id,
                    type: "subscription_digest",
                    title: title,
                    message: message,
                    priority: atRisk.Any(x => x.State.Level == "blocked" || x.State.Reason == "grace") ? "high" : "normal",
                    actionUrl: "/admin/clients",
                    ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send subscription digest to sys_admin {UserId}", admin.Id);
            }
        }

        _logger.LogInformation("Subscription digest: {Count} société(s) à risque signalées aux sys_admins", atRisk.Count);
    }
}
