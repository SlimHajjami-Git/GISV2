using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Commands.UpdateAdminVehicle;

public class UpdateAdminVehicleCommandHandler : IRequestHandler<UpdateAdminVehicleCommand, UpdateAdminVehicleResult>
{
    private readonly IGisDbContext _context;

    public UpdateAdminVehicleCommandHandler(IGisDbContext context) => _context = context;

    public async Task<UpdateAdminVehicleResult> Handle(UpdateAdminVehicleCommand r, CancellationToken ct)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.Societe)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .FirstOrDefaultAsync(v => v.Id == r.Id, ct);

        if (vehicle == null)
            return new UpdateAdminVehicleResult(false, "not_found");

        if (!string.IsNullOrEmpty(r.Name)) vehicle.Name = r.Name;
        if (r.Type != null) vehicle.Type = r.Type;
        if (r.Brand != null) vehicle.Brand = r.Brand;
        if (r.Model != null) vehicle.Model = r.Model;
        if (r.Plate != null) vehicle.Plate = r.Plate;
        if (r.Year.HasValue) vehicle.Year = r.Year.Value;
        if (r.Color != null) vehicle.Color = r.Color;
        if (r.Status != null) vehicle.Status = r.Status;
        if (r.HasGps.HasValue) vehicle.HasGps = r.HasGps.Value;
        if (r.Mileage.HasValue) vehicle.Mileage = r.Mileage.Value;
        if (r.FuelType != null) vehicle.FuelType = r.FuelType;
        if (r.FuelTankCapacity.HasValue) vehicle.FuelTankCapacity = r.FuelTankCapacity.Value;
        if (r.CompanyId.HasValue) vehicle.CompanyId = r.CompanyId.Value;

        var targetCompanyId = r.CompanyId ?? vehicle.CompanyId;

        // When changing company, move the existing GPS device to the new company too
        if (r.CompanyId.HasValue && vehicle.GpsDevice != null)
            vehicle.GpsDevice.CompanyId = r.CompanyId.Value;

        if (r.HasGps == false)
        {
            await GpsDeviceResolver.ReleaseAsync(_context, vehicle.GpsDeviceId);
            vehicle.GpsDeviceId = null;
            vehicle.HasGps = false;
        }
        else if (r.HasGps == true ||
                 r.GpsDeviceId.HasValue ||
                 !string.IsNullOrWhiteSpace(r.GpsImei) ||
                 !string.IsNullOrWhiteSpace(r.GpsMat) ||
                 !string.IsNullOrWhiteSpace(r.GpsFuelSensorMode))
        {
            GpsDevice? gpsDevice = null;

            // Use existing device if the sent GPS identifiers match the current device
            var existingDevice = vehicle.GpsDevice;
            var isSameDevice = vehicle.GpsDeviceId.HasValue && existingDevice != null && (
                (r.GpsDeviceId.HasValue && r.GpsDeviceId.Value == existingDevice.Id) ||
                (!r.GpsDeviceId.HasValue &&
                    (string.IsNullOrWhiteSpace(r.GpsImei) || r.GpsImei.Trim() == existingDevice.DeviceUid) &&
                    (string.IsNullOrWhiteSpace(r.GpsMat) || r.GpsMat.Trim() == existingDevice.Mat)));

            if (isSameDevice)
            {
                gpsDevice = existingDevice;
            }
            else if (!r.GpsDeviceId.HasValue &&
                string.IsNullOrWhiteSpace(r.GpsImei) &&
                string.IsNullOrWhiteSpace(r.GpsMat) &&
                vehicle.GpsDeviceId.HasValue)
            {
                gpsDevice = existingDevice;
            }
            else
            {
                var (resolvedDevice, error) = await GpsDeviceResolver.ResolveAsync(
                    _context, targetCompanyId, r.GpsDeviceId, r.GpsImei, r.GpsMat);
                if (error != null) return new UpdateAdminVehicleResult(false, error);
                gpsDevice = resolvedDevice;
            }

            if (gpsDevice != null)
            {
                if (vehicle.GpsDeviceId.HasValue && vehicle.GpsDeviceId != gpsDevice.Id)
                    await GpsDeviceResolver.ReleaseAsync(_context, vehicle.GpsDeviceId);

                vehicle.GpsDeviceId = gpsDevice.Id;
                vehicle.HasGps = true;
                gpsDevice.Status = "assigned";
                gpsDevice.Vehicle = vehicle;

                if (!string.IsNullOrWhiteSpace(r.GpsImei)) gpsDevice.DeviceUid = r.GpsImei!;
                if (!string.IsNullOrWhiteSpace(r.GpsMat)) gpsDevice.Mat = r.GpsMat;
                if (!string.IsNullOrWhiteSpace(r.GpsBrand)) gpsDevice.Brand = r.GpsBrand;
                if (!string.IsNullOrWhiteSpace(r.GpsModel)) gpsDevice.Model = r.GpsModel;
                if (!string.IsNullOrWhiteSpace(r.GpsFirmwareVersion)) gpsDevice.FirmwareVersion = r.GpsFirmwareVersion;
                if (!string.IsNullOrWhiteSpace(r.GpsFuelSensorMode)) gpsDevice.FuelSensorMode = r.GpsFuelSensorMode;
                if (!string.IsNullOrWhiteSpace(r.GpsSimNumber)) gpsDevice.SimNumber = r.GpsSimNumber;
                if (!string.IsNullOrWhiteSpace(r.GpsSimOperator)) gpsDevice.SimOperator = r.GpsSimOperator;
                if (r.GpsInstallationDate.HasValue) gpsDevice.InstallationDate = r.GpsInstallationDate;
            }
        }

        vehicle.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return new UpdateAdminVehicleResult(true, Vehicle: GpsDeviceResolver.MapToDto(vehicle));
    }
}
