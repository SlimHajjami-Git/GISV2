using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using GisAPI.Services;

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
    private const double MovementSpeedThresholdKph = 5.0;
    private const int MinGapSecondsMoving = 5;
    private const int MinGapSecondsStopped = 20;

    public GpsController(GisDbContext context, IGeocodingService geocodingService)
    {
        _context = context;
        _geocodingService = geocodingService;
    }

    private List<PositionDto> FilterPositionsByRecordedGap(List<PositionDto> positions)
    {
        if (positions.Count <= 1)
            return positions;

        var filtered = new List<PositionDto>(positions.Count);
        DateTime? lastAcceptedRecordedAt = null;

        foreach (var position in positions)
        {
            var speed = position.SpeedKph ?? 0.0;
            var requiredGapSeconds = speed >= MovementSpeedThresholdKph
                ? MinGapSecondsMoving
                : MinGapSecondsStopped;

            if (!lastAcceptedRecordedAt.HasValue ||
                (position.RecordedAt - lastAcceptedRecordedAt.Value).TotalSeconds >= requiredGapSeconds)
            {
                filtered.Add(position);
                lastAcceptedRecordedAt = position.RecordedAt;
            }
        }

        return filtered;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    // ==================== LATEST POSITIONS ====================

    /// <summary>
    /// Get latest position for all vehicles with GPS
    /// </summary>
    [HttpGet("positions/latest")]
    public async Task<ActionResult<List<VehiclePositionDto>>> GetLatestPositions()
    {
        var companyId = GetCompanyId();

        var positions = await _context.Vehicles
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
            .Where(v => v.Id == vehicleId && v.CompanyId == companyId)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .FirstOrDefaultAsync();

        if (vehicle == null)
            return NotFound();

        if (!vehicle.GpsDeviceId.HasValue)
            return Ok(new { message = "Vehicle has no GPS device assigned" });

        var lastPosition = await _context.GpsPositions
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
    [HttpGet("vehicles/{vehicleId}/history")]
    public async Task<ActionResult<List<PositionDto>>> GetVehicleHistory(
        int vehicleId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 10000)
    {
        var companyId = GetCompanyId();

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound();

        if (!vehicle.GpsDeviceId.HasValue)
            return Ok(new List<PositionDto>());

        from ??= DateTime.UtcNow.AddHours(-24);
        to ??= DateTime.UtcNow;

        var positions = await _context.GpsPositions
            .Where(p => p.DeviceId == vehicle.GpsDeviceId &&
                        p.RecordedAt >= from &&
                        p.RecordedAt <= to)
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

        var filteredPositions = FilterPositionsByRecordedGap(positions);

        return Ok(filteredPositions);
    }

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

        var positions = await _context.GpsPositions
            .Where(p => p.DeviceId == device.Id &&
                        p.RecordedAt >= from &&
                        p.RecordedAt <= to)
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

        var positions = await _context.GpsPositions
            .Where(p => p.DeviceId == vehicle.GpsDeviceId &&
                        p.RecordedAt >= from &&
                        p.RecordedAt <= to)
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
