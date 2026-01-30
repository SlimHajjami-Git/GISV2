using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.Departments.Commands;

public class AssignVehiclesToDepartmentCommandHandler : IRequestHandler<AssignVehiclesToDepartmentCommand, Unit>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public AssignVehiclesToDepartmentCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Unit> Handle(AssignVehiclesToDepartmentCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        // Verify department exists and belongs to company
        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == request.DepartmentId && d.CompanyId == companyId, cancellationToken);

        if (department == null)
            throw new InvalidOperationException("Department not found");

        // Get all vehicles for this company
        var vehicles = await _context.Vehicles
            .Where(v => v.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        // Clear department from vehicles not in the list
        foreach (var vehicle in vehicles.Where(v => v.DepartmentId == request.DepartmentId && !request.VehicleIds.Contains(v.Id)))
        {
            vehicle.DepartmentId = null;
        }

        // Assign vehicles to this department
        foreach (var vehicle in vehicles.Where(v => request.VehicleIds.Contains(v.Id)))
        {
            vehicle.DepartmentId = request.DepartmentId;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}



