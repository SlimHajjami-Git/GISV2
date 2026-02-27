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

        // Fetch latest position per device
        var latestPositions = new Dictionary<int, LatestPositionData>();
        foreach (var deviceId in deviceIds)
        {
            var pos = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => p.DeviceId == deviceId)
                .OrderByDescending(p => p.RecordedAt)
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
                .FirstOrDefaultAsync(ct);
            if (pos != null) latestPositions[deviceId] = pos;
        }

        // Get today's stats per device (last 24 hours)
        var since = DateTime.UtcNow.AddHours(-24);
        var deviceStats = new Dictionary<int, (double MaxSpeed, int MovingCount, int StoppedCount, int TotalCount)>();
        foreach (var deviceId in deviceIds)
        {
            var stats = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => p.DeviceId == deviceId && p.RecordedAt >= since)
                .GroupBy(p => 1)
                .Select(g => new {
                    MaxSpeed = g.Max(p => p.SpeedKph ?? 0),
                    MovingCount = g.Count(p => p.SpeedKph > 5),
                    StoppedCount = g.Count(p => p.SpeedKph <= 5),
                    TotalCount = g.Count()
                })
                .FirstOrDefaultAsync(ct);
            if (stats != null)
                deviceStats[deviceId] = (stats.MaxSpeed, stats.MovingCount, stats.StoppedCount, stats.TotalCount);
        }

        var result = vehicles.Select(v =>
        {
            var deviceId = v.GpsDevice?.Id ?? 0;
            latestPositions.TryGetValue(deviceId, out var position);
            deviceStats.TryGetValue(deviceId, out var stats);
            var lastComm = v.GpsDevice?.LastCommunication;
            var isOnline = lastComm.HasValue && (DateTime.UtcNow - lastComm.Value).TotalMinutes < 30;
            // Calculate battery level from latest position's PowerVoltage (same formula as Rust ingest)
            // Fallback to GpsDevice.BatteryLevel if PowerVoltage is not available
            int? batteryLevel = v.GpsDevice?.BatteryLevel;
            if (batteryLevel == null && position?.PowerVoltage != null && position.PowerVoltage > 0)
            {
                const double voltageFactor = 0.3;
                const double batteryMinV = 11.0;
                const double batteryMaxV = 12.8;
                var voltage = position.PowerVoltage.Value * voltageFactor;
                if (voltage <= batteryMinV) batteryLevel = 0;
                else if (voltage >= batteryMaxV) batteryLevel = 100;
                else batteryLevel = (int)Math.Round((voltage - batteryMinV) / (batteryMaxV - batteryMinV) * 100.0);
            }

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

            // Estimate moving/stopped time based on position counts (approx 1 min per position)
            var movingMinutes = stats.MovingCount;
            var stoppedMinutes = stats.StoppedCount;

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
                    position.OdometerKm
                ) : null,
                new VehicleStatsDto(
                    currentSpeed,
                    maxSpeed,
                    fuelLevel,
                    temperature,
                    batteryLevel,
                    isMoving,
                    !isMoving,
                    TimeSpan.FromMinutes(movingMinutes),
                    TimeSpan.FromMinutes(stoppedMinutes),
                    isMoving ? null : position?.RecordedAt,
                    isMoving ? position?.RecordedAt : null
                ),
                // Firmware "L": use GPS odometer_km directly, otherwise use vehicle mileage
                (v.GpsDevice?.FirmwareVersion != null 
                 && v.GpsDevice.FirmwareVersion.StartsWith("L", StringComparison.OrdinalIgnoreCase)
                 && position?.OdometerKm > 0
                 && position?.OdometerKm != 1048574)
                    ? (int)position!.OdometerKm.Value
                    : v.Mileage
            );
        }).ToList();

        return result;
    }
}



