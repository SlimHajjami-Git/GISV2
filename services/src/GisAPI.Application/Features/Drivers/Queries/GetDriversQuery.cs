using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Drivers.Queries;

public record GetDriversQuery() : IRequest<List<DriverDto>>;

public class GetDriversQueryHandler : IRequestHandler<GetDriversQuery, List<DriverDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetDriversQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<DriverDto>> Handle(GetDriversQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId;

        // Use IgnoreQueryFilters to prevent the User global query filter from
        // filtering out users via the Include (which would cause null d.User).
        // We apply the company filter manually on the Drivers side.
        return await _context.Drivers
            .IgnoreQueryFilters()
            .Where(d => _tenantService.IsSystemAdmin || companyId == null || d.CompanyId == companyId)
            .Include(d => d.User)
            .Include(d => d.AssignedVehicle)
            .OrderBy(d => d.User != null ? d.User.FirstName : string.Empty)
            .ThenBy(d => d.User != null ? d.User.LastName : string.Empty)
            .Select(d => new DriverDto(
                d.Id,
                d.UserId,
                d.User != null ? d.User.FirstName : string.Empty,
                d.User != null ? d.User.LastName : string.Empty,
                d.User != null ? d.User.Email : string.Empty,
                d.User != null ? d.User.Phone : null,
                d.PermitNumber,
                d.PermitType,
                d.PermitExpiry,
                d.CIN,
                d.DateOfBirth,
                d.AssignedVehicleId,
                d.AssignedVehicle != null ? d.AssignedVehicle.Name : null,
                d.AssignedVehicle != null ? d.AssignedVehicle.Plate : null,
                d.Status,
                d.CreatedAt
            ))
            .ToListAsync(ct);
    }
}
