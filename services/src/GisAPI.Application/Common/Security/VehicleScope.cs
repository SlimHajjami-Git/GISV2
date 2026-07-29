using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Common.Security;

/// <summary>
/// Portée des véhicules visibles par l'appelant.
///
/// Un administrateur de société voit tout le parc ; un utilisateur simple ne
/// voit que les véhicules qui lui ont été explicitement affectés (table
/// UserVehicles), et RIEN s'il n'en a aucun.
///
/// Cette logique était recopiée à la main dans chaque handler, ce qui l'a fait
/// oublier dans plusieurs écrans : les échéances, par exemple, filtraient
/// seulement par société, si bien qu'un employé restreint à deux véhicules
/// voyait les échéances de tout le parc alors que le monitoring et le playback
/// le filtraient correctement. Ce helper est la définition unique — tout écran
/// qui liste des données rattachées à un véhicule doit passer par lui.
/// </summary>
public static class VehicleScope
{
    private static readonly string[] AdminRoles =
        { "company_admin", "admin", "super_admin", "system_admin" };

    /// <summary>L'appelant voit-il l'ensemble du parc de sa société ?</summary>
    public static bool SeesWholeFleet(ICurrentTenantService tenant) =>
        tenant.UserRoles.Any(r => AdminRoles.Contains(r));

    /// <summary>
    /// Identifiants des véhicules visibles par l'appelant, ou <c>null</c>
    /// lorsqu'il voit tout le parc (auquel cas le filtre société suffit).
    /// Une liste VIDE signifie « aucun véhicule visible » et doit produire un
    /// résultat vide — surtout pas l'absence de filtre.
    /// </summary>
    public static async Task<List<int>?> AccessibleVehicleIdsAsync(
        IGisDbContext context,
        ICurrentTenantService tenant,
        CancellationToken ct)
    {
        if (SeesWholeFleet(tenant)) return null;

        var userId = tenant.UserId ?? 0;
        if (userId <= 0) return new List<int>();   // non identifié => rien

        return await context.UserVehicles
            .AsNoTracking()
            .Where(uv => uv.UserId == userId)
            .Select(uv => uv.VehicleId)
            .ToListAsync(ct);
    }
}
