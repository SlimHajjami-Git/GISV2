using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers.Queries;

public class GetSupplierStatsQueryHandler : IRequestHandler<GetSupplierStatsQuery, SupplierStatsDto>
{
    private readonly IGisDbContext _context;

    public GetSupplierStatsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<SupplierStatsDto> Handle(GetSupplierStatsQuery request, CancellationToken cancellationToken)
    {
        var suppliers = await _context.Suppliers.ToListAsync(cancellationToken);

        var total = suppliers.Count;
        var active = suppliers.Count(s => s.IsActive);
        var inactive = total - active;
        var avgRating = suppliers.Any(s => s.Rating > 0) 
            ? suppliers.Where(s => s.Rating > 0).Average(s => s.Rating) 
            : 0m;

        var byType = suppliers
            .GroupBy(s => s.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        return new SupplierStatsDto(
            total,
            active,
            inactive,
            Math.Round(avgRating, 1),
            byType
        );
    }
}
