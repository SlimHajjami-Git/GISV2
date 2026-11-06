using GisAPI.Application.Features.Payments;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Rapprochement des paiements ClicToPay (migration 067). Le manuel « Basic » n'a pas de
/// notification serveur à serveur : si le client paie puis ferme l'onglet avant l'écran de
/// retour, rien ne valide sa commande. Toutes les 3 minutes, les tentatives encore en attente
/// sont revérifiées auprès de ClicToPay ; une tentative payée valide sa commande, une tentative
/// jamais payée est close au bout de 48 h. Ne tourne pas sans identifiants marchand.
/// </summary>
public class OnlinePaymentReconciliationService : BackgroundService
{
    private static readonly TimeSpan Cycle = TimeSpan.FromMinutes(3);
    // Laisser au client le temps de revenir par l'écran de retour, qui vérifie lui-même.
    private static readonly TimeSpan MinAge = TimeSpan.FromMinutes(2);
    private const int BatchSize = 50;

    private readonly IServiceProvider _services;
    private readonly ClicToPayOptions _options;
    private readonly ILogger<OnlinePaymentReconciliationService> _logger;

    public OnlinePaymentReconciliationService(
        IServiceProvider services, ClicToPayOptions options, ILogger<OnlinePaymentReconciliationService> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogInformation("Paiement ClicToPay non configuré : rapprochement des paiements inactif.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(Cycle, stoppingToken); }
            catch (OperationCanceledException) { return; }

            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Rapprochement des paiements ClicToPay en échec");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<OnlinePaymentProcessor>();

        var before = DateTime.UtcNow - MinAge;
        // Service de fond, sans tenant : IgnoreQueryFilters, le filtre global ne décide rien ici.
        var ids = await db.OnlinePayments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Status == OnlinePayment.StatusPending && p.ProviderOrderId != null && p.CreatedAt < before)
            .OrderBy(p => p.LastCheckedAt ?? p.CreatedAt)
            .Select(p => p.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var id in ids)
        {
            try
            {
                var result = await processor.VerifyAsync(id, ct);
                if (result.Status != OnlinePayment.StatusPending)
                    _logger.LogInformation("Rapprochement ClicToPay : {Reference} → {Status}", result.Reference, result.Status);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Rapprochement ClicToPay en échec pour la tentative #{Id}", id);
            }
        }
    }
}
