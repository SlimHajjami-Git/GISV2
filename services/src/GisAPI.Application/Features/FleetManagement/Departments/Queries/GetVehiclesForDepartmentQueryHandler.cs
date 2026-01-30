using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FleetManagement.Departments.Queries;

public class GetVehiclesForDepartmentQueryHandler : IRequestHandler<GetVehiclesForDepartmentQuery, List<VehicleForAssignmentDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehiclesForDepartmentQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<VehicleForAssignmentDto>> Handle(GetVehiclesForDepartmentQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var vehicles = await _context.Vehicles
            .Where(v => v.CompanyId == companyId)
            .OrderBy(v => v.Name)
            .Select(v => new VehicleForAssignmentDto(
                v.Id,
                v.Name,
                v.Plate,
                v.Type,
                v.Brand,
                v.Model,
                v.DepartmentId == request.DepartmentId,
                v.DepartmentId,
                v.DepartmentId != null ? _context.Departments.Where(d => d.Id == v.DepartmentId).Select(d => d.Name).FirstOrDefault() : null
            ))
            .ToListAsync(cancellationToken);

        return vehicles;
    }
}



