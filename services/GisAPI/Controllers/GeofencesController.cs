using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Data;
using GisAPI.Models;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GeofencesController : ControllerBase
{
    private readonly GisDbContext _context;

    public GeofencesController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    [HttpGet]
    public async Task<ActionResult<List<Geofence>>> GetGeofences()
    {
        var companyId = GetCompanyId();

        var geofences = await _context.Geofences
            .Where(g => g.CompanyId == companyId)
            .Include(g => g.AssignedVehicles)
            .OrderBy(g => g.Name)
            .ToListAsync();

        return Ok(geofences);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Geofence>> GetGeofence(int id)
    {
        var companyId = GetCompanyId();

        var geofence = await _context.Geofences
            .Where(g => g.Id == id && g.CompanyId == companyId)
            .Include(g => g.AssignedVehicles)
            .FirstOrDefaultAsync();

        if (geofence == null)
            return NotFound();

        return Ok(geofence);
    }

    [HttpPost]
    public async Task<ActionResult<Geofence>> CreateGeofence([FromBody] Geofence geofence)
    {
        var companyId = GetCompanyId();
        geofence.CompanyId = companyId;
        geofence.CreatedAt = DateTime.UtcNow;

        _context.Geofences.Add(geofence);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetGeofence), new { id = geofence.Id }, geofence);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateGeofence(int id, [FromBody] Geofence updated)
    {
        var companyId = GetCompanyId();

        var geofence = await _context.Geofences
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (geofence == null)
            return NotFound();

        geofence.Name = updated.Name;
        geofence.Description = updated.Description;
        geofence.Type = updated.Type;
        geofence.Color = updated.Color;
        geofence.Coordinates = updated.Coordinates;
        geofence.CenterLat = updated.CenterLat;
        geofence.CenterLng = updated.CenterLng;
        geofence.Radius = updated.Radius;
        geofence.AlertOnEntry = updated.AlertOnEntry;
        geofence.AlertOnExit = updated.AlertOnExit;
        geofence.AlertSpeedLimit = updated.AlertSpeedLimit;
        geofence.IsActive = updated.IsActive;
        geofence.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteGeofence(int id)
    {
        var companyId = GetCompanyId();

        var geofence = await _context.Geofences
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (geofence == null)
            return NotFound();

        _context.Geofences.Remove(geofence);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}/vehicles")]
    public async Task<ActionResult> AssignVehicles(int id, [FromBody] int[] vehicleIds)
    {
        var companyId = GetCompanyId();

        var geofence = await _context.Geofences
            .Include(g => g.AssignedVehicles)
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (geofence == null)
            return NotFound();

        // Remove existing assignments
        geofence.AssignedVehicles.Clear();

        // Add new assignments
        foreach (var vehicleId in vehicleIds)
        {
            geofence.AssignedVehicles.Add(new GeofenceVehicle
            {
                GeofenceId = id,
                VehicleId = vehicleId
            });
        }

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id}/events")]
    public async Task<ActionResult<List<GeofenceEvent>>> GetGeofenceEvents(int id, [FromQuery] int limit = 50)
    {
        var companyId = GetCompanyId();

        var geofence = await _context.Geofences
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (geofence == null)
            return NotFound();

        var events = await _context.GeofenceEvents
            .Where(e => e.GeofenceId == id)
            .Include(e => e.Vehicle)
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .ToListAsync();

        return Ok(events);
    }
}
