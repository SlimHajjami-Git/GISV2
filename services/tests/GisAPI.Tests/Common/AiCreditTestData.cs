using GisAPI.Domain.Entities;

namespace GisAPI.Tests.Common;

/// <summary>
/// Depuis le 22/09/2026, tout appel au modèle passe par le crédit IA de la société
/// (AiCredit) : une société INTROUVABLE vaut crédit nul, donc refus 403. Les tests qui
/// exercent l'assistant IA sans se soucier du crédit déclarent leur société avec le crédit
/// par défaut (60 000 jetons, aucune consommation) par cet appel.
/// </summary>
public static class AiCreditTestData
{
    /// <summary>Ajoute la société (crédit par défaut) si elle n'existe pas encore.</summary>
    public static void EnsureSocieteAvecCredit(TestGisDbContext ctx, int companyId, int? monthlyTokens = null)
    {
        if (ctx.Societes.Any(s => s.Id == companyId))
            return;
        ctx.Societes.Add(new Societe
        {
            Id = companyId,
            Name = "Société de test",
            SubscriptionStatus = "active",
            IsActive = true,
            InvoiceScanMonthlyTokens = monthlyTokens
        });
        ctx.SaveChanges();
    }
}
