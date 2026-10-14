using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelEntries.Queries;

/// <summary>
/// Totaux de l'écran Carburant sur TOUS les pleins qui répondent aux filtres de la liste, et
/// non sur la page chargée (campagne de tests GPA du 06/10/2026, erreur n°3).
/// </summary>
public record GetFuelEntriesSummaryQuery(
    int? FuelTypeId,
    string? VehiclePlate,
    DateTime? StartDate,
    DateTime? EndDate,
    int? VehicleId = null
) : IRequest<FuelEntriesSummaryDto>;

/// <summary>
/// <paramref name="AveragePricePerLiter"/> = montant total ÷ litres : le prix moyen payé au
/// litre. La moyenne simple des prix comptait un appoint de 10 L autant qu'un plein de 60 L.
/// Null sans aucun litre.
/// </summary>
public record FuelEntriesSummaryDto(
    int Count,
    decimal TotalVolume,
    decimal TotalAmount,
    decimal? AveragePricePerLiter);

public class GetFuelEntriesSummaryQueryHandler : IRequestHandler<GetFuelEntriesSummaryQuery, FuelEntriesSummaryDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetFuelEntriesSummaryQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<FuelEntriesSummaryDto> Handle(GetFuelEntriesSummaryQuery request, CancellationToken cancellationToken)
    {
        var query = await FuelEntriesFilter.AppliquerAsync(
            _context, _tenantService, request.FuelTypeId, request.VehiclePlate,
            request.StartDate, request.EndDate, request.VehicleId, cancellationToken);

        var count = await query.CountAsync(cancellationToken);
        var volume = count == 0 ? 0m : await query.SumAsync(e => e.Volume, cancellationToken);
        var montant = count == 0 ? 0m : await query.SumAsync(e => e.TotalAmount, cancellationToken);

        return new FuelEntriesSummaryDto(
            count,
            volume,
            montant,
            volume > 0 ? Math.Round(montant / volume, 3) : null);
    }
}
