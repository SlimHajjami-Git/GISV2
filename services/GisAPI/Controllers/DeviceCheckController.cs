using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using System.Text.RegularExpressions;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DeviceCheckController : ControllerBase
{
    private readonly GisDbContext _context;

    public DeviceCheckController(GisDbContext context)
    {
        _context = context;
    }

    private static string MaskImei(string imei) =>
        imei.Length > 4 ? new string('*', imei.Length - 4) + imei[^4..] : imei;

    /// <summary>
    /// Lookup a GPS device by IMEI or vehicle MAT/plate (requires authentication)
    /// Returns last position info: connection status, ignition, coordinates, odometer, fuel, last frame time
    /// </summary>
    [HttpGet("lookup")]
    public async Task<ActionResult> Lookup([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Veuillez fournir un IMEI ou une MAT." });

        var query = q.Trim();

        // Try to find by IMEI (DeviceUid) first, then by MAT (plate)
        var device = await _context.GpsDevices.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeviceUid == query);

        Vehicle? vehicle = null;

        if (device != null)
        {
            vehicle = await _context.Vehicles.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(v => v.GpsDeviceId == device.Id);
        }
        else
        {
            // Try by MAT on device
            device = await _context.GpsDevices.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(d => d.Mat != null && d.Mat.ToLower() == query.ToLower());

            if (device != null)
            {
                vehicle = await _context.Vehicles.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(v => v.GpsDeviceId == device.Id);
            }
            else
            {
                // Try by vehicle plate
                vehicle = await _context.Vehicles.IgnoreQueryFilters().AsNoTracking()
                    .Include(v => v.GpsDevice)
                    .FirstOrDefaultAsync(v => v.Plate != null && v.Plate.ToLower() == query.ToLower());

                if (vehicle?.GpsDevice != null)
                    device = vehicle.GpsDevice;
            }
        }

        if (device == null && vehicle == null)
            return Ok(new { found = false, message = "MAT/IMEI introuvable." });

        if (device == null)
            return Ok(new
            {
                found = true,
                hasGps = false,
                message = "Véhicule trouvé mais aucun boîtier GPS associé.",
                plate = vehicle?.Plate,
                vehicleName = vehicle?.Name
            });

        // Get last GPS position
        var lastPosition = await _context.GpsPositions.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.DeviceId == device.Id)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync();

        if (lastPosition == null)
            return Ok(new
            {
                found = true,
                hasGps = true,
                connected = false,
                message = "Aucune trame trouvée pour ce boîtier.",
                imei = MaskImei(device.DeviceUid),
                mat = device.Mat,
                plate = vehicle?.Plate,
                vehicleName = vehicle?.Name,
                firmwareVersion = device.FirmwareVersion,
                model = device.Model,
                status = device.Status
            });

        // Fuel conversion (same logic as GpsController / monitoring)
        int? fuelPercent = null;
        if (lastPosition.FuelRaw.HasValue && lastPosition.FuelRaw.Value > 0)
        {
            var fuelMode = device.FuelSensorMode ?? "raw_255";
            var tankCapacity = vehicle?.FuelTankCapacity ?? 60;
            var raw = lastPosition.FuelRaw.Value;
            fuelPercent = fuelMode switch
            {
                "percent" => raw,
                "raw_255" => (int)Math.Round(raw / 255.0 * 100.0),
                "liters" => tankCapacity > 0 ? (int)Math.Round(raw * 100.0 / tankCapacity) : raw,
                "half_liter" => tankCapacity > 0 ? (int)Math.Round(raw * 0.5 * 100.0 / tankCapacity) : (int)Math.Round(raw * 0.5),
                _ => raw
            };
            if (fuelPercent > 100) fuelPercent = 100;
            if (fuelPercent < 0) fuelPercent = 0;
        }

        // Connection status: last frame > 40 min = stale (grey)
        var lastFrameTime = lastPosition.RecordedAt;
        var minutesSinceLastFrame = (DateTime.UtcNow - lastFrameTime).TotalMinutes;
        var isStale = minutesSinceLastFrame > 40;

        // Calypso 7 — bug technicien : "Odomètre = 0 km" sur /device-check
        // pour un véhicule fraîchement installé alors que /monitoring affiche
        // bien le km. Cause : la logique était INVERSÉE par rapport à
        // GetVehiclesWithPositionsQueryHandler. Pour le firmware "L" (NEMS L),
        // c'est précisément le boîtier qui rapporte odometer_km via FMS — il
        // FAUT préférer position.OdometerKm. Pour les autres firmwares qui
        // n'ont pas la donnée FMS, on retombe sur vehicle.Mileage.
        //
        // Pour un vieux véhicule, vehicle.Mileage avait fini par être synchronisé
        // à une vraie valeur, donc l'inversion était invisible. Pour un nouveau
        // véhicule (Mileage=0 par défaut), elle affichait 0 km systématiquement.
        //
        // On filtre aussi 1048574 (sentinelle "capteur non initialisé") comme
        // dans le handler /monitoring.
        long? odometerKm = null;
        var fw = device.FirmwareVersion ?? "";
        if (fw.StartsWith("L", StringComparison.OrdinalIgnoreCase)
            && lastPosition.OdometerKm.HasValue
            && lastPosition.OdometerKm.Value > 0
            && lastPosition.OdometerKm.Value != 1048574)
        {
            odometerKm = lastPosition.OdometerKm.Value;
        }
        else if (vehicle != null)
        {
            odometerKm = vehicle.Mileage;
        }

        return Ok(new
        {
            found = true,
            hasGps = true,
            connected = !isStale,
            isStale,
            imei = MaskImei(device.DeviceUid),
            mat = device.Mat,
            plate = vehicle?.Plate,
            vehicleName = vehicle?.Name,
            firmwareVersion = device.FirmwareVersion,
            fuelSensorMode = device.FuelSensorMode,
            model = device.Model,
            deviceStatus = device.Status,
            lastPosition = new
            {
                latitude = lastPosition.Latitude,
                longitude = lastPosition.Longitude,
                speedKph = lastPosition.SpeedKph,
                ignitionOn = lastPosition.IgnitionOn,
                fuelPercent,
                fuelRaw = lastPosition.FuelRaw,
                odometerKm,
                address = lastPosition.Address,
                recordedAt = lastPosition.RecordedAt,
                satellites = lastPosition.Satellites,
                isValid = lastPosition.IsValid
            },
            minutesSinceLastFrame = Math.Round(minutesSinceLastFrame, 1)
        });
    }
}
