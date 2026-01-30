using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.FuelTypes.Commands;

public class SetFuelPriceCommandHandler : IRequestHandler<SetFuelPriceCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public SetFuelPriceCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<int> Handle(SetFuelPriceCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        // Validate fuel type exists
        var fuelTypeExists = await _context.FuelTypes.AnyAsync(ft => ft.Id == request.FuelTypeId, cancellationToken);
        if (!fuelTypeExists)
            throw new InvalidOperationException($"Fuel type with ID {request.FuelTypeId} not found");

        var effectiveFrom = request.EffectiveFrom ?? DateTime.UtcNow;

        // Deactivate any existing active pricing for this fuel type that starts on or after the new effective date
        var existingPricings = await _context.FuelPricings
            .Where(fp => fp.CompanyId == companyId && 
                         fp.FuelTypeId == request.FuelTypeId && 
                         fp.IsActive &&
                         (fp.EffectiveTo == null || fp.EffectiveTo > effectiveFrom))
            .ToListAsync(cancellationToken);

        foreach (var pricing in existingPricings)
        {
            if (pricing.EffectiveFrom >= effectiveFrom)
            {
                pricing.IsActive = false;
            }
            else
            {
                pricing.EffectiveTo = effectiveFrom;
            }
        }

        // Create new pricing
        var newPricing = new FuelPricing
        {
            CompanyId = companyId,
            FuelTypeId = request.FuelTypeId,
            PricePerLiter = request.PricePerLiter,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = null,
            IsActive = true
        };

        _context.FuelPricings.Add(newPricing);
        await _context.SaveChangesAsync(cancellationToken);

        return newPricing.Id;
    }
}



