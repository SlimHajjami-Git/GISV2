using GisAPI.Application.Common.Helpers;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Commands.DeleteAdminVehicle;

public class DeleteAdminVehicleCommandHandler : IRequestHandler<DeleteAdminVehicleCommand, bool>
{
    private readonly IGisDbContext _context;

    public DeleteAdminVehicleCommandHandler(IGisDbContext context) => _context = context;

    public async Task<bool> Handle(DeleteAdminVehicleCommand request, CancellationToken ct)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.Id, ct);

        if (vehicle == null) return false;

        // Free the GPS device first (unlink + mark available), then cascade-delete
        // the vehicle and all its history — otherwise RESTRICT foreign keys on the
        // child tables reject the delete (Postgres 23503) and it fails silently.
        if (vehicle.GpsDeviceId.HasValue)
        {
            await GpsDeviceResolver.ReleaseAsync(_context, vehicle.GpsDeviceId);
            await _context.SaveChangesAsync(ct);
        }

        await VehicleDeletionHelper.CascadeDeleteAsync(_context, vehicle.Id, ct);
        return true;
    }
}
