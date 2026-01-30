using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.FuelTypes.Queries;

public class GetFuelPricingHistoryQueryHandler : IRequestHandler<GetFuelPricingHistoryQuery, List<FuelPricingDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetFuelPricingHistoryQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<FuelPricingDto>> Handle(GetFuelPricingHistoryQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var pricings = await _context.FuelPricings
            .Where(fp => fp.CompanyId == companyId && fp.FuelTypeId == request.FuelTypeId)
            .Join(_context.FuelTypes,
                fp => fp.FuelTypeId,
                ft => ft.Id,
                (fp, ft) => new FuelPricingDto(
                    fp.Id,
                    fp.FuelTypeId,
                    ft.Name,
                    fp.PricePerLiter,
                    fp.EffectiveFrom,
                    fp.EffectiveTo,
                    fp.IsActive,
                    fp.CreatedAt
                ))
            .OrderByDescending(fp => fp.EffectiveFrom)
            .ToListAsync(cancellationToken);

        return pricings;
    }
}



