using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicles;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Services;

public static class GpsDeviceResolver
{
    public static async Task<(GpsDevice? device, string? error)> ResolveAsync(
        IGisDbContext context, int companyId, int? gpsDeviceId, string? gpsImei, string? gpsMat)
    {
        if (gpsDeviceId.HasValue)
        {
            var device = await context.GpsDevices.FirstOrDefaultAsync(d => d.Id == gpsDeviceId.Value);
            if (device == null) return (null, "Appareil GPS introuvable.");
            if (device.CompanyId != companyId) return (null, "Cet appareil GPS appartient à une autre société.");
            return (device, null);
        }

        var normalizedImei = gpsImei?.Trim();
        if (!string.IsNullOrEmpty(normalizedImei))
        {
            var device = await context.GpsDevices
                .Include(d => d.Vehicle)
                .FirstOrDefaultAsync(d => d.DeviceUid == normalizedImei);

            if (device != null)
            {
                if (device.CompanyId != companyId) device.CompanyId = companyId;
                if (!string.IsNullOrWhiteSpace(gpsMat)) device.Mat = gpsMat.Trim();
                if (device.Vehicle != null)
                {
                    device.Vehicle.GpsDeviceId = null;
                    device.Vehicle.HasGps = false;
                }
                device.UpdatedAt = DateTime.UtcNow;
                return (device, null);
            }

            var newDevice = new GpsDevice
            {
                DeviceUid = normalizedImei,
                Mat = gpsMat?.Trim(),
                CompanyId = companyId,
                Status = "unassigned",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.GpsDevices.Add(newDevice);
            return (newDevice, null);
        }

        var normalizedMat = gpsMat?.Trim();
        if (!string.IsNullOrEmpty(normalizedMat))
        {
            var device = await context.GpsDevices
                .Include(d => d.Vehicle)
                .FirstOrDefaultAsync(d => d.Mat == normalizedMat);
            if (device != null)
            {
                if (device.CompanyId != companyId) device.CompanyId = companyId;
                if (device.Vehicle != null)
                {
                    device.Vehicle.GpsDeviceId = null;
                    device.Vehicle.HasGps = false;
                }
                device.UpdatedAt = DateTime.UtcNow;
                return (device, null);
            }
        }

        return (null, null);
    }

    public static async Task ReleaseAsync(IGisDbContext context, int? gpsDeviceId)
    {
        if (!gpsDeviceId.HasValue) return;
        var device = await context.GpsDevices.FirstOrDefaultAsync(d => d.Id == gpsDeviceId.Value);
        if (device == null) return;
        device.Status = "unassigned";
        device.UpdatedAt = DateTime.UtcNow;
    }

    public static AdminVehicleDto MapToDto(Vehicle v)
    {
        return new AdminVehicleDto
        {
            Id = v.Id, Name = v.Name, Type = v.Type, Brand = v.Brand, Model = v.Model,
            Plate = v.Plate, Year = v.Year, Color = v.Color, Status = v.Status,
            HasGps = v.HasGps, Mileage = v.Mileage, FuelType = v.FuelType,
            FuelTankCapacity = v.FuelTankCapacity, CompanyId = v.CompanyId,
            CompanyName = v.Societe?.Name,
            GpsDeviceId = v.GpsDeviceId,
            GpsImei = v.GpsDevice?.DeviceUid, GpsMat = v.GpsDevice?.Mat,
            GpsBrand = v.GpsDevice?.Brand, GpsModel = v.GpsDevice?.Model,
            GpsFirmwareVersion = v.GpsDevice?.FirmwareVersion,
            GpsFuelSensorMode = v.GpsDevice?.FuelSensorMode,
            GpsSimNumber = v.GpsDevice?.SimNumber,
            GpsSimOperator = v.GpsDevice?.SimOperator,
            GpsInstallationDate = v.GpsDevice?.InstallationDate,
            AssignedDriverId = v.AssignedDriverId,
            AssignedDriverName = v.AssignedDriver?.FullName,
            CreatedAt = v.CreatedAt, UpdatedAt = v.UpdatedAt
        };
    }
}
