using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.Departments.Queries;

public class GetDepartmentByIdQueryHandler : IRequestHandler<GetDepartmentByIdQuery, DepartmentDetailDto?>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetDepartmentByIdQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<DepartmentDetailDto?> Handle(GetDepartmentByIdQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var department = await _context.Departments
            .Where(d => d.Id == request.Id && d.CompanyId == companyId)
            .Select(d => new DepartmentDetailDto(
                d.Id,
                d.Name,
                d.Description,
                d.IsActive,
                _context.Vehicles
                    .Where(v => v.DepartmentId == d.Id)
                    .Select(v => new DepartmentVehicleDto(
                        v.Id,
                        v.Name,
                        v.Plate,
                        v.Type,
                        v.Status
                    ))
                    .ToList(),
                d.CreatedAt,
                d.UpdatedAt
            ))
            .FirstOrDefaultAsync(cancellationToken);

        return department;
    }
}



