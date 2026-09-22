using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.AiCredits;

namespace GisAPI.Application.Features.Societes.Commands.SetSocieteScanQuota;

/// <summary>
/// Fixe le crédit IA MENSUEL d'une société, en jetons (sys admin) — depuis le 22/09/2026 il
/// couvre toute l'IA de la société (scans, assistant, rapports IA), pas seulement le scan.
/// <paramref name="MonthlyTokens"/> : null = revenir au défaut plateforme
/// (<see cref="AiCredit.DefaultMonthlyTokens"/>), 0 = désactiver l'IA pour la société,
/// sinon 1..<see cref="AiCredit.MaxMonthlyTokens"/>.
/// Rend le crédit du mois tel qu'il s'applique désormais (budget, consommation, ventilation,
/// recharge), pour que la fiche redessine sa barre sans relire toute la société.
/// </summary>
public record SetSocieteScanQuotaCommand(int Id, int? MonthlyTokens) : ICommand<AiCreditStatus>;
