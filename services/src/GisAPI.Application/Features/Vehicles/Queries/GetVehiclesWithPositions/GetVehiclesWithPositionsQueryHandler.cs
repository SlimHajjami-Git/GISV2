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

        // Get vehicles with GPS devices
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Include(v => v.GpsDevice)
            .ToListAsync(ct);

        var deviceIds = vehicles
            .Where(v => v.GpsDevice != null)
            .Select(v => v.GpsDevice!.Id)
            .ToList();

        // Get latest positions using projection to avoid JSONB issues
        var latestPositions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId))
            .GroupBy(p => p.DeviceId)
            .Select(g => new {
                DeviceId = g.Key,
                Position = g.OrderByDescending(p => p.RecordedAt).Select(p => new {
                    p.Id,
                    p.Latitude,
                    p.Longitude,
                    p.SpeedKph,
                    p.CourseDeg,
                    p.IgnitionOn,
                    p.RecordedAt,
                    p.FuelRaw,
                    p.TemperatureC
                }).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.DeviceId, x => x.Position, ct);

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
                    batteryLevel
                ) : null,
                new VehicleStatsDto(
                    currentSpeed,
                    maxSpeed,
                    position?.FuelRaw,
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
