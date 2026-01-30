using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.Departments.Queries;

public class GetDepartmentsQueryHandler : IRequestHandler<GetDepartmentsQuery, List<DepartmentDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetDepartmentsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<DepartmentDto>> Handle(GetDepartmentsQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var query = _context.Departments
            .Where(d => d.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var searchTerm = request.SearchTerm.ToLower();
            query = query.Where(d => d.Name.ToLower().Contains(searchTerm) || 
                                     (d.Description != null && d.Description.ToLower().Contains(searchTerm)));
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(d => d.IsActive == request.IsActive.Value);
        }

        // Get vehicle counts per department
        var vehicleCounts = await _context.Vehicles
            .Where(v => v.CompanyId == companyId && v.DepartmentId != null)
            .GroupBy(v => v.DepartmentId)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId!.Value, x => x.Count, cancellationToken);

        var departments = await query
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        return departments.Select(d => new DepartmentDto(
            d.Id,
            d.Name,
            d.Description,
            d.IsActive,
            vehicleCounts.GetValueOrDefault(d.Id, 0),
            d.CreatedAt,
            d.UpdatedAt
        )).ToList();
    }
}



