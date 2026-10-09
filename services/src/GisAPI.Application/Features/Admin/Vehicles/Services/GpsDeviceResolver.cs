using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Vehicles.Queries.GetAdminVehicles;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Services;

public static class GpsDeviceResolver
{
    /// <param name="ownDeviceId">
    /// Fiche ACTUELLE du véhicule édité : sur elle, la saisie du MAT gagne toujours (c'est la seule
    /// façon de corriger un MAT saisi faux sur un boîtier qui émet — cas HTZ 345, 09/10/2026) ; la
    /// contradiction ne vaut que pour une AUTRE fiche retrouvée par IMEI.
    /// </param>
    public static async Task<(GpsDevice? device, string? error)> ResolveAsync(
        IGisDbContext context, int companyId, int? gpsDeviceId, string? gpsImei, string? gpsMat,
        int? ownDeviceId = null)
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
                // Un MAT saisi qui contredit celui d'une AUTRE fiche qui a déjà communiqué ne
                // s'applique pas en silence : il se signale, avant de toucher à la fiche
                // (recette du 09/10/2026 : IMEI permutés entre HTZ 344 et HTZ 345, la saisie
                // aurait rattaché le mauvais boîtier). La fiche propre du véhicule édité, elle,
                // reste corrigeable : l'écran n'édite le MAT qu'en mode « nouvel appareil », qui
                // passe par ici avec l'IMEI du boîtier actuel.
                if (device.Id != ownDeviceId)
                {
                    var contradiction = GpsDeviceUniquenessGuard.ContradictionAvecLeMatDuBoitier(
                        device.DeviceUid, device.Mat, device.LastCommunication, gpsMat);
                    if (contradiction != null) return (null, contradiction);
                }

                if (device.CompanyId != companyId) device.CompanyId = companyId;
                // Fiche existante retrouvée par son IMEI : le MAT saisi n'a pas été lu sur
                // elle. Si le boîtier a déjà communiqué, l'orthographe stockée (celle que
                // l'ingestion compare à l'identique) est gardée quand la saisie n'en diffère
                // que par la casse ou des espaces.
                if (!string.IsNullOrWhiteSpace(gpsMat))
                    device.Mat = GpsDeviceUniquenessGuard.StoredValueFor(
                        device.Mat, gpsMat, keepDeviceSpelling: device.LastCommunication != null);
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
