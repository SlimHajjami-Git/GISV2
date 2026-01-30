using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.SpeedLimits.Commands;

public class SetVehicleSpeedLimitCommandHandler : IRequestHandler<SetVehicleSpeedLimitCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public SetVehicleSpeedLimitCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(SetVehicleSpeedLimitCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        if (request.SpeedLimit < 0 || request.SpeedLimit > 300)
            throw new InvalidOperationException("Speed limit must be between 0 and 300 km/h");

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, cancellationToken)
            ?? throw new InvalidOperationException($"Vehicle with ID {request.VehicleId} not found");

        vehicle.SpeedLimit = request.SpeedLimit;
        vehicle.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }
}



