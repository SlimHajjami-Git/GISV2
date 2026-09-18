using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
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

    /// <summary>
    /// L'appelant peut-il écrire sur ce véhicule ? Même règle que la lecture :
    /// un administrateur écrit sur tout le parc, un employé restreint seulement
    /// sur ses véhicules. Les écritures (réparations, pleins, entretiens,
    /// sinistres) ne filtraient que par société, alors que POST /api/costs
    /// appliquait déjà la portée : un employé limité au véhicule A pouvait
    /// imputer une réparation au véhicule B en forgeant l'appel.
    /// </summary>
    public static async Task<bool> CanAccessVehicleAsync(
        IGisDbContext context,
        ICurrentTenantService tenant,
        int vehicleId,
        CancellationToken ct)
    {
        var scope = await AccessibleVehicleIdsAsync(context, tenant, ct);
        return scope is null || scope.Contains(vehicleId);
    }

    /// <summary>
    /// Refuse l'écriture sur un véhicule hors portée par un 404 : même réponse
    /// qu'un véhicule inexistant, pour ne jamais révéler qu'il existe.
    /// </summary>
    public static async Task EnsureCanWriteAsync(
        IGisDbContext context,
        ICurrentTenantService tenant,
        int vehicleId,
        string notFoundMessage,
        CancellationToken ct)
    {
        if (!await CanAccessVehicleAsync(context, tenant, vehicleId, ct))
            throw new NotFoundException(notFoundMessage);
    }
}
