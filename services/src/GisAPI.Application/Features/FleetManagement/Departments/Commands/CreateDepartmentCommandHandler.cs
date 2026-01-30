using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.Departments.Commands;

public class CreateDepartmentCommandHandler : IRequestHandler<CreateDepartmentCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreateDepartmentCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<int> Handle(CreateDepartmentCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        // Check for duplicate name
        var exists = await _context.Departments
            .AnyAsync(d => d.CompanyId == companyId && d.Name.ToLower() == request.Name.ToLower(), cancellationToken);
        
        if (exists)
            throw new InvalidOperationException($"A department with the name '{request.Name}' already exists");

        var department = new Department
        {
            Name = request.Name,
            Description = request.Description,
            IsActive = request.IsActive,
            CompanyId = companyId
        };

        _context.Departments.Add(department);
        await _context.SaveChangesAsync(cancellationToken);

        return department.Id;
    }
}



