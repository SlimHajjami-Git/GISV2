using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.FuelTypes.Queries;

public class GetFuelTypesQueryHandler : IRequestHandler<GetFuelTypesQuery, List<FuelTypeDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetFuelTypesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<FuelTypeDto>> Handle(GetFuelTypesQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId;
        var now = DateTime.UtcNow;

        // Get all fuel types first
        var fuelTypes = await _context.FuelTypes
            .OrderBy(ft => ft.Name)
            .ToListAsync(cancellationToken);

        // Get current pricing for each fuel type if company is set
        Dictionary<int, (decimal Price, DateTime EffectiveFrom)> currentPrices = new();
        
        if (companyId.HasValue)
        {
            var pricings = await _context.FuelPricings
                .Where(fp => fp.CompanyId == companyId.Value && 
                             fp.IsActive &&
                             fp.EffectiveFrom <= now &&
                             (fp.EffectiveTo == null || fp.EffectiveTo > now))
                .ToListAsync(cancellationToken);

            currentPrices = pricings
                .GroupBy(fp => fp.FuelTypeId)
                .ToDictionary(
                    g => g.Key,
                    g => {
                        var latest = g.OrderByDescending(fp => fp.EffectiveFrom).First();
                        return (latest.PricePerLiter, latest.EffectiveFrom);
                    }
                );
        }

        return fuelTypes.Select(ft => new FuelTypeDto(
            ft.Id,
            ft.Code,
            ft.Name,
            ft.IsSystem,
            currentPrices.TryGetValue(ft.Id, out var pricing) ? pricing.Price : null,
            currentPrices.TryGetValue(ft.Id, out var pricing2) ? pricing2.EffectiveFrom : null
        )).ToList();
    }
}



