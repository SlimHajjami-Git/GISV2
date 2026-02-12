using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Drivers.Queries;

public record GetDriversQuery() : IRequest<List<DriverDto>>;

public class GetDriversQueryHandler : IRequestHandler<GetDriversQuery, List<DriverDto>>
{
    private readonly IGisDbContext _context;

    public GetDriversQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<DriverDto>> Handle(GetDriversQuery request, CancellationToken ct)
    {
        return await _context.Drivers
            .Include(d => d.User)
            .Include(d => d.AssignedVehicle)
            .OrderBy(d => d.User.FirstName)
            .ThenBy(d => d.User.LastName)
            .Select(d => new DriverDto(
                d.Id,
                d.UserId,
                d.User.FirstName,
                d.User.LastName,
                d.User.Email,
                d.User.Phone,
                d.PermitNumber,
                d.PermitType,
                d.PermitExpiry,
                d.CIN,
                d.DateOfBirth,
                d.HireDate,
                d.AssignedVehicleId,
                d.AssignedVehicle != null ? d.AssignedVehicle.Name : null,
                d.AssignedVehicle != null ? d.AssignedVehicle.Plate : null,
                d.Status,
                d.CreatedAt
            ))
            .ToListAsync(ct);
    }
}
