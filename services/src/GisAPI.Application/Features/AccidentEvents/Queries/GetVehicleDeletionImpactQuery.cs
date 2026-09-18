using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AccidentEvents.Queries;

/// <summary>
/// Ce que la suppression d'un véhicule emporte, et ce qu'elle laisse (recette du
/// 18/09/2026) : l'écran d'administration annonçait « toutes les données associées
/// seront perdues » sans dire lesquelles.
///
/// <para>Règles réelles, lues dans <c>VehicleDeletionHelper</c> :</para>
/// <list type="bullet">
///   <item>les dossiers de SINISTRE survivent, détachés du véhicule (leur libellé est
///     figé avant) — d'où le compteur, un dossier détaché reste consultable ;</item>
///   <item>les réparations et les dépenses portent une colonne <c>vehicle_id</c> NON
///     NULL : elles sont SUPPRIMÉES avec le véhicule, et leur montant quitte les
///     rapports de coûts.</item>
/// </list>
///
/// <para>Trois <c>COUNT</c> sur des colonnes indexées : la fenêtre de confirmation ne
/// doit pas attendre.</para>
/// </summary>
public record GetVehicleDeletionImpactQuery(int VehicleId) : IRequest<VehicleDeletionImpactDto?>;

/// <param name="Accidents">Dossiers de sinistre conservés, détachés du véhicule.</param>
/// <param name="Repairs">Réparations supprimées avec le véhicule.</param>
/// <param name="Costs">Dépenses supprimées avec le véhicule.</param>
public record VehicleDeletionImpactDto(int VehicleId, int Accidents, int Repairs, int Costs);

public class GetVehicleDeletionImpactQueryHandler
    : IRequestHandler<GetVehicleDeletionImpactQuery, VehicleDeletionImpactDto?>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public GetVehicleDeletionImpactQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<VehicleDeletionImpactDto?> Handle(GetVehicleDeletionImpactQuery request, CancellationToken ct)
    {
        var vehicle = await _context.Vehicles.AsNoTracking()
            .Where(v => v.Id == request.VehicleId)
            .Select(v => new { v.Id, v.CompanyId })
            .FirstOrDefaultAsync(ct);
        if (vehicle == null) return null;

        // L'écran d'administration travaille sur toutes les sociétés ; appelée depuis
        // l'espace client, la requête reste bornée à la société de l'appelant.
        if (!_tenant.IsSystemAdmin && vehicle.CompanyId != (_tenant.CompanyId ?? 0)) return null;

        // Société du VÉHICULE dans chaque compte : l'administrateur système contourne
        // les filtres de requête, et repairs n'en a pas du tout (sa clé est societe_id).
        var accidents = await _context.AccidentEvents.AsNoTracking()
            .CountAsync(e => e.VehicleId == vehicle.Id && e.CompanyId == vehicle.CompanyId, ct);
        var repairs = await _context.Repairs.AsNoTracking()
            .CountAsync(r => r.VehicleId == vehicle.Id && r.SocieteId == vehicle.CompanyId, ct);
        var costs = await _context.VehicleCosts.AsNoTracking()
            .CountAsync(c => c.VehicleId == vehicle.Id && c.CompanyId == vehicle.CompanyId, ct);

        return new VehicleDeletionImpactDto(vehicle.Id, accidents, repairs, costs);
    }
}
