using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Data;
using GisAPI.DTOs;
using GisAPI.Models;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VehiclesController : ControllerBase
{
    private readonly GisDbContext _context;

    public VehiclesController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    [HttpGet]
    public async Task<ActionResult<List<VehicleDto>>> GetVehicles()
    {
        var companyId = GetCompanyId();

        var vehicles = await _context.Vehicles
            .Where(v => v.CompanyId == companyId)
            .Include(v => v.AssignedDriver)
            .Include(v => v.AssignedSupervisor)
            .Include(v => v.GpsDevice)
            .OrderBy(v => v.Name)
            .Select(v => new VehicleDto(
                v.Id,
                v.Name,
                v.Type,
                v.Brand,
                v.Model,
                v.Plate,
                v.Year,
                v.Color,
                v.Status,
                v.HasGps,
                v.Mileage,
                v.AssignedDriverId,
                v.AssignedDriver != null ? v.AssignedDriver.Name : null,
                v.AssignedSupervisorId,
                v.AssignedSupervisor != null ? v.AssignedSupervisor.Name : null,
                v.GpsDevice != null ? new GpsDeviceDto(
                    v.GpsDevice.Id,
                    v.GpsDevice.DeviceUid,
                    v.GpsDevice.Label,
                    v.GpsDevice.Status,
                    v.GpsDevice.LastCommunication,
                    v.GpsDevice.BatteryLevel,
                    v.GpsDevice.SignalStrength
                ) : null,
                v.CreatedAt
            ))
            .ToListAsync();

        return Ok(vehicles);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<VehicleDto>> GetVehicle(int id)
    {
        var companyId = GetCompanyId();

        var vehicle = await _context.Vehicles
            .Where(v => v.Id == id && v.CompanyId == companyId)
            .Include(v => v.AssignedDriver)
            .Include(v => v.AssignedSupervisor)
            .Include(v => v.GpsDevice)
            .Select(v => new VehicleDto(
                v.Id,
                v.Name,
                v.Type,
                v.Brand,
                v.Model,
                v.Plate,
                v.Year,
                v.Color,
                v.Status,
                v.HasGps,
                v.Mileage,
                v.AssignedDriverId,
                v.AssignedDriver != null ? v.AssignedDriver.Name : null,
                v.AssignedSupervisorId,
                v.AssignedSupervisor != null ? v.AssignedSupervisor.Name : null,
                v.GpsDevice != null ? new GpsDeviceDto(
                    v.GpsDevice.Id,
                    v.GpsDevice.DeviceUid,
                    v.GpsDevice.Label,
                    v.GpsDevice.Status,
                    v.GpsDevice.LastCommunication,
                    v.GpsDevice.BatteryLevel,
                    v.GpsDevice.SignalStrength
                ) : null,
                v.CreatedAt
            ))
            .FirstOrDefaultAsync();

        if (vehicle == null)
            return NotFound();

        return Ok(vehicle);
    }

    [HttpPost]
    public async Task<ActionResult<VehicleDto>> CreateVehicle([FromBody] CreateVehicleRequest request)
    {
        var companyId = GetCompanyId();

        var vehicle = new Vehicle
        {
            Name = request.Name,
            Type = request.Type,
            Brand = request.Brand,
            Model = request.Model,
            Plate = request.Plate,
            Year = request.Year,
            Color = request.Color,
            Mileage = request.Mileage,
            CompanyId = companyId,
            Status = "available"
        };

        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetVehicle), new { id = vehicle.Id }, new VehicleDto(
            vehicle.Id,
            vehicle.Name,
            vehicle.Type,
            vehicle.Brand,
            vehicle.Model,
            vehicle.Plate,
            vehicle.Year,
            vehicle.Color,
            vehicle.Status,
            vehicle.HasGps,
            vehicle.Mileage,
            null, null, null, null, null,
            vehicle.CreatedAt
        ));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateVehicle(int id, [FromBody] UpdateVehicleRequest request)
    {
        var companyId = GetCompanyId();

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == id && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound();

        vehicle.Name = request.Name;
        vehicle.Type = request.Type;
        vehicle.Brand = request.Brand;
        vehicle.Model = request.Model;
        vehicle.Plate = request.Plate;
        vehicle.Year = request.Year;
        vehicle.Color = request.Color;
        vehicle.Status = request.Status;
        vehicle.Mileage = request.Mileage;
        vehicle.AssignedDriverId = request.AssignedDriverId;
        vehicle.AssignedSupervisorId = request.AssignedSupervisorId;
        vehicle.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteVehicle(int id)
    {
        var companyId = GetCompanyId();

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == id && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound();

        _context.Vehicles.Remove(vehicle);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("locations")]
    public async Task<ActionResult<List<VehicleLocationDto>>> GetVehicleLocations()
    {
        var companyId = GetCompanyId();
        var cutoffTime = DateTime.UtcNow.AddMinutes(-5);

        var locations = await _context.Vehicles
            .Where(v => v.CompanyId == companyId && v.HasGps && v.GpsDeviceId != null)
            .Include(v => v.GpsDevice)
            .ThenInclude(d => d!.Positions.OrderByDescending(p => p.RecordedAt).Take(1))
            .Select(v => new
            {
                Vehicle = v,
                LastPosition = v.GpsDevice!.Positions.OrderByDescending(p => p.RecordedAt).FirstOrDefault()
            })
            .Where(x => x.LastPosition != null)
            .Select(x => new VehicleLocationDto(
                x.Vehicle.Id,
                x.Vehicle.Name,
                x.Vehicle.Plate,
                x.LastPosition!.Latitude,
                x.LastPosition.Longitude,
                x.LastPosition.SpeedKph,
                x.LastPosition.CourseDeg,
                x.LastPosition.IgnitionOn,
                x.LastPosition.RecordedAt,
                x.LastPosition.RecordedAt > cutoffTime
            ))
            .ToListAsync();

        return Ok(locations);
    }
}
