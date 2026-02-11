using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicles;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Commands.CreateAdminVehicle;

public class CreateAdminVehicleCommandHandler : IRequestHandler<CreateAdminVehicleCommand, CreateAdminVehicleResult>
{
    private readonly IGisDbContext _context;

    public CreateAdminVehicleCommandHandler(IGisDbContext context) => _context = context;

    public async Task<CreateAdminVehicleResult> Handle(CreateAdminVehicleCommand r, CancellationToken ct)
    {
        var company = await _context.Societes.FindAsync(new object[] { r.CompanyId }, ct);
        if (company == null)
            return new CreateAdminVehicleResult(false, "Société non trouvée");

        var vehicle = new Vehicle
        {
            Name = r.Name,
            Type = r.Type ?? "camion",
            Brand = r.Brand,
            Model = r.Model,
            Plate = r.Plate,
            Year = r.Year,
            Color = r.Color,
            Status = r.Status ?? "available",
            HasGps = r.HasGps,
            Mileage = r.Mileage ?? 0,
            FuelType = r.FuelType ?? "diesel",
            FuelTankCapacity = r.FuelTankCapacity,
            CompanyId = r.CompanyId
        };

        if (r.HasGps)
        {
            var (gpsDevice, error) = await GpsDeviceResolver.ResolveAsync(
                _context, r.CompanyId, r.GpsDeviceId, r.GpsImei, r.GpsMat);

            if (error != null) return new CreateAdminVehicleResult(false, error);
            if (gpsDevice == null) return new CreateAdminVehicleResult(false, "Impossible d'associer le GPS sans IMEI ou appareil existant.");

            vehicle.GpsDeviceId = gpsDevice.Id;
            vehicle.HasGps = true;
            gpsDevice.Status = "assigned";
            gpsDevice.Vehicle = vehicle;

            if (!string.IsNullOrWhiteSpace(r.GpsMat)) gpsDevice.Mat = r.GpsMat;
            if (!string.IsNullOrWhiteSpace(r.GpsBrand)) gpsDevice.Brand = r.GpsBrand;
            if (!string.IsNullOrWhiteSpace(r.GpsModel)) gpsDevice.FirmwareVersion = r.GpsModel;
            if (!string.IsNullOrWhiteSpace(r.GpsFuelSensorMode)) gpsDevice.FuelSensorMode = r.GpsFuelSensorMode;
        }

        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync(ct);

        // Reload with navigations
        var created = await _context.Vehicles
            .Include(v => v.Societe)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .FirstAsync(v => v.Id == vehicle.Id, ct);

        return new CreateAdminVehicleResult(true, Vehicle: GpsDeviceResolver.MapToDto(created));
    }
}
