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
        return await _context.FuelTypes
            .OrderBy(ft => ft.Name)
            .Select(ft => new FuelTypeDto(
                ft.Id,
                ft.Code,
                ft.Name,
                ft.IsSystem
            ))
            .ToListAsync(cancellationToken);
    }
}
