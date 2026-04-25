using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelPrices.Queries;

public class GetFuelTypesQueryHandler : IRequestHandler<GetFuelTypesQuery, List<FuelTypeDto>>
{
    private readonly IGisDbContext _context;

    public GetFuelTypesQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<FuelTypeDto>> Handle(GetFuelTypesQuery request, CancellationToken cancellationToken)
    {
        var all = await _context.FuelTypes
            .OrderBy(ft => ft.Name)
            .ToListAsync(cancellationToken);

        // Deduplicate by normalized code/name to avoid "Même type de carburant" appearing twice
        return all
            .GroupBy(ft => (ft.Code ?? ft.Name ?? string.Empty).Trim().ToLowerInvariant())
            .Select(g => g.OrderByDescending(ft => ft.IsSystem).ThenBy(ft => ft.Id).First())
            .Select(ft => new FuelTypeDto(ft.Id, ft.Code, ft.Name, ft.IsSystem))
            .ToList();
    }
}
