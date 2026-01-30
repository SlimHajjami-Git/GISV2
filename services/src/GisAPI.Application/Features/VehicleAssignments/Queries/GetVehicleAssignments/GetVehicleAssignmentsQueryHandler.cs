using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.VehicleAssignments.Commands.AssignVehicle;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.VehicleAssignments.Queries.GetVehicleAssignments;

public class GetVehicleAssignmentsQueryHandler : IRequestHandler<GetVehicleAssignmentsQuery, List<VehicleAssignmentDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehicleAssignmentsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<VehicleAssignmentDto>> Handle(GetVehicleAssignmentsQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId;

        var query = _context.VehicleAssignments
            .Include(a => a.Vehicle)
            .Include(a => a.User)
            .Where(a => a.IsActive && a.Vehicle!.CompanyId == companyId);

        if (request.VehicleId.HasValue)
            query = query.Where(a => a.VehicleId == request.VehicleId);

        if (request.UserId.HasValue)
            query = query.Where(a => a.UserId == request.UserId);

        return await query
            .OrderByDescending(a => a.AssignedAt)
            .Select(a => new VehicleAssignmentDto(
                a.Id,
                a.VehicleId,
                a.Vehicle!.Name,
                a.UserId,
                a.User!.FullName,
                a.AssignedAt,
                a.IsActive
            ))
            .ToListAsync(ct);
    }
}




