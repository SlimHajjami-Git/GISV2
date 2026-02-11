using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicles;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicleById;

public record GetAdminVehicleByIdQuery(int Id) : IRequest<AdminVehicleDto?>;

public class GetAdminVehicleByIdQueryHandler : IRequestHandler<GetAdminVehicleByIdQuery, AdminVehicleDto?>
{
    private readonly IGisDbContext _context;

    public GetAdminVehicleByIdQueryHandler(IGisDbContext context) => _context = context;

    public async Task<AdminVehicleDto?> Handle(GetAdminVehicleByIdQuery request, CancellationToken ct)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.Societe)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .FirstOrDefaultAsync(v => v.Id == request.Id, ct);

        return vehicle != null ? GpsDeviceResolver.MapToDto(vehicle) : null;
    }
}
