using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/device-events")]
[Authorize]
public class DeviceEventsController : ControllerBase
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;

    public DeviceEventsController(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    /// <summary>
    /// Véhicules visibles par l'appelant. TROIS états : <c>null</c> = administrateur,
    /// AUCUN filtre ; liste non vide = ses véhicules ; liste VIDE = il ne voit RIEN.
    ///
    /// La projection publie LastKnownLat, LastKnownLon, LastKnownAddress et WasMoving
    /// par véhicule : la dernière position connue du véhicule loué à un autre client.
    /// </summary>
    private Task<List<int>?> PorteeVehiculesAsync(CancellationToken ct) =>
        VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);

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

        // Un événement sans véhicule rattaché (boîtier en stock) n'entre dans la portée
        // de personne : un non-administrateur ne le voit pas.
        var portee = await PorteeVehiculesAsync(HttpContext.RequestAborted);
        if (portee is not null)
        {
            List<int> ids = portee;
            query = query.Where(e => e.VehicleId.HasValue && ids.Contains(e.VehicleId.Value));
        }

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

        // Acquitter l'événement d'un véhicule hors portée, c'est l'effacer de l'écran
        // de son vrai locataire : même 404 qu'un événement inexistant.
        if (evt.VehicleId.HasValue
            ? !await VehicleScope.CanAccessVehicleAsync(_context, _tenant, evt.VehicleId.Value, HttpContext.RequestAborted)
            : !VehicleScope.SeesWholeFleet(_tenant))
            return NotFound();

        // Le jeton ne porte AUCUN claim « userId » : JwtService écrit l'identifiant dans
        // « sub », que la couche JWT projette sur ClaimTypes.NameIdentifier — c'est ce que
        // lisent TenantMiddleware, PermissionMiddleware, le hub et les autres contrôleurs.
        // En lisant « userId », cette route rendait donc 401 à TOUT LE MONDE : l'acquittement
        // n'a jamais fonctionné, et le contrôle de portée ajouté juste au-dessus ne servait à
        // rien puisque l'appel échouait deux lignes plus bas. Repli sur « sub » comme ailleurs,
        // pour un jeton dont le mappage entrant serait désactivé.
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        if (!int.TryParse(userIdClaim, out var userId)) return Unauthorized();

        evt.Acknowledged = true;
        evt.AcknowledgedBy = userId;
        evt.AcknowledgedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return NoContent();
    }
}
