using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions;

// Internal class for query projection
internal class LatestPositionData
{
    public int DeviceId { get; set; }
    public long Id { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? SpeedKph { get; set; }
    public double? CourseDeg { get; set; }
    public bool? IgnitionOn { get; set; }
    public DateTime RecordedAt { get; set; }
    public int? FuelRaw { get; set; }
    public short? TemperatureC { get; set; }
    public int? PowerVoltage { get; set; }
    public string? Address { get; set; }
    public long? OdometerKm { get; set; }
}

public class GetVehiclesWithPositionsQueryHandler : IRequestHandler<GetVehiclesWithPositionsQuery, List<VehicleWithPositionDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehiclesWithPositionsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<VehicleWithPositionDto>> Handle(GetVehiclesWithPositionsQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;
        var userId = _tenantService.UserId ?? 0;
        var isAdmin = _tenantService.UserRoles.Any(r => r == "company_admin" || r == "admin" || r == "super_admin" || r == "system_admin");

        // Get vehicles with GPS devices
        var vehicleQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Include(v => v.GpsDevice)
            .AsQueryable();

        // Non-admin users only see their assigned vehicles
        if (!isAdmin && userId > 0)
        {
            var assignedVehicleIds = await _context.UserVehicles
                .Where(uv => uv.UserId == userId)
                .Select(uv => uv.VehicleId)
                .ToListAsync(ct);

            if (assignedVehicleIds.Any())
                vehicleQuery = vehicleQuery.Where(v => assignedVehicleIds.Contains(v.Id));
            else
                vehicleQuery = vehicleQuery.Where(v => false);
        }

        var vehicles = await vehicleQuery.ToListAsync(ct);

        var deviceIds = vehicles
            .Where(v => v.GpsDevice != null)
            .Select(v => v.GpsDevice!.Id)
            .ToList();

        // Fetch latest position per device — ONE query instead of N
        // Uses GroupBy to get the max position ID per device, then fetches those positions
        var latestPosIds = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId))
            .GroupBy(p => p.DeviceId)
            .Select(g => g.OrderByDescending(p => p.RecordedAt).Select(p => p.Id).First())
            .ToListAsync(ct);

        var latestPositions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => latestPosIds.Contains(p.Id))
            .Select(p => new LatestPositionData
            {
                DeviceId = p.DeviceId,
                Id = p.Id,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                SpeedKph = p.SpeedKph,
                CourseDeg = p.CourseDeg,
                IgnitionOn = p.IgnitionOn,
                RecordedAt = p.RecordedAt,
                FuelRaw = p.FuelRaw,
                TemperatureC = p.TemperatureC,
                PowerVoltage = p.PowerVoltage,
                Address = p.Address,
                OdometerKm = p.OdometerKm
            })
            .ToDictionaryAsync(p => p.DeviceId, ct);

        // Get today's stats per device (last 24 hours) — ONE query for all devices
        // Fetch all positions for all devices at once, then compute stats in-memory
        var since = DateTime.UtcNow.AddHours(-24);
        var allRecentPositions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId) && p.RecordedAt >= since)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new { p.DeviceId, p.RecordedAt, p.SpeedKph })
            .ToListAsync(ct);

        // "Engine off since" — for each device, the most recent frame
        // where the engine was on. After that timestamp the engine has
        // been off. We rely on Postgres's DISTINCT ON via a GroupBy + Max
        // (which Npgsql translates well), with a hard cap at 30 days to
        // keep the scan bounded — anything older than that and the
        // operator can read the value as "very long time ago" anyway.
        var engineOffLookback = DateTime.UtcNow.AddDays(-30);
        var lastIgnitionOn = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId)
                     && p.IgnitionOn == true
                     && p.RecordedAt >= engineOffLookback)
            .GroupBy(p => p.DeviceId)
            .Select(g => new { DeviceId = g.Key, LastOn = g.Max(p => p.RecordedAt) })
            .ToDictionaryAsync(x => x.DeviceId, x => x.LastOn, ct);

        var deviceStats = new Dictionary<int, (double MaxSpeed, double MovingMinutes, double StoppedMinutes, int TotalCount)>();
        foreach (var group in allRecentPositions.GroupBy(p => p.DeviceId))
        {
            var positions = group.ToList();
            var maxSpeed = positions.Max(p => p.SpeedKph ?? 0);
            double movingSeconds = 0;
            double stoppedSeconds = 0;

            for (int i = 1; i < positions.Count; i++)
            {
                var gap = (positions[i].RecordedAt - positions[i - 1].RecordedAt).TotalSeconds;
                // Cap gap at 10 minutes to avoid counting long offline periods
                if (gap > 600) gap = 600;

                // Attribute the gap to moving or stopped based on the previous position's speed
                if ((positions[i - 1].SpeedKph ?? 0) > 5)
                    movingSeconds += gap;
                else
                    stoppedSeconds += gap;
            }

            deviceStats[group.Key] = (maxSpeed, movingSeconds / 60.0, stoppedSeconds / 60.0, positions.Count);
        }

        var result = vehicles.Select(v =>
        {
            var deviceId = v.GpsDevice?.Id ?? 0;
            latestPositions.TryGetValue(deviceId, out var position);
            deviceStats.TryGetValue(deviceId, out var stats);
            var lastComm = v.GpsDevice?.LastCommunication;
            var isOnline = lastComm.HasValue && (DateTime.UtcNow - lastComm.Value).TotalMinutes < 41;
            // Calculate battery level from latest position's PowerVoltage (same formula as Rust ingest)
            // Fallback to GpsDevice.BatteryLevel if PowerVoltage is not available
            // ALSO compute the raw voltage in volts so the UI can show "12.6 V"
            // alongside (or instead of) the percentage — operator preference.
            int? batteryLevel = v.GpsDevice?.BatteryLevel;
            double? batteryVoltageV = null;
            if (position?.PowerVoltage != null && position.PowerVoltage > 0)
            {
                const double voltageFactor = 0.3;
                const double batteryMinV = 11.0;
                const double batteryMaxV = 12.8;
                var voltage = position.PowerVoltage.Value * voltageFactor;
                batteryVoltageV = Math.Round(voltage, 1);
                if (batteryLevel == null)
                {
                    if (voltage <= batteryMinV) batteryLevel = 0;
                    else if (voltage >= batteryMaxV) batteryLevel = 100;
                    else batteryLevel = (int)Math.Round((voltage - batteryMinV) / (batteryMaxV - batteryMinV) * 100.0);
                }
            }

            // Sticky battery-health flag: any voltage-health alert raised in
            // the last 7 days surfaces as a warning indicator on the
            // monitoring page. We deliberately go beyond the 48h cooldown of
            // the detector so an admin who didn't see the SignalR push still
            // notices the warning when they open the page.
            var batteryAlertCutoff = DateTime.UtcNow.AddDays(-7);
            var hasBatteryHealthAlert =
                v.GpsDevice?.LastVoltageHealthAlertAt.HasValue == true
                && v.GpsDevice.LastVoltageHealthAlertAt.Value >= batteryAlertCutoff;

            // If ignition is off, speed is 0
            var ignitionOn = position?.IgnitionOn ?? false;
            var rawSpeed = position?.SpeedKph ?? 0.0;

            // Stale ignition detection: if last position says ignition ON but speed is 0
            // and the position is older than 10 minutes, it's likely stale data
            // (the ignition-OFF frame was missed due to GPS ingest throttling)
            if (ignitionOn && rawSpeed <= 1 && position != null)
            {
                var positionAge = (DateTime.UtcNow - position.RecordedAt).TotalMinutes;
                if (positionAge > 10)
                {
                    ignitionOn = false;
                }
            }

            var currentSpeed = ignitionOn ? Math.Round(rawSpeed) : 0.0;
            var isMoving = ignitionOn && rawSpeed > 5;

            // Round max speed to whole number
            var maxSpeed = Math.Round(stats.MaxSpeed);

            // Filter invalid temperature values (-32768 is uninitialized/error value)
            var temperature = position?.TemperatureC;
            if (temperature.HasValue && (temperature.Value < -100 || temperature.Value > 200))
            {
                temperature = null;
            }

            // Convert fuel raw value based on fuel_sensor_mode
            var fuelRaw = position?.FuelRaw;
            int? fuelLevel = null;
            if (fuelRaw.HasValue)
            {
                var fuelMode = v.GpsDevice?.FuelSensorMode ?? "raw_255";
                var tankCapacity = v.FuelTankCapacity ?? 60; // Default 60L if not set
                fuelLevel = fuelMode switch
                {
                    "percent" => fuelRaw.Value, // Already 0-100%
                    "raw_255" => (int)Math.Round(fuelRaw.Value / 255.0 * 100.0), // 0-255 -> 0-100%
                    "liters" => tankCapacity > 0 ? (int)Math.Round(fuelRaw.Value * 100.0 / tankCapacity) : fuelRaw.Value, // Liters -> %
                    "half_liter" => tankCapacity > 0 ? (int)Math.Round(fuelRaw.Value * 0.5 * 100.0 / tankCapacity) : (int)Math.Round(fuelRaw.Value * 0.5), // Half-liters -> %
                    _ => fuelRaw.Value // Default: keep as-is
                };
                // Clamp to 0-100
                if (fuelLevel > 100) fuelLevel = 100;
                if (fuelLevel < 0) fuelLevel = 0;
            }

            // Accurate moving/stopped time from time-gap calculation
            var movingMinutes = stats.MovingMinutes;
            var stoppedMinutes = stats.StoppedMinutes;

            return new VehicleWithPositionDto(
                v.Id,
                v.Name,
                v.Type,
                v.Brand,
                v.Model,
                v.Plate,
                v.Status,
                v.HasGps,
                v.GpsDevice?.DeviceUid,
                v.GpsDevice?.Id,
                lastComm,
                isOnline,
                position != null ? new PositionDto(
                    (int)position.Id,
                    position.Latitude,
                    position.Longitude,
                    ignitionOn ? Math.Round(position.SpeedKph ?? 0.0) : 0.0,
                    position.CourseDeg ?? 0.0,
                    ignitionOn,
                    position.RecordedAt,
                    position.FuelRaw,
                    temperature,
                    batteryLevel,
                    position.Address,
                    position.OdometerKm,
                    batteryVoltageV
                ) : null,
                new VehicleStatsDto(
                    currentSpeed,
                    maxSpeed,
                    fuelLevel,
                    temperature,
                    batteryLevel,
                    batteryVoltageV,
                    isMoving,
                    !isMoving,
                    TimeSpan.FromMinutes(movingMinutes),
                    TimeSpan.FromMinutes(stoppedMinutes),
                    isMoving ? null : position?.RecordedAt,
                    isMoving ? position?.RecordedAt : null,
                    // EngineOffSince: the timestamp of the most recent
                    // ignition-on frame. Null while the engine is
                    // currently on, or if there's no ignition-on frame
                    // in the lookback window.
                    isMoving
                        ? null
                        : (lastIgnitionOn.TryGetValue(deviceId, out var lastOn) ? lastOn : (DateTime?)null)
                ),
                // Firmware "L": use GPS odometer_km directly, otherwise use vehicle mileage
                (v.GpsDevice?.FirmwareVersion != null
                 && v.GpsDevice.FirmwareVersion.StartsWith("L", StringComparison.OrdinalIgnoreCase)
                 && position?.OdometerKm > 0
                 && position?.OdometerKm != 1048574)
                    ? (int)position!.OdometerKm.Value
                    : v.Mileage,
                hasBatteryHealthAlert,
                v.IsImmobilized,
                v.ImmobilizationReason,
                v.ImmobilizationStartedAt
            );
        }).ToList();

        return result;
    }
}



