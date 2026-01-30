using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Commands.PatchVehicle;

public class PatchVehicleCommandHandler : IRequestHandler<PatchVehicleCommand, Unit>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public PatchVehicleCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Unit> Handle(PatchVehicleCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.Id && v.CompanyId == companyId, cancellationToken);

        if (vehicle == null)
            throw new InvalidOperationException("Vehicle not found");

        if (request.SpeedLimit.HasValue)
            vehicle.SpeedLimit = request.SpeedLimit.Value;

        if (request.DepartmentId.HasValue)
            vehicle.DepartmentId = request.DepartmentId.Value == 0 ? null : request.DepartmentId.Value;

        if (!string.IsNullOrEmpty(request.FuelType))
            vehicle.FuelType = request.FuelType;

        await _context.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}



