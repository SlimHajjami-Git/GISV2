using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Costs;

namespace GisAPI.Application.Features.Societes.Commands.SetSocieteScanQuota;

/// <summary>
/// Fixe le crédit IA MENSUEL du scan de factures d'une société, en jetons (sys admin).
/// <paramref name="MonthlyTokens"/> : null = revenir au défaut plateforme
/// (<see cref="InvoiceScanCredit.DefaultMonthlyTokens"/>), 0 = désactiver la
/// fonctionnalité, sinon 1..<see cref="InvoiceScanCredit.MaxMonthlyTokens"/>.
/// Rend le crédit du mois tel qu'il s'applique désormais (budget, consommation, recharge),
/// pour que la fiche redessine sa barre sans relire toute la société.
/// </summary>
public record SetSocieteScanQuotaCommand(int Id, int? MonthlyTokens) : ICommand<InvoiceScanCreditStatus>;
