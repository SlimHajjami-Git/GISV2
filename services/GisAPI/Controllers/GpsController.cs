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
    // GPS ingest stores local time as UTC (no offset), so we just tag the kind.
    private static DateTime EnsureUtc(DateTime dt)
    {
        return dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
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

        var positions = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.GpsDeviceId.HasValue)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .Select(v => new VehiclePositionDto
            {
                VehicleId = v.Id,
                VehicleName = v.Name,
                Plate = v.Plate,
                DriverName = v.AssignedDriver != null ? v.AssignedDriver.Name : null,
                DeviceId = v.GpsDeviceId,
                DeviceUid = v.GpsDevice != null ? v.GpsDevice.DeviceUid : null,
                LastPosition = v.GpsDevice != null 
                    ? _context.GpsPositions
                        .Where(p => p.DeviceId == v.GpsDeviceId)
                        .OrderByDescending(p => p.RecordedAt)
                        .Select(p => new PositionDto
                        {
                            Id = p.Id,
                            Latitude = p.Latitude,
                            Longitude = p.Longitude,
                            SpeedKph = p.SpeedKph,
                            CourseDeg = p.CourseDeg,
                            IgnitionOn = p.IgnitionOn,
                            RecordedAt = p.RecordedAt,
                            Address = p.Address
                        })
                        .FirstOrDefault()
                    : null,
                Status = v.Status,
                LastCommunication = v.GpsDevice != null ? v.GpsDevice.LastCommunication : null
            })
            .ToListAsync();

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
    /// </summary>
    /// <param name="vehicleId">Vehicle ID</param>
    /// <param name="from">Start date (default: 24h ago)</param>
    /// <param name="to">End date (default: now)</param>
    /// <param name="limit">Max positions to return (default: 10000)</param>
    /// <param name="filterDrift">Filter GPS drift when vehicle is stationary (speed &lt; 3 km/h, distance &lt; 15m)</param>
    [HttpGet("vehicles/{vehicleId}/history")]
    public async Task<ActionResult<List<PositionDto>>> GetVehicleHistory(
        int vehicleId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 10000,
        [FromQuery] bool filterDrift = false)
    {
        var companyId = GetCompanyId();

        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound();

        if (!vehicle.GpsDeviceId.HasValue)
            return Ok(new List<PositionDto>());

        from ??= DateTime.UtcNow.AddHours(-24);
        to ??= DateTime.UtcNow;

        // Ensure UTC kind for Npgsql timestamptz compatibility
        var fromUtc = EnsureUtc(from.Value);
        var toUtc = EnsureUtc(to.Value);

        var rawPositions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId &&
                        p.RecordedAt >= fromUtc &&
                        p.RecordedAt <= toUtc)
            .OrderBy(p => p.RecordedAt)
            .Take(limit)
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
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        if (!filterDrift || rawPositions.Count < 2)
            return Ok(rawPositions);

        // Filter GPS drift: remove consecutive points within 15m when speed < 3 km/h
        var filtered = new List<PositionDto> { rawPositions[0] };
        
        for (int i = 1; i < rawPositions.Count; i++)
        {
            var prev = filtered[^1]; // Last kept position
            var curr = rawPositions[i];
            
            var prevSpeed = prev.SpeedKph ?? 0;
            var currSpeed = curr.SpeedKph ?? 0;
            
            // If both points are stationary (speed < 3), check distance
            if (prevSpeed < 3 && currSpeed < 3)
            {
                var distance = CalculateDistance(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
                
                // Skip if within 15m (GPS drift)
                if (distance < 15)
                    continue;
            }
            
            filtered.Add(curr);
        }

        return Ok(filtered);
    }
    
    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        => GeoMath.HaversineDistance(lat1, lon1, lat2, lon2);

    /// <summary>
    /// Get position history for a device by IMEI
    /// </summary>
    [HttpGet("devices/{deviceUid}/history")]
    public async Task<ActionResult<List<PositionDto>>> GetDeviceHistory(
        string deviceUid,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 10000)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.DeviceUid == deviceUid && d.CompanyId == companyId);

        if (device == null)
            return NotFound();

        from ??= DateTime.UtcNow.AddHours(-24);
        to ??= DateTime.UtcNow;

        // Ensure UTC kind for Npgsql timestamptz compatibility
        var fromUtc = EnsureUtc(from.Value);
        var toUtc = EnsureUtc(to.Value);

        var positions = await _context.GpsPositions
            .Where(p => p.DeviceId == device.Id &&
                        p.RecordedAt >= fromUtc &&
                        p.RecordedAt <= toUtc)
            .OrderBy(p => p.RecordedAt)
            .Take(limit)
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
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        return Ok(positions);
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
