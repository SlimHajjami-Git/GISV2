using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.Departments.Commands;

public class UpdateDepartmentCommandHandler : IRequestHandler<UpdateDepartmentCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpdateDepartmentCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(UpdateDepartmentCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var department = await _context.Departments
            .FirstOrDefaultAsync(d => d.Id == request.Id && d.CompanyId == companyId, cancellationToken)
            ?? throw new InvalidOperationException($"Department with ID {request.Id} not found");

        // Check for duplicate name (excluding current department)
        var exists = await _context.Departments
            .AnyAsync(d => d.CompanyId == companyId && d.Id != request.Id && d.Name.ToLower() == request.Name.ToLower(), cancellationToken);
        
        if (exists)
            throw new InvalidOperationException($"A department with the name '{request.Name}' already exists");

        department.Name = request.Name;
        department.Description = request.Description;
        department.IsActive = request.IsActive;
        department.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
    }
}



