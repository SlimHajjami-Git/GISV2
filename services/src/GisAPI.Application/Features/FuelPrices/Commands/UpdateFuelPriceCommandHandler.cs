using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelPrices.Commands;

public class UpdateFuelPriceCommandHandler : IRequestHandler<UpdateFuelPriceCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpdateFuelPriceCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<bool> Handle(UpdateFuelPriceCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");

        var fuelPrice = await _context.FuelPricings
            .FirstOrDefaultAsync(fp => fp.Id == request.Id && fp.CompanyId == companyId, cancellationToken);

        if (fuelPrice == null)
            return false;

        // Validate fuel type exists
        var fuelTypeExists = await _context.FuelTypes
            .AnyAsync(ft => ft.Id == request.FuelTypeId, cancellationToken);
        
        if (!fuelTypeExists)
            throw new ArgumentException($"Fuel type with ID {request.FuelTypeId} not found");

        var effectiveFrom = request.EffectiveFrom.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(request.EffectiveFrom, DateTimeKind.Utc)
            : request.EffectiveFrom.ToUniversalTime();

        DateTime? effectiveTo = null;
        if (request.EffectiveTo.HasValue)
        {
            effectiveTo = request.EffectiveTo.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(request.EffectiveTo.Value, DateTimeKind.Utc)
                : request.EffectiveTo.Value.ToUniversalTime();
        }

        fuelPrice.FuelTypeId = request.FuelTypeId;
        fuelPrice.PricePerLiter = request.PricePerLiter;
        fuelPrice.EffectiveFrom = effectiveFrom;
        fuelPrice.EffectiveTo = effectiveTo;
        fuelPrice.IsActive = request.IsActive;
        fuelPrice.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
