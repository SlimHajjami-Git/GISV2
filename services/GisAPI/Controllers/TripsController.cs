using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TripsController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public TripsController(GisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    /// <summary>
    /// Véhicules visibles par l'appelant. TROIS états, à ne jamais confondre :
    /// <c>null</c> = administrateur, AUCUN filtre ; liste non vide = ses véhicules ;
    /// liste VIDE = non-administrateur sans affectation, il ne voit RIEN.
    ///
    /// Chez un LOUEUR (HERTZ), les véhicules d'une même société appartiennent à des
    /// locataires différents : le filtre société ne cloisonne RIEN. Un trajet publie
    /// départ, arrivée, adresses et plaque — c'est la trajectoire du véhicule loué à
    /// un autre client, sous une autre URL que le playback.
    /// </summary>
    private Task<List<int>?> PorteeVehiculesAsync() =>
        VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, HttpContext.RequestAborted);

    /// <summary>
    /// Le véhicule demandé est-il hors de la portée de l'appelant ? Le refus reste un
    /// 404 identique à celui d'un véhicule inexistant : on ne révèle pas qu'il existe.
    /// </summary>
    private async Task<bool> HorsPorteeAsync(int vehicleId) =>
        !await VehicleScope.CanAccessVehicleAsync(_context, _tenantService, vehicleId, HttpContext.RequestAborted);

    [HttpGet]
    public async Task<ActionResult<List<Trip>>> GetTrips(
        [FromQuery] int? vehicleId = null,
        [FromQuery] int? driverId = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int limit = 100)
    {
        var companyId = GetCompanyId();

        var query = _context.Trips
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId)
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .AsQueryable();

        // La portée s'applique AVANT le filtre optionnel, qui ne fait ensuite que
        // l'INTERSECTER : le front n'envoie pas vehicleId quand aucun véhicule n'est
        // sélectionné, c'est justement l'appel qui rendait tout le parc.
        var portee = await PorteeVehiculesAsync();
        if (portee is not null)
        {
            List<int> ids = portee;
            query = query.Where(t => ids.Contains(t.VehicleId));
        }

        if (vehicleId.HasValue)
            query = query.Where(t => t.VehicleId == vehicleId);

        if (driverId.HasValue)
            query = query.Where(t => t.DriverId == driverId);

        if (startDate.HasValue)
            query = query.Where(t => t.StartTime >= DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc));

        if (endDate.HasValue)
            query = query.Where(t => t.EndTime <= DateTime.SpecifyKind(endDate.Value.Date.AddDays(1), DateTimeKind.Utc));

        var trips = await query
            .OrderByDescending(t => t.StartTime)
            .Take(limit)
            .ToListAsync();

        return Ok(trips);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Trip>> GetTrip(long id)
    {
        var companyId = GetCompanyId();

        var trip = await _context.Trips
            .AsNoTracking()
            .Where(t => t.Id == id && t.CompanyId == companyId)
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceNumber))
            .FirstOrDefaultAsync();

        if (trip == null)
            return NotFound();

        // IDOR : le détail porte la trajectoire complète (points de passage). Hors
        // portée, même 404 qu'un trajet inexistant.
        if (await HorsPorteeAsync(trip.VehicleId))
            return NotFound();

        return Ok(trip);
    }

    [HttpGet("vehicle/{vehicleId}")]
    public async Task<ActionResult<List<Trip>>> GetVehicleTrips(
        int vehicleId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int limit = 50)
    {
        var companyId = GetCompanyId();

        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound();

        // IDOR : il suffisait de changer l'identifiant dans l'URL pour lire les
        // trajets du véhicule loué à un autre client.
        if (await HorsPorteeAsync(vehicleId))
            return NotFound();

        var query = _context.Trips
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicleId)
            .Include(t => t.Driver)
            .AsQueryable();

        if (startDate.HasValue)
            query = query.Where(t => t.StartTime >= DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc));

        if (endDate.HasValue)
            query = query.Where(t => t.EndTime <= DateTime.SpecifyKind(endDate.Value.Date.AddDays(1), DateTimeKind.Utc));

        var trips = await query
            .OrderByDescending(t => t.StartTime)
            .Take(limit)
            .ToListAsync();

        return Ok(trips);
    }

    [HttpGet("{id}/waypoints")]
    public async Task<ActionResult<List<TripWaypoint>>> GetTripWaypoints(long id)
    {
        var companyId = GetCompanyId();

        var trip = await _context.Trips
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);

        if (trip == null)
            return NotFound();

        // Les points de passage SONT la trajectoire : même contrôle que le détail.
        if (await HorsPorteeAsync(trip.VehicleId))
            return NotFound();

        var waypoints = await _context.TripWaypoints
            .AsNoTracking()
            .Where(w => w.TripId == id)
            .OrderBy(w => w.SequenceNumber)
            .ToListAsync();

        return Ok(waypoints);
    }

    [HttpGet("summary")]
    public async Task<ActionResult> GetTripsSummary(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var companyId = GetCompanyId();
        var utcStart = DateTime.SpecifyKind((startDate ?? DateTime.UtcNow.AddMonths(-1)).Date, DateTimeKind.Utc);
        var utcEnd = DateTime.SpecifyKind((endDate ?? DateTime.UtcNow).Date.AddDays(1), DateTimeKind.Utc);

        var baseQuery = _context.Trips
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId &&
                        t.StartTime >= utcStart &&
                        t.EndTime <= utcEnd &&
                        t.Status == "completed");

        // Les totaux agrègent le parc : sans portée, un locataire lisait le
        // kilométrage et la consommation des véhicules loués aux autres clients.
        var portee = await PorteeVehiculesAsync();
        if (portee is not null)
        {
            List<int> ids = portee;
            baseQuery = baseQuery.Where(t => ids.Contains(t.VehicleId));
        }

        var summary = await baseQuery
            .GroupBy(t => 1)
            .Select(g => new
            {
                TotalTrips = g.Count(),
                TotalDistanceKm = g.Sum(t => t.DistanceKm),
                TotalDurationMinutes = g.Sum(t => t.DurationMinutes),
                TotalFuelConsumed = g.Sum(t => t.FuelConsumedLiters ?? 0),
                AverageSpeedKph = g.Average(t => t.AverageSpeedKph ?? 0),
                MaxSpeedKph = g.Max(t => t.MaxSpeedKph ?? 0),
                TotalHarshBraking = g.Sum(t => t.HarshBrakingCount ?? 0),
                TotalHarshAcceleration = g.Sum(t => t.HarshAccelerationCount ?? 0),
                TotalOverspeeding = g.Sum(t => t.OverspeedingCount ?? 0)
            })
            .FirstOrDefaultAsync();

        var result = new
        {
            TotalTrips = summary?.TotalTrips ?? 0,
            TotalDistanceKm = summary?.TotalDistanceKm ?? 0,
            TotalDurationMinutes = summary?.TotalDurationMinutes ?? 0,
            TotalFuelConsumed = summary?.TotalFuelConsumed ?? 0,
            AverageSpeedKph = summary?.AverageSpeedKph ?? 0,
            MaxSpeedKph = summary?.MaxSpeedKph ?? 0,
            TotalHarshBraking = summary?.TotalHarshBraking ?? 0,
            TotalHarshAcceleration = summary?.TotalHarshAcceleration ?? 0,
            TotalOverspeeding = summary?.TotalOverspeeding ?? 0,
            Period = new { StartDate = utcStart, EndDate = utcEnd }
        };

        return Ok(result);
    }
}
