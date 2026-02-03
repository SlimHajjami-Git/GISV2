using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelPrices.Commands;

public class DeleteFuelPriceCommandHandler : IRequestHandler<DeleteFuelPriceCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeleteFuelPriceCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<bool> Handle(DeleteFuelPriceCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");

        var fuelPrice = await _context.FuelPricings
            .FirstOrDefaultAsync(fp => fp.Id == request.Id && fp.CompanyId == companyId, cancellationToken);

        if (fuelPrice == null)
            return false;

        _context.FuelPricings.Remove(fuelPrice);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
