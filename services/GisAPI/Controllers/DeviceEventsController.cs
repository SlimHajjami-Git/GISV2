using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/device-events")]
[Authorize]
public class DeviceEventsController : ControllerBase
{
    private readonly IGisDbContext _context;

    public DeviceEventsController(IGisDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetDeviceEvents(
        [FromQuery] string? eventType = null,
        [FromQuery] int? vehicleId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] bool? acknowledged = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var query = _context.DeviceEvents
            .Include(e => e.Vehicle)
            .AsQueryable();

        if (!string.IsNullOrEmpty(eventType))
            query = query.Where(e => e.EventType == eventType);

        if (vehicleId.HasValue)
            query = query.Where(e => e.VehicleId == vehicleId.Value);

        if (from.HasValue)
            query = query.Where(e => e.EventAt >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.EventAt <= to.Value);

        if (acknowledged.HasValue)
            query = query.Where(e => e.Acknowledged == acknowledged.Value);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(e => e.EventAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.DeviceId,
                e.VehicleId,
                VehicleName = e.Vehicle != null ? (e.Vehicle.Name ?? e.Vehicle.Plate) : null,
                e.EventType,
                e.EventAt,
                e.OfflineDurationSecs,
                e.LastKnownLat,
                e.LastKnownLon,
                e.LastKnownAddress,
                e.WasMoving,
                e.Acknowledged,
                e.AcknowledgedBy,
                e.AcknowledgedAt
            })
            .ToListAsync();

        return Ok(new { items, totalCount, page, pageSize });
    }

    [HttpPost("{id}/acknowledge")]
    public async Task<IActionResult> Acknowledge(long id)
    {
        var evt = await _context.DeviceEvents.FindAsync(id);
        if (evt == null) return NotFound();

        var userIdClaim = User.FindFirst("userId")?.Value;
        if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

        evt.Acknowledged = true;
        evt.AcknowledgedBy = userId;
        evt.AcknowledgedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }
}
