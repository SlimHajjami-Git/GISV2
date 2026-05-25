using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Controllers;

/// <summary>
/// Standalone tow events (engine-off + speed + displacement), surfaced on the
/// dedicated /remorquages page. Tenant-scoped via the global query filter on
/// <c>TowEvent.CompanyId</c>.
/// </summary>
[ApiController]
[Route("api/towing")]
[Authorize]
public class TowingController : ControllerBase
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public TowingController(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<IActionResult> GetTowEvents(
        [FromQuery] string? status = null,
        [FromQuery] int? vehicleId = null,
        [FromQuery] bool? acknowledged = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _context.TowEvents
            .Include(t => t.Vehicle)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(t => t.Status == status);
        if (vehicleId.HasValue)
            query = query.Where(t => t.VehicleId == vehicleId.Value);
        if (acknowledged.HasValue)
            query = query.Where(t => t.Acknowledged == acknowledged.Value);
        if (from.HasValue)
            query = query.Where(t => t.StartedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.StartedAt <= to.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(t => t.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.VehicleId,
                VehicleName = t.Vehicle != null ? (t.Vehicle.Name ?? t.Vehicle.Plate) : null,
                VehiclePlate = t.Vehicle != null ? t.Vehicle.Plate : null,
                t.DeviceUid,
                t.StartedAt,
                t.LastSeenAt,
                t.EndedAt,
                t.StartLat,
                t.StartLon,
                t.LastLat,
                t.LastLon,
                t.StartAddress,
                t.MaxSpeedKph,
                t.DistanceMeters,
                t.FrameCount,
                t.Status,
                t.Acknowledged,
                t.AcknowledgedAt,
            })
            .ToListAsync(ct);

        return Ok(new
        {
            items,
            totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
        });
    }

    /// <summary>Counts unacknowledged tows — used for a nav badge.</summary>
    [HttpGet("unacknowledged-count")]
    public async Task<IActionResult> GetUnacknowledgedCount(CancellationToken ct = default)
    {
        var count = await _context.TowEvents.CountAsync(t => !t.Acknowledged, ct);
        return Ok(new { count });
    }

    [HttpPost("{id:int}/acknowledge")]
    public async Task<IActionResult> Acknowledge(int id, CancellationToken ct = default)
    {
        var ev = await _context.TowEvents.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (ev == null) return NotFound();

        ev.Acknowledged = true;
        ev.AcknowledgedBy = _tenant.UserId;
        ev.AcknowledgedAt = DateTime.UtcNow;
        ev.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Ok(new { ev.Id, ev.Acknowledged, ev.AcknowledgedAt });
    }
}
