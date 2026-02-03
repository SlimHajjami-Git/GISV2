using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelPrices.Commands;

public class CreateFuelPriceCommandHandler : IRequestHandler<CreateFuelPriceCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreateFuelPriceCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<int> Handle(CreateFuelPriceCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");

        // Convert effectiveFrom to UTC first
        var effectiveFrom = request.EffectiveFrom.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(request.EffectiveFrom, DateTimeKind.Utc)
            : request.EffectiveFrom.ToUniversalTime();

        // Validate fuel type exists
        var fuelTypeExists = await _context.FuelTypes
            .AnyAsync(ft => ft.Id == request.FuelTypeId, cancellationToken);
        
        if (!fuelTypeExists)
            throw new ArgumentException($"Fuel type with ID {request.FuelTypeId} not found");

        // Deactivate existing active prices for same fuel type if overlapping
        var overlappingPrices = await _context.FuelPricings
            .Where(fp => fp.CompanyId == companyId 
                && fp.FuelTypeId == request.FuelTypeId 
                && fp.IsActive
                && fp.EffectiveFrom <= effectiveFrom)
            .ToListAsync(cancellationToken);

        foreach (var price in overlappingPrices)
        {
            if (price.EffectiveTo == null || price.EffectiveTo > effectiveFrom)
            {
                price.EffectiveTo = effectiveFrom.AddDays(-1);
                price.UpdatedAt = DateTime.UtcNow;
            }
        }

        DateTime? effectiveTo = null;
        if (request.EffectiveTo.HasValue)
        {
            effectiveTo = request.EffectiveTo.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(request.EffectiveTo.Value, DateTimeKind.Utc)
                : request.EffectiveTo.Value.ToUniversalTime();
        }

        var fuelPrice = new FuelPricing
        {
            CompanyId = companyId,
            FuelTypeId = request.FuelTypeId,
            PricePerLiter = request.PricePerLiter,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.FuelPricings.Add(fuelPrice);
        await _context.SaveChangesAsync(cancellationToken);

        return fuelPrice.Id;
    }
}
