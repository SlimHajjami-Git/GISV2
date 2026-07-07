using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Societes.Commands.SetSocieteScanQuota;

/// <summary>
/// Fixe le quota mensuel de scans de factures IA d'une société (sys admin).
/// <paramref name="MonthlyLimit"/> : null = revenir au défaut plateforme (20),
/// 0 = désactiver la fonctionnalité, sinon 1..100000.
/// </summary>
public record SetSocieteScanQuotaCommand(int Id, int? MonthlyLimit) : ICommand<bool>;
