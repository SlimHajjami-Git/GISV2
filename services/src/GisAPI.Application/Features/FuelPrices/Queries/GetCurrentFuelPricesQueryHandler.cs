using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelPrices.Queries;

public class GetCurrentFuelPricesQueryHandler : IRequestHandler<GetCurrentFuelPricesQuery, List<FuelPriceDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetCurrentFuelPricesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<FuelPriceDto>> Handle(GetCurrentFuelPricesQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");
        var now = DateTime.UtcNow;

        // First fetch active prices with their fuel types
        var activePrices = await _context.FuelPricings
            .Include(fp => fp.FuelType)
            .Where(fp => fp.CompanyId == companyId 
                && fp.IsActive 
                && fp.EffectiveFrom <= now 
                && (fp.EffectiveTo == null || fp.EffectiveTo >= now))
            .OrderByDescending(fp => fp.EffectiveFrom)
            .ToListAsync(cancellationToken);

        // Then group in memory and select the most recent per fuel type
        var currentPrices = activePrices
            .GroupBy(fp => fp.FuelTypeId)
            .Select(g => g.First())
            .Select(fp => new FuelPriceDto(
                fp.Id,
                fp.FuelTypeId,
                fp.FuelType?.Code ?? "",
                fp.FuelType?.Name ?? "",
                fp.PricePerLiter,
                fp.EffectiveFrom,
                fp.EffectiveTo,
                fp.IsActive,
                fp.CreatedAt,
                fp.UpdatedAt
            ))
            .ToList();

        return currentPrices;
    }
}
