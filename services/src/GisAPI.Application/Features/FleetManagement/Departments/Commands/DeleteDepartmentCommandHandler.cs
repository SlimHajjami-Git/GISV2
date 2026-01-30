using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.Departments.Commands;

public class DeleteDepartmentCommandHandler : IRequestHandler<DeleteDepartmentCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeleteDepartmentCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(DeleteDepartmentCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == request.Id && d.CompanyId == companyId, cancellationToken)
            ?? throw new InvalidOperationException($"Department with ID {request.Id} not found");

        // Check if department has vehicles assigned
        var hasVehicles = await _context.Vehicles
            .AnyAsync(v => v.DepartmentId == request.Id, cancellationToken);
        
        if (hasVehicles)
            throw new InvalidOperationException("Cannot delete department with assigned vehicles. Please reassign vehicles first.");

        _context.Departments.Remove(department);
        await _context.SaveChangesAsync(cancellationToken);
    }
}



