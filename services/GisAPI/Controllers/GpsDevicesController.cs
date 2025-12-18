using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Data;
using GisAPI.Models;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GpsDevicesController : ControllerBase
{
    private readonly GisDbContext _context;

    public GpsDevicesController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    [HttpGet]
    public async Task<ActionResult<List<GpsDevice>>> GetDevices()
    {
        var companyId = GetCompanyId();

        var devices = await _context.GpsDevices
            .Where(d => d.CompanyId == companyId)
            .Include(d => d.Vehicle)
            .OrderBy(d => d.Label)
            .ToListAsync();

        return Ok(devices);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GpsDevice>> GetDevice(int id)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .Where(d => d.Id == id && d.CompanyId == companyId)
            .Include(d => d.Vehicle)
            .FirstOrDefaultAsync();

        if (device == null)
            return NotFound();

        return Ok(device);
    }

    [HttpGet("unassigned")]
    public async Task<ActionResult<List<GpsDevice>>> GetUnassignedDevices()
    {
        var companyId = GetCompanyId();

        var devices = await _context.GpsDevices
            .Where(d => d.CompanyId == companyId && d.Status == "unassigned")
            .OrderBy(d => d.Label)
            .ToListAsync();

        return Ok(devices);
    }

    [HttpPost]
    public async Task<ActionResult<GpsDevice>> CreateDevice([FromBody] GpsDevice device)
    {
        var companyId = GetCompanyId();

        if (await _context.GpsDevices.AnyAsync(d => d.DeviceUid == device.DeviceUid))
        {
            return BadRequest(new { message = "Un appareil avec cet IMEI existe déjà" });
        }

        device.CompanyId = companyId;
        device.Status = "unassigned";
        device.CreatedAt = DateTime.UtcNow;

        _context.GpsDevices.Add(device);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetDevice), new { id = device.Id }, device);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateDevice(int id, [FromBody] GpsDevice updated)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

        if (device == null)
            return NotFound();

        device.Label = updated.Label;
        device.SimNumber = updated.SimNumber;
        device.SimOperator = updated.SimOperator;
        device.Model = updated.Model;
        device.Brand = updated.Brand;
        device.FirmwareVersion = updated.FirmwareVersion;
        device.Status = updated.Status;
        device.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}/assign/{vehicleId}")]
    public async Task<ActionResult> AssignToVehicle(int id, int vehicleId)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

        if (device == null)
            return NotFound(new { message = "Appareil non trouvé" });

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound(new { message = "Véhicule non trouvé" });

        // Unassign from previous vehicle if any
        if (vehicle.GpsDeviceId.HasValue)
        {
            var previousDevice = await _context.GpsDevices.FindAsync(vehicle.GpsDeviceId);
            if (previousDevice != null)
            {
                previousDevice.Status = "unassigned";
            }
        }

        vehicle.GpsDeviceId = id;
        vehicle.HasGps = true;
        device.Status = "active";
        device.InstallationDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Appareil assigné avec succès" });
    }

    [HttpPost("{id}/unassign")]
    public async Task<ActionResult> UnassignFromVehicle(int id)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

        if (device == null)
            return NotFound();

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.GpsDeviceId == id);

        if (vehicle != null)
        {
            vehicle.GpsDeviceId = null;
            vehicle.HasGps = false;
        }

        device.Status = "unassigned";
        await _context.SaveChangesAsync();

        return Ok(new { message = "Appareil désassigné avec succès" });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteDevice(int id)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

        if (device == null)
            return NotFound();

        // Unassign from vehicle first
        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.GpsDeviceId == id);

        if (vehicle != null)
        {
            vehicle.GpsDeviceId = null;
            vehicle.HasGps = false;
        }

        _context.GpsDevices.Remove(device);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id}/positions")]
    public async Task<ActionResult<List<GpsPosition>>> GetDevicePositions(
        int id,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int limit = 100)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

        if (device == null)
            return NotFound();

        var query = _context.GpsPositions
            .Where(p => p.DeviceId == id)
            .AsQueryable();

        if (startDate.HasValue)
            query = query.Where(p => p.RecordedAt >= startDate);

        if (endDate.HasValue)
            query = query.Where(p => p.RecordedAt <= endDate);

        var positions = await query
            .OrderByDescending(p => p.RecordedAt)
            .Take(limit)
            .ToListAsync();

        return Ok(positions);
    }
}
