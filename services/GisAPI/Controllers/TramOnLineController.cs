using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GisAPI.Data;
using GisAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TramOnLineController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly ILogger<TramOnLineController> _logger;

    public TramOnLineController(GisDbContext context, ILogger<TramOnLineController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Register a MAT (device UID). If it does not exist, create a GPS device and vehicle (HTZ X).
    /// </summary>
    [HttpPost("register/{mat}")]
    public async Task<ActionResult<MatRegistrationResponse>> RegisterMat(string mat)
    {
        if (string.IsNullOrWhiteSpace(mat))
        {
            return BadRequest("MAT is required.");
        }

        mat = mat.Trim();

        var existingDevice = await _context.GpsDevices
            .Include(d => d.Vehicle)
            .FirstOrDefaultAsync(d => d.DeviceUid == mat);

        if (existingDevice != null)
        {
            return Ok(new MatRegistrationResponse(
                false,
                existingDevice.DeviceUid,
                existingDevice.Vehicle?.Name,
                existingDevice.Vehicle?.Id,
                existingDevice.Vehicle?.GpsDeviceId ?? existingDevice.Id));
        }

        var companyId = await _context.Companies
            .OrderBy(c => c.Id)
            .Select(c => c.Id)
            .FirstOrDefaultAsync();

        if (companyId == 0)
        {
            return BadRequest("No company configured. Please create a company first.");
        }

        var htzCount = await _context.Vehicles.CountAsync(v => v.Name.StartsWith("HTZ"));
        var vehicleName = $"HTZ {htzCount}";

        var gpsDevice = new GpsDevice
        {
            DeviceUid = mat,
            CompanyId = companyId,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.GpsDevices.Add(gpsDevice);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle
        {
            Name = vehicleName,
            CompanyId = companyId,
            Status = "available",
            HasGps = true,
            GpsDeviceId = gpsDevice.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created vehicle {VehicleName} for MAT {Mat}", vehicleName, mat);

        return Ok(new MatRegistrationResponse(true, mat, vehicle.Name, vehicle.Id, gpsDevice.Id));
    }

    /// <summary>
    /// Get positions for a MAT.
    /// </summary>
    [HttpGet("{mat}/positions")]
    public async Task<ActionResult<IEnumerable<MatPositionDto>>> GetPositions(string mat, [FromQuery] int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(mat))
        {
            return BadRequest("MAT is required.");
        }

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.DeviceUid == mat.Trim());

        if (device == null)
        {
            return NotFound($"No device found for MAT {mat}.");
        }

        limit = Math.Clamp(limit, 1, 1000);

        var positions = await _context.GpsPositions
            .Where(p => p.DeviceId == device.Id)
            .OrderByDescending(p => p.RecordedAt)
            .Take(limit)
            .Select(p => new MatPositionDto(
                p.Id,
                p.RecordedAt,
                p.Latitude,
                p.Longitude,
                p.SpeedKph,
                p.CourseDeg,
                p.IgnitionOn,
                p.OdometerKm,
                p.Address))
            .ToListAsync();

        return Ok(positions);
    }
}

public record MatRegistrationResponse(
    bool Created,
    string Mat,
    string? VehicleName,
    int? VehicleId,
    int DeviceId);

public record MatPositionDto(
    long Id,
    DateTime RecordedAt,
    double Latitude,
    double Longitude,
    double? SpeedKph,
    double? CourseDeg,
    bool? IgnitionOn,
    long? OdometerKm,
    string? Address);
