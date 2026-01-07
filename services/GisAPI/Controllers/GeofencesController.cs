using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;

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

    // ==================== GEOFENCES ====================

    [HttpGet]
    public async Task<ActionResult<List<GeofenceDto>>> GetGeofences([FromQuery] int? groupId = null, [FromQuery] bool? isActive = null)
    {
        var companyId = GetCompanyId();

        var query = _context.Geofences
            .Where(g => g.CompanyId == companyId);

        if (groupId.HasValue)
            query = query.Where(g => g.GroupId == groupId);

        if (isActive.HasValue)
            query = query.Where(g => g.IsActive == isActive);

        var geofences = await query
            .Include(g => g.AssignedVehicles)
            .ThenInclude(gv => gv.Vehicle)
            .Include(g => g.Group)
            .OrderBy(g => g.Name)
            .Select(g => new GeofenceDto
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                Type = g.Type,
                Color = g.Color,
                IconName = g.IconName,
                Coordinates = g.Coordinates,
                CenterLat = g.CenterLat,
                CenterLng = g.CenterLng,
                Radius = g.Radius,
                AlertOnEntry = g.AlertOnEntry,
                AlertOnExit = g.AlertOnExit,
                AlertSpeedLimit = g.AlertSpeedLimit,
                NotificationCooldownMinutes = g.NotificationCooldownMinutes,
                MaxStayDurationMinutes = g.MaxStayDurationMinutes,
                ActiveStartTime = g.ActiveStartTime,
                ActiveEndTime = g.ActiveEndTime,
                ActiveDays = g.ActiveDays,
                IsActive = g.IsActive,
                GroupId = g.GroupId,
                GroupName = g.Group != null ? g.Group.Name : null,
                AssignedVehicleIds = g.AssignedVehicles.Select(v => v.VehicleId).ToList(),
                AssignedVehicleNames = g.AssignedVehicles.Select(v => v.Vehicle!.Plate ?? v.Vehicle.Name).ToList(),
                CreatedAt = g.CreatedAt,
                UpdatedAt = g.UpdatedAt
            })
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
    public async Task<ActionResult<List<GeofenceEventDto>>> GetGeofenceEvents(
        int id, 
        [FromQuery] int limit = 50,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var companyId = GetCompanyId();

        var geofence = await _context.Geofences
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (geofence == null)
            return NotFound();

        var query = _context.GeofenceEvents.Where(e => e.GeofenceId == id);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(e => e.Type == type);

        if (from.HasValue)
            query = query.Where(e => e.Timestamp >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.Timestamp <= to.Value);

        var events = await query
            .Include(e => e.Vehicle)
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .Select(e => new GeofenceEventDto
            {
                Id = e.Id,
                GeofenceId = e.GeofenceId,
                VehicleId = e.VehicleId,
                VehicleName = e.Vehicle != null ? (e.Vehicle.Plate ?? e.Vehicle.Name) : null,
                Type = e.Type,
                Latitude = e.Latitude,
                Longitude = e.Longitude,
                Address = e.Address,
                Speed = e.Speed,
                DurationInsideSeconds = e.DurationInsideSeconds,
                IsNotified = e.IsNotified,
                Timestamp = e.Timestamp
            })
            .ToListAsync();

        return Ok(events);
    }

    // ==================== ALL GEOFENCE EVENTS ====================

    [HttpGet("events")]
    public async Task<ActionResult<List<GeofenceEventDto>>> GetAllEvents(
        [FromQuery] int? geofenceId = null,
        [FromQuery] int? vehicleId = null,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int limit = 50)
    {
        var companyId = GetCompanyId();

        // Get geofence IDs for this company
        var geofenceIds = await _context.Geofences
            .Where(g => g.CompanyId == companyId)
            .Select(g => g.Id)
            .ToListAsync();

        var query = _context.GeofenceEvents
            .Where(e => geofenceIds.Contains(e.GeofenceId));

        if (geofenceId.HasValue)
            query = query.Where(e => e.GeofenceId == geofenceId.Value);

        if (vehicleId.HasValue)
            query = query.Where(e => e.VehicleId == vehicleId.Value);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(e => e.Type == type);

        if (startDate.HasValue)
            query = query.Where(e => e.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(e => e.Timestamp <= endDate.Value);

        var events = await query
            .Include(e => e.Vehicle)
            .Include(e => e.Geofence)
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .Select(e => new GeofenceEventDto
            {
                Id = e.Id,
                GeofenceId = e.GeofenceId,
                GeofenceName = e.Geofence != null ? e.Geofence.Name : null,
                VehicleId = e.VehicleId,
                VehicleName = e.Vehicle != null ? (e.Vehicle.Plate ?? e.Vehicle.Name) : null,
                Type = e.Type,
                Latitude = e.Latitude,
                Longitude = e.Longitude,
                Address = e.Address,
                Speed = e.Speed,
                DurationInsideSeconds = e.DurationInsideSeconds,
                IsNotified = e.IsNotified,
                Timestamp = e.Timestamp
            })
            .ToListAsync();

        return Ok(events);
    }

    // ==================== GEOFENCE GROUPS ====================

    [HttpGet("groups")]
    public async Task<ActionResult<List<GeofenceGroup>>> GetGroups()
    {
        var companyId = GetCompanyId();

        var groups = await _context.GeofenceGroups
            .Where(g => g.CompanyId == companyId)
            .Include(g => g.Geofences)
            .OrderBy(g => g.Name)
            .ToListAsync();

        return Ok(groups);
    }

    [HttpPost("groups")]
    public async Task<ActionResult<GeofenceGroup>> CreateGroup([FromBody] GeofenceGroup group)
    {
        var companyId = GetCompanyId();
        group.CompanyId = companyId;
        group.CreatedAt = DateTime.UtcNow;

        _context.GeofenceGroups.Add(group);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetGroups), new { id = group.Id }, group);
    }

    [HttpPut("groups/{id}")]
    public async Task<ActionResult> UpdateGroup(int id, [FromBody] GeofenceGroup updated)
    {
        var companyId = GetCompanyId();

        var group = await _context.GeofenceGroups
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (group == null)
            return NotFound();

        group.Name = updated.Name;
        group.Description = updated.Description;
        group.Color = updated.Color;
        group.IconName = updated.IconName;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("groups/{id}")]
    public async Task<ActionResult> DeleteGroup(int id)
    {
        var companyId = GetCompanyId();

        var group = await _context.GeofenceGroups
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (group == null)
            return NotFound();

        _context.GeofenceGroups.Remove(group);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ==================== STATISTICS ====================

    [HttpGet("stats")]
    public async Task<ActionResult> GetGeofenceStats([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var companyId = GetCompanyId();
        from ??= DateTime.UtcNow.AddDays(-30);
        to ??= DateTime.UtcNow;

        var geofenceIds = await _context.Geofences
            .Where(g => g.CompanyId == companyId)
            .Select(g => g.Id)
            .ToListAsync();

        var events = await _context.GeofenceEvents
            .Where(e => geofenceIds.Contains(e.GeofenceId) && e.Timestamp >= from && e.Timestamp <= to)
            .GroupBy(e => e.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalGeofences = geofenceIds.Count;
        var activeGeofences = await _context.Geofences
            .CountAsync(g => g.CompanyId == companyId && g.IsActive);

        return Ok(new
        {
            TotalGeofences = totalGeofences,
            ActiveGeofences = activeGeofences,
            TotalEntries = events.FirstOrDefault(e => e.Type == "entry")?.Count ?? 0,
            TotalExits = events.FirstOrDefault(e => e.Type == "exit")?.Count ?? 0,
            TotalSpeedViolations = events.FirstOrDefault(e => e.Type == "speed_violation")?.Count ?? 0,
            TotalOverstays = events.FirstOrDefault(e => e.Type == "overstay")?.Count ?? 0,
            Period = new { From = from, To = to }
        });
    }
}

// ==================== DTOs ====================

public class GeofenceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = "polygon";
    public string Color { get; set; } = "#3b82f6";
    public string? IconName { get; set; }
    public GeofencePoint[]? Coordinates { get; set; }
    public double? CenterLat { get; set; }
    public double? CenterLng { get; set; }
    public double? Radius { get; set; }
    public bool AlertOnEntry { get; set; }
    public bool AlertOnExit { get; set; }
    public int? AlertSpeedLimit { get; set; }
    public int NotificationCooldownMinutes { get; set; }
    public int? MaxStayDurationMinutes { get; set; }
    public TimeSpan? ActiveStartTime { get; set; }
    public TimeSpan? ActiveEndTime { get; set; }
    public string[]? ActiveDays { get; set; }
    public bool IsActive { get; set; }
    public int? GroupId { get; set; }
    public string? GroupName { get; set; }
    public List<int> AssignedVehicleIds { get; set; } = new();
    public List<string> AssignedVehicleNames { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class GeofenceEventDto
{
    public int Id { get; set; }
    public int GeofenceId { get; set; }
    public string? GeofenceName { get; set; }
    public int VehicleId { get; set; }
    public string? VehicleName { get; set; }
    public string Type { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public double? Speed { get; set; }
    public int? DurationInsideSeconds { get; set; }
    public bool IsNotified { get; set; }
    public DateTime Timestamp { get; set; }
}
