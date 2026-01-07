using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AlertsController : ControllerBase
{
    private readonly GisDbContext _context;

    public AlertsController(GisDbContext context)
    {
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    [HttpGet]
    public async Task<ActionResult<List<GpsAlert>>> GetAlerts(
        [FromQuery] bool? resolved = null,
        [FromQuery] string? type = null,
        [FromQuery] int limit = 50)
    {
        var companyId = GetCompanyId();

        var vehicleIds = await _context.Vehicles
            .Where(v => v.CompanyId == companyId)
            .Select(v => v.Id)
            .ToListAsync();

        var query = _context.GpsAlerts
            .Where(a => a.VehicleId.HasValue && vehicleIds.Contains(a.VehicleId.Value))
            .Include(a => a.Vehicle)
            .AsQueryable();

        if (resolved.HasValue)
            query = query.Where(a => a.Resolved == resolved.Value);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(a => a.Type == type);

        var alerts = await query
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .ToListAsync();

        return Ok(alerts);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> GetUnreadCount()
    {
        var companyId = GetCompanyId();

        var vehicleIds = await _context.Vehicles
            .Where(v => v.CompanyId == companyId)
            .Select(v => v.Id)
            .ToListAsync();

        var count = await _context.GpsAlerts
            .Where(a => a.VehicleId.HasValue && 
                        vehicleIds.Contains(a.VehicleId.Value) && 
                        !a.Resolved)
            .CountAsync();

        return Ok(count);
    }

    [HttpPost("{id}/resolve")]
    public async Task<ActionResult> ResolveAlert(int id)
    {
        var userId = GetUserId();

        var alert = await _context.GpsAlerts.FindAsync(id);

        if (alert == null)
            return NotFound();

        alert.Resolved = true;
        alert.ResolvedAt = DateTime.UtcNow;
        alert.ResolvedByUserId = userId;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("resolve-all")]
    public async Task<ActionResult> ResolveAllAlerts()
    {
        var companyId = GetCompanyId();
        var userId = GetUserId();

        var vehicleIds = await _context.Vehicles
            .Where(v => v.CompanyId == companyId)
            .Select(v => v.Id)
            .ToListAsync();

        var unresolvedAlerts = await _context.GpsAlerts
            .Where(a => a.VehicleId.HasValue && 
                        vehicleIds.Contains(a.VehicleId.Value) && 
                        !a.Resolved)
            .ToListAsync();

        foreach (var alert in unresolvedAlerts)
        {
            alert.Resolved = true;
            alert.ResolvedAt = DateTime.UtcNow;
            alert.ResolvedByUserId = userId;
        }

        await _context.SaveChangesAsync();

        return Ok(new { resolved = unresolvedAlerts.Count });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GpsAlert>> GetAlert(int id)
    {
        var alert = await _context.GpsAlerts
            .Include(a => a.Vehicle)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (alert == null)
            return NotFound();

        return Ok(alert);
    }
}
