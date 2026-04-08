using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common;
using GisAPI.Services;
using GisAPI.Application.Features.Gps.Commands.BroadcastPosition;
using MediatR;

namespace GisAPI.Controllers;

/// <summary>
/// GPS API Controller - CQRS Query Side
/// Handles all GPS data reads (positions, tracking, history)
/// Write operations are handled by the Rust GPS Ingest service
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GpsController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly IGeocodingService _geocodingService;
    private readonly IGpsHubService _gpsHubService;
    private readonly IRedisCacheService _redisCache;
    private readonly ISender _mediator;

    public GpsController(GisDbContext context, IGeocodingService geocodingService, IGpsHubService gpsHubService, IRedisCacheService redisCache, ISender mediator)
    {
        _context = context;
        _geocodingService = geocodingService;
        _gpsHubService = gpsHubService;
        _redisCache = redisCache;
        _mediator = mediator;
    }

    // Npgsql 6+ requires DateTimeKind.Utc for timestamptz columns.
    // GPS ingest stores timestamps in real UTC.
    private static DateTime EnsureUtc(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Utc) return dt;
        if (dt.Kind == DateTimeKind.Local) return dt.ToUniversalTime();
        // Unspecified — assume UTC (GPS data is stored in UTC)
        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    // ==================== REAL-TIME POSITIONS (REDIS) ====================

    /// <summary>
    /// Get real-time positions from Redis cache (ultra-fast, ~10ms latency)
    /// Falls back to database if Redis is unavailable
    /// </summary>
    [HttpGet("positions/realtime")]
    public async Task<ActionResult<List<RealtimePositionDto>>> GetRealtimePositions()
    {
        var companyId = GetCompanyId();

        // Try Redis first for real-time data
        var redisPositions = await _redisCache.GetAllPositionsForCompanyAsync(companyId);
        
        if (redisPositions.Any())
        {
            // Get vehicle info from DB to enrich Redis data
            var vehicleMap = await _context.Vehicles
                .AsNoTracking()
                .Where(v => v.CompanyId == companyId && v.GpsDeviceId.HasValue)
                .Include(v => v.GpsDevice)
                .ToDictionaryAsync(v => v.GpsDevice!.DeviceUid, v => v);

            var result = redisPositions
                .Where(p => vehicleMap.ContainsKey(p.DeviceUid))
                .Select(p => {
                    var vehicle = vehicleMap[p.DeviceUid];
                    return new RealtimePositionDto
                    {
                        VehicleId = vehicle.Id,
                        VehicleName = vehicle.Name,
                        Plate = vehicle.Plate,
                        DeviceUid = p.DeviceUid,
                        Latitude = p.Latitude,
                        Longitude = p.Longitude,
                        SpeedKph = p.SpeedKph,
                        HeadingDeg = p.HeadingDeg,
                        IgnitionOn = p.IgnitionOn,
                        IsValid = p.IsValid,
                        RecordedAt = p.RecordedAt,
                        CachedAt = p.CachedAt,
                        Source = "redis"
                    };
                })
                .ToList();

            return Ok(result);
        }

        // Fallback to database - single query with join instead of N+1 correlated subqueries
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.GpsDeviceId.HasValue)
            .Include(v => v.GpsDevice)
            .ToListAsync();

        var deviceIds = vehicles.Where(v => v.GpsDeviceId.HasValue).Select(v => v.GpsDeviceId!.Value).ToList();

        // Single query: get latest position per device using GroupBy
        var latestPositions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId))
            .GroupBy(p => p.DeviceId)
            .Select(g => g.OrderByDescending(p => p.RecordedAt).First())
            .ToListAsync();

        var positionMap = latestPositions.ToDictionary(p => p.DeviceId);

        var dbPositions = vehicles.Select(v =>
        {
            positionMap.TryGetValue(v.GpsDeviceId!.Value, out var pos);
            return new RealtimePositionDto
            {
                VehicleId = v.Id,
                VehicleName = v.Name,
                Plate = v.Plate,
                DeviceUid = v.GpsDevice?.DeviceUid ?? "",
                Latitude = pos?.Latitude ?? 0,
                Longitude = pos?.Longitude ?? 0,
                SpeedKph = pos?.SpeedKph ?? 0,
                IgnitionOn = pos?.IgnitionOn ?? false,
                RecordedAt = pos?.RecordedAt ?? default,
                Source = "database"
            };
        }).ToList();

        return Ok(dbPositions);
    }

    // ==================== LATEST POSITIONS ====================

    /// <summary>
    /// Get latest position for all vehicles with GPS
    /// </summary>
    [HttpGet("positions/latest")]
    public async Task<ActionResult<List<VehiclePositionDto>>> GetLatestPositions()
    {
        var companyId = GetCompanyId();

        // Step 1: Get vehicles with GPS devices (single query)
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.GpsDeviceId.HasValue)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .ToListAsync();

        var deviceIds = vehicles
            .Where(v => v.GpsDevice != null)
            .Select(v => v.GpsDevice!.Id)
            .ToList();

        // Step 2: Get latest position per device — ONE grouped query instead of N correlated subqueries
        var latestPositions = new Dictionary<int, PositionDto>();
        if (deviceIds.Any())
        {
            var latestPosIds = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => deviceIds.Contains(p.DeviceId))
                .GroupBy(p => p.DeviceId)
                .Select(g => g.OrderByDescending(p => p.RecordedAt).Select(p => p.Id).First())
                .ToListAsync();

            latestPositions = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => latestPosIds.Contains(p.Id))
                .Select(p => new { p.DeviceId, Dto = new PositionDto
                {
                    Id = p.Id,
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    SpeedKph = p.SpeedKph,
                    CourseDeg = p.CourseDeg,
                    IgnitionOn = p.IgnitionOn,
                    RecordedAt = p.RecordedAt,
                    Address = p.Address
                }})
                .ToDictionaryAsync(x => x.DeviceId, x => x.Dto);
        }

        // Step 3: Join in memory
        var positions = vehicles.Select(v => new VehiclePositionDto
        {
            VehicleId = v.Id,
            VehicleName = v.Name,
            Plate = v.Plate,
            DriverName = v.AssignedDriver?.Name,
            DeviceId = v.GpsDeviceId,
            DeviceUid = v.GpsDevice?.DeviceUid,
            LastPosition = v.GpsDeviceId.HasValue && latestPositions.TryGetValue(v.GpsDeviceId.Value, out var pos) ? pos : null,
            Status = v.Status,
            LastCommunication = v.GpsDevice?.LastCommunication
        }).ToList();

        return Ok(positions);
    }

    /// <summary>
    /// Get latest position for a specific vehicle
    /// </summary>
    [HttpGet("vehicles/{vehicleId}/position")]
    public async Task<ActionResult<VehiclePositionDto>> GetVehiclePosition(int vehicleId)
    {
        var companyId = GetCompanyId();

        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.Id == vehicleId && v.CompanyId == companyId)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .FirstOrDefaultAsync();

        if (vehicle == null)
            return NotFound();

        if (!vehicle.GpsDeviceId.HasValue)
            return Ok(new { message = "Vehicle has no GPS device assigned" });

        var lastPosition = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId)
            .OrderByDescending(p => p.RecordedAt)
            .FirstOrDefaultAsync();

        return Ok(new VehiclePositionDto
        {
            VehicleId = vehicle.Id,
            VehicleName = vehicle.Name,
            Plate = vehicle.Plate,
            DriverName = vehicle.AssignedDriver?.Name,
            DeviceId = vehicle.GpsDeviceId,
            DeviceUid = vehicle.GpsDevice?.DeviceUid,
            LastPosition = lastPosition != null ? new PositionDto
            {
                Id = lastPosition.Id,
                Latitude = lastPosition.Latitude,
                Longitude = lastPosition.Longitude,
                SpeedKph = lastPosition.SpeedKph,
                CourseDeg = lastPosition.CourseDeg,
                IgnitionOn = lastPosition.IgnitionOn,
                RecordedAt = lastPosition.RecordedAt,
                Address = lastPosition.Address
            } : null,
            Status = vehicle.Status,
            LastCommunication = vehicle.GpsDevice?.LastCommunication
        });
    }

    // ==================== POSITION HISTORY ====================

    /// <summary>
    /// Get position history for a vehicle (for playback/route display)
    /// Optimized: uses SQL-level sampling for large date ranges to avoid transferring 10K+ points.
    /// </summary>
    /// <param name="vehicleId">Vehicle ID</param>
    /// <param name="from">Start date (default: 24h ago)</param>
    /// <param name="to">End date (default: now)</param>
    /// <param name="maxPoints">Target max positions (default: 3000). Server will downsample if raw count exceeds this.</param>
    /// <param name="filterDrift">Filter GPS drift when vehicle is stationary (speed &lt; 3 km/h, distance &lt; 15m)</param>
    [HttpGet("vehicles/{vehicleId}/history")]
    public async Task<ActionResult> GetVehicleHistory(
        int vehicleId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int maxPoints = 3000,
        [FromQuery] bool filterDrift = true)
    {
        var companyId = GetCompanyId();

        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound();

        if (!vehicle.GpsDeviceId.HasValue)
            return Ok(new List<PositionDto>());

        from ??= DateTime.UtcNow.AddHours(-24);
        to ??= DateTime.UtcNow;

        var fromUtc = EnsureUtc(from.Value);
        var toUtc = EnsureUtc(to.Value);
        maxPoints = Math.Clamp(maxPoints, 100, 15000);

        // Step 1: Get total count for this range (fast - uses index)
        var totalCount = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId &&
                        p.RecordedAt >= fromUtc &&
                        p.RecordedAt <= toUtc)
            .CountAsync();

        if (totalCount == 0)
            return Ok(new { positions = new List<PositionDto>(), totalCount = 0, sampled = false });

        List<PositionDto> positions;

        if (totalCount <= maxPoints)
        {
            // Small dataset - fetch all, no sampling needed
            positions = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => p.DeviceId == vehicle.GpsDeviceId &&
                            p.RecordedAt >= fromUtc &&
                            p.RecordedAt <= toUtc)
                .OrderBy(p => p.RecordedAt)
                .Select(p => new PositionDto
                {
                    Id = p.Id,
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    SpeedKph = p.SpeedKph,
                    CourseDeg = p.CourseDeg,
                    IgnitionOn = p.IgnitionOn,
                    RecordedAt = p.RecordedAt,
                    Address = p.Address,
                    FuelRaw = p.FuelRaw,
                    OdometerKm = p.OdometerKm,
                    IsRealTime = p.IsRealTime,
                    TemperatureC = p.TemperatureC,
                    CreatedAt = p.CreatedAt,
                    Rpm = p.Rpm,
                    FuelRateLPer100Km = p.FuelRateLPer100Km
                })
                .ToListAsync();
        }
        else
        {
            // Large dataset - use SQL-level sampling with ROW_NUMBER() modulo
            // Keeps every Nth point + always keeps first, last, and speed-change points
            var sampleRate = Math.Max(1, totalCount / maxPoints);

            positions = await _context.GpsPositions
                .FromSqlRaw(@"
                    WITH numbered AS (
                        SELECT *, 
                               ROW_NUMBER() OVER (ORDER BY recorded_at ASC) AS rn,
                               COUNT(*) OVER () AS total,
                               COALESCE(speed_kph, 0) AS spd,
                               COALESCE(LAG(speed_kph) OVER (ORDER BY recorded_at ASC), 0) AS prev_spd
                        FROM gps_positions
                        WHERE device_id = {0}
                          AND recorded_at >= {1}
                          AND recorded_at <= {2}
                    )
                    SELECT id, device_id, latitude, longitude, speed_kph, course_deg,
                           altitude_m, ignition_on, fuel_raw, power_voltage, satellites,
                           is_valid, is_real_time, recorded_at, address, metadata,
                           created_at, event_key, mems_x, mems_y, mems_z,
                           temperature_c, odometer_km, rpm, send_flag, protocol_version,
                           fuel_rate_l_per_100km
                    FROM numbered
                    WHERE rn = 1                           -- always keep first
                       OR rn = total                       -- always keep last
                       OR rn % {3} = 0                     -- every Nth point
                       OR (spd > 3 AND prev_spd <= 3)      -- vehicle started moving
                       OR (spd <= 3 AND prev_spd > 3)      -- vehicle stopped
                       OR ABS(spd - prev_spd) > 20         -- significant speed change
                    ORDER BY recorded_at ASC
                ", vehicle.GpsDeviceId!, fromUtc, toUtc, sampleRate)
                .AsNoTracking()
                .Select(p => new PositionDto
                {
                    Id = p.Id,
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    SpeedKph = p.SpeedKph,
                    CourseDeg = p.CourseDeg,
                    IgnitionOn = p.IgnitionOn,
                    RecordedAt = p.RecordedAt,
                    Address = p.Address,
                    FuelRaw = p.FuelRaw,
                    OdometerKm = p.OdometerKm,
                    IsRealTime = p.IsRealTime,
                    TemperatureC = p.TemperatureC,
                    CreatedAt = p.CreatedAt,
                    Rpm = p.Rpm,
                    FuelRateLPer100Km = p.FuelRateLPer100Km
                })
                .ToListAsync();
        }

        // Convert fuel_raw → percentage based on fuel_sensor_mode (same logic as monitoring)
        var fuelMode = vehicle.GpsDevice?.FuelSensorMode ?? "raw_255";
        var tankCapacity = vehicle.FuelTankCapacity ?? 60;
        foreach (var pos in positions)
        {
            if (pos.FuelRaw.HasValue && pos.FuelRaw.Value > 0)
            {
                var raw = pos.FuelRaw.Value;
                pos.FuelRaw = fuelMode switch
                {
                    "percent" => raw,
                    "raw_255" => (int)Math.Round(raw / 255.0 * 100.0),
                    "liters" => tankCapacity > 0 ? (int)Math.Round(raw * 100.0 / tankCapacity) : raw,
                    "half_liter" => tankCapacity > 0 ? (int)Math.Round(raw * 0.5 * 100.0 / tankCapacity) : (int)Math.Round(raw * 0.5),
                    _ => raw
                };
                if (pos.FuelRaw > 100) pos.FuelRaw = 100;
                if (pos.FuelRaw < 0) pos.FuelRaw = 0;
            }
        }

        // Filter GPS drift if requested
        if (filterDrift && positions.Count >= 2)
        {
            var filtered = new List<PositionDto>(positions.Count) { positions[0] };
            
            for (int i = 1; i < positions.Count; i++)
            {
                var prev = filtered[^1];
                var curr = positions[i];
                
                var prevSpeed = prev.SpeedKph ?? 0;
                var currSpeed = curr.SpeedKph ?? 0;
                
                if (prevSpeed < 3 && currSpeed < 3)
                {
                    var distance = CalculateDistance(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
                    if (distance < 15)
                        continue;
                }
                
                filtered.Add(curr);
            }
            positions = filtered;
        }

        return Ok(new
        {
            positions,
            totalCount,
            returnedCount = positions.Count,
            sampled = totalCount > maxPoints,
            sampleRate = totalCount > maxPoints ? Math.Max(1, totalCount / maxPoints) : 1
        });
    }
    
    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        => GeoMath.HaversineDistance(lat1, lon1, lat2, lon2);

    /// <summary>
    /// Get position history for a device by IMEI
    /// Optimized: same SQL-level sampling as vehicle history endpoint.
    /// </summary>
    [HttpGet("devices/{deviceUid}/history")]
    public async Task<ActionResult> GetDeviceHistory(
        string deviceUid,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int maxPoints = 3000,
        [FromQuery] bool filterDrift = true)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeviceUid == deviceUid && d.CompanyId == companyId);

        if (device == null)
            return NotFound();

        from ??= DateTime.UtcNow.AddHours(-24);
        to ??= DateTime.UtcNow;

        var fromUtc = EnsureUtc(from.Value);
        var toUtc = EnsureUtc(to.Value);
        maxPoints = Math.Clamp(maxPoints, 100, 15000);

        var totalCount = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == device.Id &&
                        p.RecordedAt >= fromUtc &&
                        p.RecordedAt <= toUtc)
            .CountAsync();

        if (totalCount == 0)
            return Ok(new { positions = new List<PositionDto>(), totalCount = 0, sampled = false });

        List<PositionDto> positions;

        if (totalCount <= maxPoints)
        {
            positions = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => p.DeviceId == device.Id &&
                            p.RecordedAt >= fromUtc &&
                            p.RecordedAt <= toUtc)
                .OrderBy(p => p.RecordedAt)
                .Select(p => new PositionDto
                {
                    Id = p.Id,
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    SpeedKph = p.SpeedKph,
                    CourseDeg = p.CourseDeg,
                    IgnitionOn = p.IgnitionOn,
                    RecordedAt = p.RecordedAt,
                    Address = p.Address,
                    FuelRaw = p.FuelRaw,
                    OdometerKm = p.OdometerKm,
                    IsRealTime = p.IsRealTime,
                    TemperatureC = p.TemperatureC,
                    CreatedAt = p.CreatedAt,
                    FuelRateLPer100Km = p.FuelRateLPer100Km
                })
                .ToListAsync();
        }
        else
        {
            var sampleRate = Math.Max(1, totalCount / maxPoints);

            positions = await _context.GpsPositions
                .FromSqlRaw(@"
                    WITH numbered AS (
                        SELECT *, 
                               ROW_NUMBER() OVER (ORDER BY recorded_at ASC) AS rn,
                               COUNT(*) OVER () AS total,
                               COALESCE(speed_kph, 0) AS spd,
                               COALESCE(LAG(speed_kph) OVER (ORDER BY recorded_at ASC), 0) AS prev_spd
                        FROM gps_positions
                        WHERE device_id = {0}
                          AND recorded_at >= {1}
                          AND recorded_at <= {2}
                    )
                    SELECT id, device_id, latitude, longitude, speed_kph, course_deg,
                           altitude_m, ignition_on, fuel_raw, power_voltage, satellites,
                           is_valid, is_real_time, recorded_at, address, metadata,
                           created_at, event_key, mems_x, mems_y, mems_z,
                           temperature_c, odometer_km, rpm, send_flag, protocol_version,
                           fuel_rate_l_per_100km
                    FROM numbered
                    WHERE rn = 1
                       OR rn = total
                       OR rn % {3} = 0
                       OR (spd > 3 AND prev_spd <= 3)
                       OR (spd <= 3 AND prev_spd > 3)
                       OR ABS(spd - prev_spd) > 20
                    ORDER BY recorded_at ASC
                ", device.Id, fromUtc, toUtc, sampleRate)
                .AsNoTracking()
                .Select(p => new PositionDto
                {
                    Id = p.Id,
                    Latitude = p.Latitude,
                    Longitude = p.Longitude,
                    SpeedKph = p.SpeedKph,
                    CourseDeg = p.CourseDeg,
                    IgnitionOn = p.IgnitionOn,
                    RecordedAt = p.RecordedAt,
                    Address = p.Address,
                    FuelRaw = p.FuelRaw,
                    OdometerKm = p.OdometerKm,
                    IsRealTime = p.IsRealTime,
                    TemperatureC = p.TemperatureC,
                    CreatedAt = p.CreatedAt,
                    FuelRateLPer100Km = p.FuelRateLPer100Km
                })
                .ToListAsync();
        }

        // Convert fuel_raw → percentage based on fuel_sensor_mode (same logic as monitoring)
        var deviceFuelMode = device.FuelSensorMode ?? "raw_255";
        var deviceVehicle = await _context.Vehicles.AsNoTracking()
            .FirstOrDefaultAsync(v => v.GpsDeviceId == device.Id);
        var deviceTankCapacity = deviceVehicle?.FuelTankCapacity ?? 60;
        foreach (var pos in positions)
        {
            if (pos.FuelRaw.HasValue && pos.FuelRaw.Value > 0)
            {
                var raw = pos.FuelRaw.Value;
                pos.FuelRaw = deviceFuelMode switch
                {
                    "percent" => raw,
                    "raw_255" => (int)Math.Round(raw / 255.0 * 100.0),
                    "liters" => deviceTankCapacity > 0 ? (int)Math.Round(raw * 100.0 / deviceTankCapacity) : raw,
                    "half_liter" => deviceTankCapacity > 0 ? (int)Math.Round(raw * 0.5 * 100.0 / deviceTankCapacity) : (int)Math.Round(raw * 0.5),
                    _ => raw
                };
                if (pos.FuelRaw > 100) pos.FuelRaw = 100;
                if (pos.FuelRaw < 0) pos.FuelRaw = 0;
            }
        }

        if (filterDrift && positions.Count >= 2)
        {
            var filtered = new List<PositionDto>(positions.Count) { positions[0] };
            for (int i = 1; i < positions.Count; i++)
            {
                var prev = filtered[^1];
                var curr = positions[i];
                if ((prev.SpeedKph ?? 0) < 3 && (curr.SpeedKph ?? 0) < 3)
                {
                    if (CalculateDistance(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude) < 15)
                        continue;
                }
                filtered.Add(curr);
            }
            positions = filtered;
        }

        return Ok(new
        {
            positions,
            totalCount,
            returnedCount = positions.Count,
            sampled = totalCount > maxPoints,
            sampleRate = totalCount > maxPoints ? Math.Max(1, totalCount / maxPoints) : 1
        });
    }

    // ==================== GEOCODING ====================

    /// <summary>
    /// Reverse geocode coordinates to address (with caching)
    /// </summary>
    [HttpGet("geocode/reverse")]
    public async Task<ActionResult<GeocodeResultDto>> ReverseGeocode(
        [FromQuery] double lat,
        [FromQuery] double lon)
    {
        var address = await _geocodingService.ReverseGeocodeAsync(lat, lon);
        return Ok(new GeocodeResultDto
        {
            Latitude = lat,
            Longitude = lon,
            Address = address
        });
    }

    /// <summary>
    /// Get geocoding cache statistics
    /// </summary>
    [HttpGet("geocode/stats")]
    public ActionResult GetGeocodeStats()
    {
        var (hits, misses, size) = _geocodingService.GetCacheStats();
        return Ok(new { cacheHits = hits, cacheMisses = misses, cacheSize = size });
    }

    // ==================== STATISTICS ====================

    /// <summary>
    /// Get GPS statistics for a vehicle
    /// </summary>
    [HttpGet("vehicles/{vehicleId}/stats")]
    public async Task<ActionResult> GetVehicleGpsStats(
        int vehicleId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var companyId = GetCompanyId();

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound();

        if (!vehicle.GpsDeviceId.HasValue)
            return Ok(new { message = "Vehicle has no GPS device" });

        from ??= DateTime.UtcNow.Date;
        to ??= DateTime.UtcNow;

        // Ensure UTC kind for Npgsql timestamptz compatibility
        var fromUtc = EnsureUtc(from.Value);
        var toUtc = EnsureUtc(to.Value);

        var positions = await _context.GpsPositions
            .Where(p => p.DeviceId == vehicle.GpsDeviceId &&
                        p.RecordedAt >= fromUtc &&
                        p.RecordedAt <= toUtc)
            .ToListAsync();

        if (!positions.Any())
            return Ok(new { message = "No data for this period" });

        var stats = new
        {
            Period = new { From = from, To = to },
            PositionCount = positions.Count,
            MaxSpeedKph = positions.Max(p => p.SpeedKph ?? 0),
            AvgSpeedKph = positions.Where(p => p.SpeedKph > 0).Average(p => p.SpeedKph ?? 0),
            IgnitionOnTime = positions.Count(p => p.IgnitionOn == true),
            IgnitionOffTime = positions.Count(p => p.IgnitionOn == false),
            FirstPosition = positions.OrderBy(p => p.RecordedAt).First().RecordedAt,
            LastPosition = positions.OrderByDescending(p => p.RecordedAt).First().RecordedAt
        };

        return Ok(stats);
    }

    // ==================== GPS DEVICES ====================

    /// <summary>
    /// Get available (unassigned) GPS devices for assignment to vehicles
    /// </summary>
    [HttpGet("devices/available")]
    public async Task<ActionResult<List<GpsDeviceDto>>> GetAvailableDevices()
    {
        var companyId = GetCompanyId();

        var devices = await _context.GpsDevices
            .Where(d => d.CompanyId == companyId && 
                        (d.Status == "unassigned" || d.Status == null) &&
                        !_context.Vehicles.Any(v => v.GpsDeviceId == d.Id))
            .OrderByDescending(d => d.LastCommunication)
            .Select(d => new GpsDeviceDto
            {
                Id = d.Id,
                DeviceUid = d.DeviceUid,
                Label = d.Label,
                SimNumber = d.SimNumber,
                SimOperator = d.SimOperator,
                Brand = d.Brand,
                Model = d.Model,
                Status = d.Status,
                LastCommunication = d.LastCommunication
            })
            .ToListAsync();

        return Ok(devices);
    }

    /// <summary>
    /// Get all GPS devices for the company
    /// </summary>
    [HttpGet("devices")]
    public async Task<ActionResult<List<GpsDeviceDto>>> GetAllDevices()
    {
        var companyId = GetCompanyId();

        var devices = await _context.GpsDevices
            .Where(d => d.CompanyId == companyId)
            .OrderByDescending(d => d.LastCommunication)
            .Select(d => new GpsDeviceDto
            {
                Id = d.Id,
                DeviceUid = d.DeviceUid,
                Label = d.Label,
                SimNumber = d.SimNumber,
                SimOperator = d.SimOperator,
                Brand = d.Brand,
                Model = d.Model,
                Status = d.Status,
                LastCommunication = d.LastCommunication,
                AssignedVehicleId = _context.Vehicles.Where(v => v.GpsDeviceId == d.Id).Select(v => (int?)v.Id).FirstOrDefault(),
                AssignedVehicleName = _context.Vehicles.Where(v => v.GpsDeviceId == d.Id).Select(v => v.Name).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(devices);
    }

    // ==================== FLEET OVERVIEW ====================

    /// <summary>
    /// Get fleet overview with online/offline status
    /// </summary>
    [HttpGet("fleet/overview")]
    public async Task<ActionResult> GetFleetOverview()
    {
        var companyId = GetCompanyId();
        var cutoffTime = DateTime.UtcNow.AddMinutes(-5);

        var vehicles = await _context.Vehicles
            .Where(v => v.CompanyId == companyId)
            .Include(v => v.GpsDevice)
            .ToListAsync();

        var vehiclesWithGps = vehicles.Where(v => v.GpsDeviceId.HasValue).ToList();
        var deviceIds = vehiclesWithGps.Select(v => v.GpsDeviceId!.Value).ToList();

        // Get devices with recent communication
        var onlineDevices = await _context.GpsDevices
            .Where(d => deviceIds.Contains(d.Id) && d.LastCommunication > cutoffTime)
            .Select(d => d.Id)
            .ToListAsync();

        var overview = new
        {
            TotalVehicles = vehicles.Count,
            VehiclesWithGps = vehiclesWithGps.Count,
            VehiclesWithoutGps = vehicles.Count - vehiclesWithGps.Count,
            Online = onlineDevices.Count,
            Offline = vehiclesWithGps.Count - onlineDevices.Count,
            Vehicles = vehiclesWithGps.Select(v => new
            {
                v.Id,
                v.Name,
                v.Plate,
                v.Status,
                IsOnline = onlineDevices.Contains(v.GpsDeviceId!.Value),
                LastCommunication = v.GpsDevice?.LastCommunication
            })
        };

        return Ok(overview);
    }

    // ==================== TEST ENDPOINT ====================

    /// <summary>
    /// Test endpoint to simulate SignalR position broadcast
    /// Use this to debug real-time updates without actual GPS data
    /// </summary>
    [HttpPost("test/broadcast")]
    public async Task<ActionResult> TestBroadcast([FromBody] TestPositionUpdateRequest request)
    {
        var companyId = GetCompanyId();
        
        // Find vehicle
        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound(new { message = "Vehicle not found" });

        // Get last position for coordinates
        var lastPosition = vehicle.GpsDeviceId.HasValue 
            ? await _context.GpsPositions
                .Where(p => p.DeviceId == vehicle.GpsDeviceId)
                .OrderByDescending(p => p.RecordedAt)
                .FirstOrDefaultAsync()
            : null;

        var speed = request.SpeedKph ?? 0;
        var ignitionOn = request.IgnitionOn ?? false;
        var isMoving = request.IsMoving ?? (ignitionOn && speed >= 10);

        var positionUpdate = new
        {
            DeviceId = vehicle.GpsDeviceId ?? 0,
            DeviceUid = vehicle.GpsDevice?.DeviceUid ?? "TEST",
            VehicleId = vehicle.Id,
            VehicleName = vehicle.Name,
            Plate = vehicle.Plate,
            Latitude = lastPosition?.Latitude ?? 36.8065,
            Longitude = lastPosition?.Longitude ?? 10.1815,
            SpeedKph = speed,
            CourseDeg = 0,
            IgnitionOn = ignitionOn,
            IsMoving = isMoving,
            RecordedAt = DateTime.UtcNow,
            Timestamp = DateTime.UtcNow
        };

        // Broadcast to company group
        await _gpsHubService.SendPositionUpdateAsync(companyId, positionUpdate);

        // Also broadcast to vehicle group
        await _gpsHubService.SendVehiclePositionAsync(vehicle.Id, positionUpdate);

        return Ok(new { 
            message = "Broadcast sent", 
            companyId,
            vehicleId = vehicle.Id,
            isMoving,
            speed,
            ignitionOn
        });
    }

    /// <summary>
    /// Test endpoint to simulate a full GPS position (goes through MediatR → geofence checks → notifications)
    /// </summary>
    [HttpPost("test/simulate-position")]
    public async Task<ActionResult> TestSimulatePosition([FromBody] SimulatePositionRequest request)
    {
        var companyId = GetCompanyId();

        // Find vehicle and its GPS device
        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound(new { message = "Vehicle not found" });

        var deviceUid = vehicle.GpsDevice?.DeviceUid ?? $"TEST-{vehicle.Id}";

        // Send through MediatR → BroadcastPositionCommandHandler → geofence checks → notifications
        var command = new BroadcastPositionCommand(
            DeviceUid: deviceUid,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            SpeedKph: request.SpeedKph ?? 40,
            CourseDeg: 0,
            IgnitionOn: true,
            RecordedAt: DateTime.UtcNow
        );

        var result = await _mediator.Send(command);

        return Ok(new
        {
            message = "Position simulated via MediatR pipeline",
            companyId,
            vehicleId = vehicle.Id,
            vehicleName = vehicle.Name,
            deviceUid,
            latitude = request.Latitude,
            longitude = request.Longitude,
            broadcasted = result.Broadcasted,
            skipReason = result.SkipReason
        });
    }

    // ==================== REMOTE IMMOBILIZATION (DB-driven) ====================

    [HttpGet("devices/{deviceId}/immobilization")]
    public async Task<IActionResult> GetImmobilizationState(int deviceId)
    {
        var companyId = GetCompanyId();
        var device = await _context.GpsDevices
            .Where(d => d.Id == deviceId && d.CompanyId == companyId)
            .Select(d => new {
                d.ImmobilizationActive,
                d.ImmobilizationBy,
                d.ImmobilizationAt,
                d.CommandGo,
                d.CommandStop
            })
            .FirstOrDefaultAsync();

        if (device == null) return NotFound();

        string? activatedByName = null;
        if (device.ImmobilizationBy.HasValue)
        {
            activatedByName = await _context.Users
                .Where(u => u.Id == device.ImmobilizationBy.Value)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync();
        }

        return Ok(new {
            immobilizationActive = device.ImmobilizationActive,
            immobilizationBy = device.ImmobilizationBy,
            immobilizationByName = activatedByName,
            immobilizationAt = device.ImmobilizationAt,
            commandGo = device.CommandGo,
            commandStop = device.CommandStop
        });
    }

    [HttpPost("devices/{deviceId}/stop")]
    public async Task<IActionResult> StopDevice(int deviceId, [FromServices] INotificationService notificationService)
    {
        return await CreateImmobilizationRequest(deviceId, "STOP", notificationService);
    }

    [HttpPost("devices/{deviceId}/go")]
    public async Task<IActionResult> GoDevice(int deviceId, [FromServices] INotificationService notificationService)
    {
        return await CreateImmobilizationRequest(deviceId, "GO", notificationService);
    }

    private async Task<IActionResult> CreateImmobilizationRequest(int deviceId, string commandType, INotificationService notificationService)
    {
        var companyId = GetCompanyId();
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        var device = await _context.GpsDevices
            .Include(d => d.Vehicle)
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.CompanyId == companyId);

        if (device == null) return NotFound();

        // Check for existing pending request on this device
        var hasPending = await _context.Notifications
            .AnyAsync(n => n.Type == "immobilization_request" && n.ReferenceId == deviceId && !n.IsRead);
        if (hasPending)
            return Conflict(new { success = false, message = "Une demande est déjà en attente pour ce véhicule" });

        var requesterName = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FirstName + " " + u.LastName)
            .FirstOrDefaultAsync() ?? "Utilisateur";

        var vehicleName = device.Vehicle?.Name ?? $"Device #{deviceId}";
        var plate = device.Vehicle?.Plate ?? "";
        var isStop = commandType == "STOP";

        var title = isStop
            ? $"Demande d'arrêt — {vehicleName}"
            : $"Demande de libération — {vehicleName}";
        var message = isStop
            ? $"{requesterName} demande l'arrêt du véhicule {vehicleName}"
            : $"{requesterName} demande la libération du véhicule {vehicleName}";

        var metadata = new Dictionary<string, object>
        {
            ["commandType"] = commandType,
            ["deviceId"] = deviceId,
            ["vehicleId"] = device.Vehicle?.Id ?? 0,
            ["vehicleName"] = vehicleName,
            ["plate"] = plate,
            ["requestedBy"] = userId,
            ["requestedByName"] = requesterName
        };

        // Send notification to system admin (user id=1) for approval
        var notification = await notificationService.CreateAndSendAsync(
            companyId,
            userId: 1,
            type: "immobilization_request",
            title: title,
            message: message,
            priority: "urgent",
            referenceType: "device",
            referenceId: deviceId,
            actionUrl: null,
            metadata: metadata
        );

        return Ok(new { success = true, message = "Demande envoyée à l'administrateur", requestId = notification.Id });
    }

    [HttpGet("devices/{deviceId}/commands")]
    public async Task<IActionResult> GetDeviceCommands(int deviceId, [FromQuery] int limit = 20)
    {
        var companyId = GetCompanyId();
        var commands = await _context.DeviceCommands
            .Where(c => c.DeviceId == deviceId && c.CompanyId == companyId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .Select(c => new {
                c.Id,
                c.CommandType,
                c.CommandText,
                c.Status,
                c.SentAt,
                c.CreatedAt,
                c.Source,
                c.Attempts,
                c.ErrorMessage,
                userName = c.User != null ? c.User.FirstName + " " + c.User.LastName : "Système"
            })
            .ToListAsync();

        return Ok(commands);
    }

    // ==================== IMMOBILIZATION APPROVAL FLOW ====================

    [HttpPost("immobilization-requests/{requestId}/approve")]
    public async Task<IActionResult> ApproveImmobilizationRequest(long requestId, [FromServices] INotificationService notificationService)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (userId != 1) return Forbid();

        var companyId = GetCompanyId();

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == requestId && n.Type == "immobilization_request" && !n.IsRead);
        if (notification == null) return NotFound(new { message = "Demande introuvable ou déjà traitée" });

        var metadata = notification.Metadata ?? new Dictionary<string, object>();
        var deviceId = Convert.ToInt32(metadata.GetValueOrDefault("deviceId", 0));
        var commandType = metadata.GetValueOrDefault("commandType", "STOP")?.ToString() ?? "STOP";
        var vehicleId = Convert.ToInt32(metadata.GetValueOrDefault("vehicleId", 0));
        var vehicleName = metadata.GetValueOrDefault("vehicleName", "")?.ToString() ?? "";
        var requestedBy = Convert.ToInt32(metadata.GetValueOrDefault("requestedBy", 0));

        var device = await _context.GpsDevices
            .Include(d => d.Vehicle)
            .FirstOrDefaultAsync(d => d.Id == deviceId);
        if (device == null) return NotFound(new { message = "Appareil introuvable" });

        var isStop = commandType == "STOP";
        device.ImmobilizationActive = isStop;
        device.ImmobilizationBy = requestedBy;
        device.ImmobilizationAt = DateTime.UtcNow;

        var cmd = new DeviceCommand
        {
            DeviceId = deviceId,
            VehicleId = vehicleId > 0 ? vehicleId : null,
            UserId = requestedBy,
            CommandType = commandType,
            CommandText = isStop ? device.CommandStop : device.CommandGo,
            Status = "pending",
            Source = "manual",
            CompanyId = device.CompanyId
        };
        _context.DeviceCommands.Add(cmd);

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Notify the requester that their request was approved
        if (requestedBy > 0)
        {
            var responseTitle = isStop
                ? $"Arrêt approuvé — {vehicleName}"
                : $"Libération approuvée — {vehicleName}";
            var responseMessage = isStop
                ? $"Votre demande d'arrêt pour {vehicleName} a été approuvée"
                : $"Votre demande de libération pour {vehicleName} a été approuvée";

            await notificationService.CreateAndSendAsync(
                companyId, requestedBy,
                type: "immobilization_response",
                title: responseTitle,
                message: responseMessage,
                priority: "high",
                referenceType: "device",
                referenceId: deviceId,
                metadata: new Dictionary<string, object>
                {
                    ["status"] = "approved",
                    ["commandType"] = commandType,
                    ["vehicleName"] = vehicleName,
                    ["deviceId"] = deviceId
                }
            );
        }

        return Ok(new { success = true, message = "Demande approuvée", commandId = cmd.Id });
    }

    [HttpPost("immobilization-requests/{requestId}/reject")]
    public async Task<IActionResult> RejectImmobilizationRequest(long requestId, [FromServices] INotificationService notificationService)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (userId != 1) return Forbid();

        var companyId = GetCompanyId();

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == requestId && n.Type == "immobilization_request" && !n.IsRead);
        if (notification == null) return NotFound(new { message = "Demande introuvable ou déjà traitée" });

        var metadata = notification.Metadata ?? new Dictionary<string, object>();
        var commandType = metadata.GetValueOrDefault("commandType", "STOP")?.ToString() ?? "STOP";
        var vehicleName = metadata.GetValueOrDefault("vehicleName", "")?.ToString() ?? "";
        var deviceId = Convert.ToInt32(metadata.GetValueOrDefault("deviceId", 0));
        var requestedBy = Convert.ToInt32(metadata.GetValueOrDefault("requestedBy", 0));

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Notify the requester that their request was rejected
        if (requestedBy > 0)
        {
            var isStop = commandType == "STOP";
            var responseTitle = isStop
                ? $"Arrêt refusé — {vehicleName}"
                : $"Libération refusée — {vehicleName}";
            var responseMessage = isStop
                ? $"Votre demande d'arrêt pour {vehicleName} a été refusée"
                : $"Votre demande de libération pour {vehicleName} a été refusée";

            await notificationService.CreateAndSendAsync(
                companyId, requestedBy,
                type: "immobilization_response",
                title: responseTitle,
                message: responseMessage,
                priority: "normal",
                referenceType: "device",
                referenceId: deviceId,
                metadata: new Dictionary<string, object>
                {
                    ["status"] = "rejected",
                    ["commandType"] = commandType,
                    ["vehicleName"] = vehicleName,
                    ["deviceId"] = deviceId
                }
            );
        }

        return Ok(new { success = true, message = "Demande refusée" });
    }

    [HttpGet("immobilization-requests/pending")]
    public async Task<IActionResult> GetPendingImmobilizationRequests()
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (userId != 1) return Forbid();

        var requests = await _context.Notifications
            .Where(n => n.Type == "immobilization_request" && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new {
                n.Id,
                n.Title,
                n.Message,
                n.Priority,
                n.Metadata,
                n.CreatedAt
            })
            .ToListAsync();

        return Ok(requests);
    }

}

