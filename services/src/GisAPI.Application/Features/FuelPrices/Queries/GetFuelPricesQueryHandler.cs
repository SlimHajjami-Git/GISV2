using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelPrices.Queries;

public class GetFuelPricesQueryHandler : IRequestHandler<GetFuelPricesQuery, PaginatedList<FuelPriceDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetFuelPricesQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<PaginatedList<FuelPriceDto>> Handle(GetFuelPricesQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");

        var query = _context.FuelPricings
            .Include(fp => fp.FuelType)
            .Where(fp => fp.CompanyId == companyId)
            .AsQueryable();

        if (request.FuelTypeId.HasValue)
        {
            query = query.Where(fp => fp.FuelTypeId == request.FuelTypeId.Value);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(fp => fp.IsActive == request.IsActive.Value);
        }

        query = query.OrderByDescending(fp => fp.EffectiveFrom);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(fp => new FuelPriceDto(
                fp.Id,
                fp.FuelTypeId,
                fp.FuelType != null ? fp.FuelType.Code : "",
                fp.FuelType != null ? fp.FuelType.Name : "",
                fp.PricePerLiter,
                fp.EffectiveFrom,
                fp.EffectiveTo,
                fp.IsActive,
                fp.CreatedAt,
                fp.UpdatedAt
            ))
            .ToListAsync(cancellationToken);

        return new PaginatedList<FuelPriceDto>(items, totalCount, request.Page, request.PageSize);
    }
}
