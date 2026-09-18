using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reports.Common;

/// <summary>
/// Devise des montants d'un rapport : celle de la société
/// (<c>Societe.Settings.Currency</c>), sinon celle du déploiement
/// (<see cref="AppCurrency.Default"/>). Même règle que la devise renvoyée au login,
/// que l'écran applique à tous les montants.
///
/// <para>Constat de la recette du 16/09/2026 : le rapport mensuel flotte libellait
/// son coût au kilomètre avec la devise du déploiement, et un client en euros lisait
/// « TND/km » à côté de montants affichés en €.</para>
/// </summary>
public static class ReportCurrency
{
    public static string Resolve(string? companyCurrency) =>
        string.IsNullOrWhiteSpace(companyCurrency) ? AppCurrency.Default : companyCurrency.Trim();

    public static async Task<string> LoadAsync(IGisDbContext context, int companyId, CancellationToken ct)
    {
        // L'entité entière et non une projection de Settings : ce type possédé est
        // stocké en JSON, et c'est ainsi que le login le relit déjà.
        var societe = await context.Societes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == companyId, ct);
        return Resolve(societe?.Settings?.Currency);
    }
}
