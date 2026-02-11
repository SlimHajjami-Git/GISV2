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

        if (vehicle.GpsDeviceId.HasValue)
            await GpsDeviceResolver.ReleaseAsync(_context, vehicle.GpsDeviceId);

        _context.Vehicles.Remove(vehicle);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
