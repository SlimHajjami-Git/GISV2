using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles;

internal static class AdminVehicleGpsHelper
{
    public static async Task<(GpsDevice? device, string? error)> ResolveGpsDeviceAsync(
        IGisDbContext context,
        int companyId,
        int? gpsDeviceId,
        string? gpsImei,
        string? gpsMat,
        CancellationToken cancellationToken)
    {
        if (gpsDeviceId.HasValue)
        {
            var device = await context.GpsDevices.FirstOrDefaultAsync(d => d.Id == gpsDeviceId.Value, cancellationToken);
            if (device == null)
                return (null, "Appareil GPS introuvable.");
            if (device.CompanyId != companyId)
                return (null, "Cet appareil GPS appartient à une autre société.");

            return (device, null);
        }

        var normalizedImei = gpsImei?.Trim();
        if (!string.IsNullOrEmpty(normalizedImei))
        {
            var device = await context.GpsDevices.FirstOrDefaultAsync(d => d.DeviceUid == normalizedImei, cancellationToken);
            if (device != null)
            {
                if (device.CompanyId != companyId)
                    return (null, "Un appareil avec cet IMEI appartient déjà à une autre société.");

                if (!string.IsNullOrWhiteSpace(gpsMat) && string.IsNullOrWhiteSpace(device.Mat))
                {
                    device.Mat = gpsMat.Trim();
                }

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

            await context.GpsDevices.AddAsync(newDevice, cancellationToken);
            return (newDevice, null);
        }

        var normalizedMat = gpsMat?.Trim();
        if (!string.IsNullOrEmpty(normalizedMat))
        {
            var device = await context.GpsDevices.FirstOrDefaultAsync(d => d.Mat == normalizedMat, cancellationToken);
            if (device != null)
            {
                if (device.CompanyId != companyId)
                    return (null, "Un appareil avec ce MAT appartient déjà à une autre société.");

                return (device, null);
            }
        }

        return (null, null);
    }

    public static async Task ReleaseGpsDeviceAsync(IGisDbContext context, int? gpsDeviceId, CancellationToken cancellationToken)
    {
        if (!gpsDeviceId.HasValue)
            return;

        var device = await context.GpsDevices.FirstOrDefaultAsync(d => d.Id == gpsDeviceId.Value, cancellationToken);
        if (device == null)
            return;

        device.Status = "unassigned";
        device.UpdatedAt = DateTime.UtcNow;
    }
}



