using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Commands.CreateVehicle;

public class CreateVehicleCommandHandler : IRequestHandler<CreateVehicleCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPublisher _publisher;

    public CreateVehicleCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, IPublisher publisher)
    {
        _context = context;
        _tenantService = tenantService;
        _publisher = publisher;
    }

    public async Task<int> Handle(CreateVehicleCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;
        
        var vehicle = new Vehicle
        {
            Name = request.Name,
            Type = request.Type,
            Brand = request.Brand,
            Model = request.Model,
            Plate = request.Plate,
            Year = request.Year,
            Color = request.Color,
            Mileage = request.Mileage,
            FuelType = request.FuelType ?? "diesel",
            FuelTankCapacity = request.FuelTankCapacity,
            CompanyId = companyId,
            Status = "available"
        };

        // Handle GPS device assignment
        if (request.GpsDeviceId.HasValue)
        {
            // Link existing GPS device
            var gpsDevice = await _context.GpsDevices
                .FirstOrDefaultAsync(d => d.Id == request.GpsDeviceId && d.CompanyId == companyId, ct);
            
            if (gpsDevice != null)
            {
                vehicle.GpsDeviceId = gpsDevice.Id;
                gpsDevice.Status = "assigned";
            }
        }
        else if (request.NewGpsDevice != null)
        {
            // Create new GPS device
            var newDevice = new GpsDevice
            {
                DeviceUid = request.NewGpsDevice.DeviceUid,
                SimNumber = request.NewGpsDevice.SimNumber,
                SimOperator = request.NewGpsDevice.SimOperator,
                Brand = request.NewGpsDevice.Brand,
                Model = request.NewGpsDevice.Model,
                InstallationDate = request.NewGpsDevice.InstallationDate,
                CompanyId = companyId,
                Status = "assigned"
            };
            
            _context.GpsDevices.Add(newDevice);
            await _context.SaveChangesAsync(ct);
            
            vehicle.GpsDeviceId = newDevice.Id;
        }

        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync(ct);

        // Notify company admins about the new vehicle
        var actorId = _tenantService.UserId ?? 0;
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, ct);
        if (actor != null && companyId > 0)
        {
            _ = _publisher.Publish(new AdminActionNotificationEvent(
                companyId, actorId, actor.FullName,
                "vehicle_created", vehicle.Name ?? vehicle.Plate, vehicle.Id, "vehicle"
            ), ct);
        }

        return vehicle.Id;
    }
}



