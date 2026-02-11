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

        // OPTIMIZED: Single query to get latest position per device (eliminates N+1 problem)
        // Uses a subquery to find the max RecordedAt per device, then joins back
        var latestPositions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId))
            .GroupBy(p => p.DeviceId)
            .Select(g => new LatestPositionData
            {
                DeviceId = g.Key,
                Id = g.OrderByDescending(p => p.RecordedAt).First().Id,
                Latitude = g.OrderByDescending(p => p.RecordedAt).First().Latitude,
                Longitude = g.OrderByDescending(p => p.RecordedAt).First().Longitude,
                SpeedKph = g.OrderByDescending(p => p.RecordedAt).First().SpeedKph,
                CourseDeg = g.OrderByDescending(p => p.RecordedAt).First().CourseDeg,
                IgnitionOn = g.OrderByDescending(p => p.RecordedAt).First().IgnitionOn,
                RecordedAt = g.OrderByDescending(p => p.RecordedAt).First().RecordedAt,
                FuelRaw = g.OrderByDescending(p => p.RecordedAt).First().FuelRaw,
                TemperatureC = g.OrderByDescending(p => p.RecordedAt).First().TemperatureC,
                Address = g.OrderByDescending(p => p.RecordedAt).First().Address,
                OdometerKm = g.OrderByDescending(p => p.RecordedAt).First().OdometerKm
            })
            .ToDictionaryAsync(p => p.DeviceId, ct);

        // Get today's stats for each device (last 24 hours)
        var since = DateTime.UtcNow.AddHours(-24);
        var deviceStats = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId) && p.RecordedAt >= since)
            .GroupBy(p => p.DeviceId)
            .Select(g => new {
                DeviceId = g.Key,
                MaxSpeed = g.Max(p => p.SpeedKph ?? 0),
                MovingCount = g.Count(p => p.SpeedKph > 5),
                StoppedCount = g.Count(p => p.SpeedKph <= 5),
                TotalCount = g.Count()
            })
            .ToDictionaryAsync(x => x.DeviceId, ct);

        var result = vehicles.Select(v =>
        {
            var deviceId = v.GpsDevice?.Id ?? 0;
            latestPositions.TryGetValue(deviceId, out var position);
            deviceStats.TryGetValue(deviceId, out var stats);
            var lastComm = v.GpsDevice?.LastCommunication;
            var isOnline = lastComm.HasValue && (DateTime.UtcNow - lastComm.Value).TotalMinutes < 30;
            var batteryLevel = v.GpsDevice?.BatteryLevel;

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
            var maxSpeed = Math.Round(stats?.MaxSpeed ?? 0.0);

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
            var movingMinutes = stats?.MovingCount ?? 0;
            var stoppedMinutes = stats?.StoppedCount ?? 0;

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
                )
            );
        }).ToList();

        return result;
    }
}



