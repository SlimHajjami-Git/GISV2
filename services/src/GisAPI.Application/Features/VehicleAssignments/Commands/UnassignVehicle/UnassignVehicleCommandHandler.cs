using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.VehicleAssignments.Commands.UnassignVehicle;

public class UnassignVehicleCommandHandler : IRequestHandler<UnassignVehicleCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UnassignVehicleCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(UnassignVehicleCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId 
            ?? throw new DomainException("Société non identifiée");

        var assignment = await _context.VehicleAssignments
            .Include(a => a.Vehicle)
            .FirstOrDefaultAsync(a => 
                a.VehicleId == request.VehicleId && 
                a.UserId == request.UserId && 
                a.IsActive &&
                a.Vehicle!.CompanyId == companyId, ct)
            ?? throw new NotFoundException("VehicleAssignment", $"{request.VehicleId}-{request.UserId}");

        assignment.IsActive = false;
        assignment.UnassignedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }
}