// ==================== DTOs ====================

public class VehiclePositionDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public string? DriverName { get; set; }
    public int? DeviceId { get; set; }
    public string? DeviceUid { get; set; }
    public PositionDto? LastPosition { get; set; }
    public string? Status { get; set; }
    public DateTime? LastCommunication { get; set; }
}

public class PositionDto
{
    public long Id { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? SpeedKph { get; set; }
    public double? CourseDeg { get; set; }
    public bool? IgnitionOn { get; set; }
    public DateTime RecordedAt { get; set; }
    public string? Address { get; set; }
    public int? FuelRaw { get; set; }
    public long? OdometerKm { get; set; }
    public bool IsRealTime { get; set; }
    public int? TemperatureC { get; set; }
    public DateTime CreatedAt { get; set; }
    public short? Rpm { get; set; }
    public decimal? FuelRateLPer100Km { get; set; }
}

public class GeocodeResultDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public bool FromCache { get; set; }
}

public class RealtimePositionDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public string DeviceUid { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double SpeedKph { get; set; }
    public double HeadingDeg { get; set; }
    public bool IgnitionOn { get; set; }
    public bool IsValid { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime CachedAt { get; set; }
    public string Source { get; set; } = "redis";
}

public class GpsDeviceDto
{
    public int Id { get; set; }
    public string DeviceUid { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? SimNumber { get; set; }
    public string? SimOperator { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Status { get; set; }
    public DateTime? LastCommunication { get; set; }
    public int? AssignedVehicleId { get; set; }
    public string? AssignedVehicleName { get; set; }
}

public class TestPositionUpdateRequest
{
    public int VehicleId { get; set; }
    public double? SpeedKph { get; set; }
    public bool? IgnitionOn { get; set; }
    public bool? IsMoving { get; set; }
}

public class SimulatePositionRequest
{
    public int VehicleId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? SpeedKph { get; set; }
}
