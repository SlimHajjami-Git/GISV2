using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Application.Common;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Services;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ToursController : ControllerBase
{
    // IGisDbContext (et non le GisDbContext concret) : même abstraction que la
    // couche Application, et c'est ce qui rend ce contrôleur instanciable dans
    // les tests (ToursLot0Tests). Constat du 18/09/2026 : sur le contexte
    // concret, aucune de ses règles d'accès ne pouvait être vérifiée.
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IValhallaService _valhallaService;
    private readonly IRedisCacheService _redisCache;
    private readonly INotificationService _notifications;
    private readonly ILogger<ToursController> _logger;

    private const string VehicleRefusedMessage = "Véhicule introuvable ou non affecté à votre compte";
    /// <summary>Démarrage manuel refusé : le chauffeur de cette tournée envoyée est déjà en tournée.</summary>
    public const string DriverBusyCode = "DRIVER_BUSY";

    public ToursController(IGisDbContext context, ICurrentTenantService tenant, IValhallaService valhallaService,
        IRedisCacheService redisCache, INotificationService notifications, ILogger<ToursController> logger)
    {
        _context = context;
        _tenant = tenant;
        _valhallaService = valhallaService;
        _redisCache = redisCache;
        _notifications = notifications;
        _logger = logger;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    // ────────────────── PORTÉE ET VALIDATIONS ──────────────────

    private List<int>? _vehicleScope;
    private bool _vehicleScopeLoaded;

    /// <summary>Véhicules visibles par l'appelant (null = tout le parc), lus
    /// une fois par requête.</summary>
    private async Task<List<int>?> VehicleScopeAsync(CancellationToken ct)
    {
        if (!_vehicleScopeLoaded)
        {
            _vehicleScope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);
            _vehicleScopeLoaded = true;
        }
        return _vehicleScope;
    }

    /// <summary>
    /// Tournées visibles par l'appelant : celles de sa société et, pour un
    /// utilisateur non administrateur, des seuls véhicules qui lui sont
    /// affectés (VehicleScope). Constat du 18/09/2026 : /api/tours ne filtrait
    /// que par société — un employé restreint ayant le droit Tournées listait,
    /// ouvrait, modifiait et suivait en direct les tournées de tout le parc.
    /// Toutes les routes passent par ici : une tournée hors portée répond 404
    /// comme une tournée inexistante, pour ne pas révéler son existence.
    /// </summary>
    private async Task<IQueryable<Tour>> ScopedToursAsync(CancellationToken ct)
    {
        var companyId = GetCompanyId();
        var scope = await VehicleScopeAsync(ct);

        var query = _context.Tours.Where(t => t.CompanyId == companyId);
        if (scope is not null)
            query = query.Where(t => scope.Contains(t.VehicleId));
        return query;
    }

    /// <summary>
    /// Véhicule affectable à une tournée : de la société de l'appelant ET dans
    /// sa portée. Constat du 18/09/2026 : la modification acceptait n'importe
    /// quel identifiant, alors que le moniteur tourne hors filtre société et
    /// notifie « Point atteint » à la société de la tournée — pointer une
    /// tournée sur le véhicule d'une autre société donnait sa position.
    /// </summary>
    private async Task<Vehicle?> FindAssignableVehicleAsync(int vehicleId, CancellationToken ct)
    {
        var scope = await VehicleScopeAsync(ct);
        if (scope is not null && !scope.Contains(vehicleId)) return null;

        var companyId = GetCompanyId();
        return await _context.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId && v.CompanyId == companyId, ct);
    }

    /// <summary>
    /// Chauffeur affectable : une fiche active de la société de l'appelant.
    /// Jusqu'au 18/09/2026 l'identifiant était enregistré sans contrôle, y
    /// compris celui d'un chauffeur d'une autre société. Ne s'applique qu'à un
    /// chauffeur qu'on AFFECTE (création, changement) — cf. UpdateTour.
    /// Retourne le message d'erreur, ou null.
    /// </summary>
    private async Task<string?> ValidateDriverAsync(int driverId, CancellationToken ct)
    {
        var companyId = GetCompanyId();
        var driver = await _context.Drivers.AsNoTracking()
            .Where(d => d.Id == driverId && d.CompanyId == companyId)
            .Select(d => new { d.Status })
            .FirstOrDefaultAsync(ct);

        if (driver == null) return "Chauffeur introuvable";
        if (!string.Equals(driver.Status, "active", StringComparison.OrdinalIgnoreCase))
            return "Ce chauffeur n'est pas actif";
        return null;
    }

    /// <summary>
    /// Les zones liées aux étapes doivent appartenir à la société : le moniteur
    /// lit les entrées de zone hors filtre société, une zone étrangère ferait
    /// fuiter les passages d'autres véhicules. Retourne le message d'erreur, ou null.
    /// </summary>
    private async Task<string?> ValidateGeofencesAsync(IEnumerable<TourWaypointRequest> waypoints, CancellationToken ct)
    {
        var ids = waypoints
            .Select(w => NormalizeGeofenceId(w.GeofenceId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return null;

        var companyId = GetCompanyId();
        var found = await _context.Geofences.AsNoTracking()
            .CountAsync(g => g.CompanyId == companyId && ids.Contains(g.Id), ct);
        return found == ids.Count ? null : "Zone géofence introuvable";
    }

    private static int? NormalizeGeofenceId(int? id) => id is > 0 ? id : null;

    private static decimal FuelRatePer100Km(Vehicle? vehicle) => vehicle?.FuelType == "essence" ? 7.0m : 9.0m;

    private static List<TourPlanning.StopInput> ToStops(IEnumerable<TourWaypointRequest> waypoints) =>
        waypoints.Select(w => new TourPlanning.StopInput(w.Latitude, w.Longitude, w.PlannedPauseMinutes)).ToList();

    /// <summary>
    /// Étape à enregistrer, avec les mêmes règles à la création et à la
    /// modification. Constat du 18/09/2026 : la modification recréait les
    /// étapes sans heure prévue, sans zone et avec la marge par défaut — une
    /// tournée modifiée perdait tout contrôle de délai.
    /// </summary>
    private static TourWaypoint BuildWaypoint(int tourId, int index, int count, TourWaypointRequest wp,
        TourPlanning.StopEstimate estimate) => new()
    {
        TourId = tourId,
        SequenceOrder = index,
        Name = wp.Name,
        Address = wp.Address,
        Latitude = wp.Latitude,
        Longitude = wp.Longitude,
        Type = index == 0 ? "origin" : (index == count - 1 ? "destination" : "waypoint"),
        GeofenceId = NormalizeGeofenceId(wp.GeofenceId),
        EstimatedLegMinutes = estimate.LegMinutes,
        DeadlineMarginMinutes = wp.DeadlineMarginMinutes > 0 ? wp.DeadlineMarginMinutes : TourPlanning.DefaultDeadlineMarginMinutes,
        EstimatedArrivalTime = estimate.EstimatedArrivalTime,
        PlannedPauseMinutes = wp.PlannedPauseMinutes,
        WaypointStatus = "pending"
    };

    private async Task<ValhallaRouteResult?> TryRouteAsync(IEnumerable<TourWaypointRequest> waypoints)
    {
        try
        {
            var points = waypoints.Select(w => new ValhallaPoint { Lat = w.Latitude, Lon = w.Longitude }).ToList();
            return await _valhallaService.GetRouteFromWaypointsAsync(points);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Valhalla route estimation failed for tour");
            return null;
        }
    }

    // ────────────────── LIST ──────────────────

    [HttpGet]
    public async Task<ActionResult> GetTours(
        [FromQuery] string? status = null,
        [FromQuery] int? vehicleId = null,
        [FromQuery] int? driverId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = (await ScopedToursAsync(ct))
            .AsNoTracking()
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(t => t.Status == status);
        if (vehicleId.HasValue)
            query = query.Where(t => t.VehicleId == vehicleId);
        if (driverId.HasValue)
            query = query.Where(t => t.DriverId == driverId);
        if (from.HasValue)
            query = query.Where(t => t.ScheduledStartTime >= DateTime.SpecifyKind(from.Value, DateTimeKind.Utc));
        if (to.HasValue)
            query = query.Where(t => t.ScheduledStartTime <= DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc));

        var totalCount = await query.CountAsync(ct);
        var tours = await query
            .OrderByDescending(t => t.ScheduledStartTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => MapToDto(t))
            .ToListAsync(ct);

        return Ok(new { items = tours, totalCount, page, pageSize });
    }

    // ────────────────── GET BY ID ──────────────────

    [HttpGet("{id}")]
    public async Task<ActionResult> GetTour(int id, CancellationToken ct = default)
    {
        var tour = await (await ScopedToursAsync(ct))
            .Where(t => t.Id == id)
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .Include(t => t.Pauses.OrderBy(p => p.StartTime))
            .FirstOrDefaultAsync(ct);

        if (tour == null) return NotFound();

        // AUTO-RÉPARATION : les tournées créées avant le correctif ValhallaService
        // (EncodedPolyline jamais renseigné sur le chemin per-leg) ont
        // EstimatedRoutePolyline = NULL → la carte du détail retombait sur une
        // liaison droite. On recalcule l'itinéraire UNE fois à l'ouverture et on
        // ne persiste QUE la polyline (ni les estimations ni les étapes — une
        // tournée terminée ne doit pas voir ses chiffres changer). Best-effort :
        // si Valhalla est indisponible, on sert le détail tel quel.
        if (string.IsNullOrEmpty(tour.EstimatedRoutePolyline) && tour.Waypoints.Count >= 2)
        {
            try
            {
                var pts = tour.Waypoints.OrderBy(w => w.SequenceOrder)
                    .Select(w => new ValhallaPoint { Lat = w.Latitude, Lon = w.Longitude }).ToList();
                var route = await _valhallaService.GetRouteFromWaypointsAsync(pts);
                if (!string.IsNullOrEmpty(route?.EncodedPolyline))
                {
                    tour.EstimatedRoutePolyline = route.EncodedPolyline;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Tour {TourId}: EstimatedRoutePolyline backfilled ({Len} chars)", tour.Id, route.EncodedPolyline.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tour {TourId}: polyline backfill failed", tour.Id);
            }
        }

        // Chronologie GPS (départ/arrêts/arrivée) — uniquement sur le détail
        // consulté, best-effort.
        object? timeline = null;
        try { timeline = await BuildTimelineAsync(tour); }
        catch (Exception ex) { _logger.LogWarning(ex, "Tour {TourId}: timeline build failed", tour.Id); }

        return Ok(MapToDetailDto(tour, timeline));
    }

    // ────────────────── CREATE ──────────────────

    [HttpPost]
    public async Task<ActionResult> CreateTour([FromBody] CreateTourRequest request, CancellationToken ct = default)
    {
        var companyId = GetCompanyId();

        if (request.Waypoints == null || request.Waypoints.Count < 2)
            return BadRequest(new { message = "Au moins 2 points (origine + destination) sont requis" });

        var vehicle = await FindAssignableVehicleAsync(request.VehicleId, ct);
        if (vehicle == null) return BadRequest(new { message = VehicleRefusedMessage });

        if (request.DriverId.HasValue)
        {
            var driverError = await ValidateDriverAsync(request.DriverId.Value, ct);
            if (driverError != null) return BadRequest(new { message = driverError });
        }

        var geofenceError = await ValidateGeofencesAsync(request.Waypoints, ct);
        if (geofenceError != null) return BadRequest(new { message = geofenceError });

        // Calculate route estimation via Valhalla
        decimal estimatedDistanceKm = 0;
        int estimatedDurationMinutes = 0;
        string? routePolyline = null;

        var routeResult = await TryRouteAsync(request.Waypoints);
        if (routeResult != null)
        {
            estimatedDistanceKm = (decimal)routeResult.TotalDistanceKm;
            estimatedDurationMinutes = (int)Math.Ceiling(routeResult.TotalTimeSeconds / 60.0);
            routePolyline = routeResult.EncodedPolyline;
        }

        // Estimate fuel based on vehicle type (avg 8L/100km for trucks, 6L/100km for cars)
        var estimatedFuel = estimatedDistanceKm * FuelRatePer100Km(vehicle) / 100m;

        // Calculate total planned pause time
        int totalPauseMinutes = request.Waypoints.Sum(w => w.PlannedPauseMinutes);

        // Calculate estimated arrival at each waypoint
        var scheduledStart = DateTime.SpecifyKind(request.ScheduledStartTime, DateTimeKind.Utc);
        var scheduledEnd = scheduledStart.AddMinutes(estimatedDurationMinutes + totalPauseMinutes);

        var tour = new Tour
        {
            Name = request.Name,
            Description = request.Description,
            VehicleId = request.VehicleId,
            DriverId = request.DriverId,
            Status = "planned",
            ScheduledStartTime = scheduledStart,
            ScheduledEndTime = scheduledEnd,
            EstimatedDistanceKm = estimatedDistanceKm,
            EstimatedDurationMinutes = estimatedDurationMinutes,
            EstimatedFuelLiters = estimatedFuel,
            EstimatedRoutePolyline = routePolyline,
            TotalPauseMinutes = totalPauseMinutes,
            Recurrence = request.Recurrence ?? "none",
            Notes = request.Notes,
            CompanyId = companyId
        };

        _context.Tours.Add(tour);
        await _context.SaveChangesAsync(ct);

        // Heures d'arrivée prévues par étape : tronçons de l'itinéraire + pauses,
        // durée de repli si le routage est indisponible (règle commune à la
        // création et à la modification, cf. TourPlanning).
        var estimates = TourPlanning.EstimateFromRoute(
            scheduledStart, ToStops(request.Waypoints), routeResult?.LegTimesSeconds, routeResult?.TotalTimeSeconds ?? 0);
        for (int i = 0; i < request.Waypoints.Count; i++)
            _context.TourWaypoints.Add(BuildWaypoint(tour.Id, i, request.Waypoints.Count, request.Waypoints[i], estimates[i]));
        await _context.SaveChangesAsync(ct);

        // Reload with includes
        var created = await _context.Tours
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .FirstAsync(t => t.Id == tour.Id, ct);

        return CreatedAtAction(nameof(GetTour), new { id = tour.Id }, MapToDetailDto(created));
    }

    // ────────────────── UPDATE ──────────────────

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateTour(int id, [FromBody] UpdateTourRequest request, CancellationToken ct = default)
    {
        var tour = await (await ScopedToursAsync(ct))
            .Include(t => t.Waypoints)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tour == null) return NotFound();
        if (tour.Status != "planned")
            return BadRequest(new { message = "Seules les tournées planifiées peuvent être modifiées" });

        // Tout est validé AVANT de toucher à l'entité.
        Vehicle? requestedVehicle = null;
        if (request.VehicleId.HasValue)
        {
            requestedVehicle = await FindAssignableVehicleAsync(request.VehicleId.Value, ct);
            if (requestedVehicle == null) return BadRequest(new { message = VehicleRefusedMessage });
        }

        // Seul un CHANGEMENT de chauffeur est validé. L'écran renvoie toujours le
        // chauffeur déjà porté : devenu inactif, ou dont la fiche a été supprimée
        // (DeleteDriverCommand supprime la ligne et tours."DriverId" n'a pas de
        // clé étrangère), il ne doit pas bloquer la modification du reste de la
        // tournée (relecture du 18/09/2026).
        if (request.DriverIdSpecified && request.DriverId.HasValue && request.DriverId != tour.DriverId)
        {
            var driverError = await ValidateDriverAsync(request.DriverId.Value, ct);
            if (driverError != null) return BadRequest(new { message = driverError });
        }

        var rebuildWaypoints = request.Waypoints != null && request.Waypoints.Count >= 2;
        if (rebuildWaypoints)
        {
            var geofenceError = await ValidateGeofencesAsync(request.Waypoints!, ct);
            if (geofenceError != null) return BadRequest(new { message = geofenceError });
        }

        var previousStart = tour.ScheduledStartTime;
        // Tournée déjà ENVOYÉE dont on retire le chauffeur : il est prévenu après
        // l'enregistrement, sous le nom qu'il a reçu (cf. plus bas).
        int? ficheRetiree = null;
        var nomRecu = tour.Name;

        if (request.Name != null) tour.Name = request.Name;
        if (request.Description != null) tour.Description = request.Description;
        if (requestedVehicle != null) tour.VehicleId = requestedVehicle.Id;
        // Propriété présente = choix explicite, null compris (« Aucun chauffeur ») :
        // jusqu'au 18/09/2026 un chauffeur affecté ne pouvait plus être retiré.
        if (request.DriverIdSpecified && request.DriverId != tour.DriverId)
        {
            // Autre chauffeur : la tournée n'est plus « envoyée » — l'ancien ne doit plus
            // la voir sur son téléphone, le nouveau ne l'a pas encore reçue.
            if (tour.SentAt.HasValue) ficheRetiree = tour.DriverId;
            tour.DriverId = request.DriverId;
            tour.SentAt = null;
            tour.FirstSentAt = null;
            tour.SentByUserId = null;
            tour.OpenedAt = null;
        }
        if (request.Notes != null) tour.Notes = request.Notes;

        if (request.ScheduledStartTime.HasValue)
            tour.ScheduledStartTime = DateTime.SpecifyKind(request.ScheduledStartTime.Value, DateTimeKind.Utc);

        if (rebuildWaypoints)
        {
            var requested = request.Waypoints!;
            var previous = tour.Waypoints.OrderBy(w => w.SequenceOrder).ToList();

            var routeResult = await TryRouteAsync(requested);
            if (routeResult != null)
            {
                tour.EstimatedDistanceKm = (decimal)routeResult.TotalDistanceKm;
                tour.EstimatedDurationMinutes = (int)Math.Ceiling(routeResult.TotalTimeSeconds / 60.0);
                tour.EstimatedRoutePolyline = routeResult.EncodedPolyline;

                var vehicle = requestedVehicle
                    ?? await _context.Vehicles.FirstOrDefaultAsync(v => v.Id == tour.VehicleId, ct);
                tour.EstimatedFuelLiters = tour.EstimatedDistanceKm * FuelRatePer100Km(vehicle) / 100m;
            }

            // Routage indisponible : on repart des tronçons inchangés au lieu de
            // tout effacer, les nouveaux prennent la durée de repli
            // (TourPlanning.EstimateFromPrevious).
            var stops = ToStops(requested);
            var estimates = routeResult != null
                ? TourPlanning.EstimateFromRoute(tour.ScheduledStartTime, stops, routeResult.LegTimesSeconds, routeResult.TotalTimeSeconds)
                : TourPlanning.EstimateFromPrevious(tour.ScheduledStartTime, stops, previous);

            _context.TourWaypoints.RemoveRange(previous);

            int totalPause = requested.Sum(w => w.PlannedPauseMinutes);
            tour.TotalPauseMinutes = totalPause;
            tour.ScheduledEndTime = tour.ScheduledStartTime.AddMinutes(tour.EstimatedDurationMinutes + totalPause);

            for (int i = 0; i < requested.Count; i++)
                _context.TourWaypoints.Add(BuildWaypoint(tour.Id, i, requested.Count, requested[i], estimates[i]));
        }
        else if (tour.ScheduledStartTime != previousStart)
        {
            // Départ déplacé sans toucher aux étapes : les heures prévues suivent,
            // sinon les échéances resteraient calées sur l'ancienne heure.
            var shift = tour.ScheduledStartTime - previousStart;
            foreach (var w in tour.Waypoints.Where(w => w.EstimatedArrivalTime.HasValue))
                w.EstimatedArrivalTime = w.EstimatedArrivalTime!.Value + shift;
            if (tour.ScheduledEndTime.HasValue)
                tour.ScheduledEndTime = tour.ScheduledEndTime.Value + shift;
        }

        await _context.SaveChangesAsync(ct);

        // L'ancien chauffeur avait reçu la tournée sur son téléphone : il doit savoir tout
        // de suite qu'elle n'est plus à lui, comme pour une annulation — sinon la
        // notification « Nouvelle tournée » reste dans sa barre et il part la faire
        // (relecture du 21/09/2026, F10).
        if (ficheRetiree is int ancienne)
            await PrevenirFicheAsync(tour, ancienne, "tour_cancelled",
                $"Tournée retirée : {nomRecu}",
                tour.DriverId.HasValue
                    ? "Votre gestionnaire a confié cette tournée à un autre chauffeur."
                    : "Votre gestionnaire vous a retiré cette tournée.", ct);

        var updated = await _context.Tours
            .Include(t => t.Vehicle).Include(t => t.Driver)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .FirstAsync(t => t.Id == tour.Id, ct);

        return Ok(MapToDetailDto(updated));
    }

    // ────────────────── DELETE ──────────────────

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteTour(int id, CancellationToken ct = default)
    {
        var tour = await (await ScopedToursAsync(ct)).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tour == null) return NotFound();

        // Supprimée alors que le chauffeur l'a reçue et peut encore la faire : il est
        // prévenu AVANT la suppression, comme pour une annulation (F10). Une tournée déjà
        // terminée ou annulée ne lui demande plus rien — pas de notification.
        if (tour.Status is "planned" or "in_progress")
            await PrevenirChauffeurAsync(tour, "tour_cancelled",
                $"Tournée retirée : {tour.Name}",
                "Cette tournée a été supprimée par votre gestionnaire.", ct);

        _context.Tours.Remove(tour);
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    // ────────────────── START TOUR ──────────────────

    [HttpPost("{id}/start")]
    public async Task<ActionResult> StartTour(int id, CancellationToken ct = default)
    {
        var tour = await (await ScopedToursAsync(ct))
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tour == null) return NotFound();
        if (tour.Status != "planned")
            return BadRequest(new { message = "La tournée doit être en statut 'planifiée' pour démarrer" });

        // Tournée ENVOYÉE à un chauffeur déjà en route sur une autre (relecture du 21/09/2026,
        // R11c) : la démarrer d'ici en ferait sa tournée en cours la plus récente, et son
        // téléphone y basculait — la trace de la tournée qu'il fait vraiment partait sur
        // celle-ci, et son suivi s'arrêtait. C'est à lui de dire « Je pars » quand il la
        // commence ; ou au gestionnaire de clôturer d'abord l'autre.
        if (tour.SentAt != null && tour.DriverId is int ficheId)
        {
            var plancher = DateTime.UtcNow - Services.Tours.DriverTourRules.MaxTrackingDuration;
            var autre = await _context.Tours.AsNoTracking()
                .Where(t => t.CompanyId == tour.CompanyId && t.DriverId == ficheId && t.Id != tour.Id
                            && t.Status == "in_progress" && (t.ActualStartTime ?? t.ScheduledStartTime) >= plancher)
                .OrderByDescending(t => t.ActualStartTime ?? t.ScheduledStartTime)
                .Select(t => new { t.Id, t.Name, t.VehicleId })
                .FirstOrDefaultAsync(ct);
            if (autre != null)
            {
                // Le refus vaut pour tous ; le nom et l'id de l'autre tournée ne sont donnés
                // qu'à qui voit son véhicule (même cloisonnement que ScopedToursAsync).
                var scope = await VehicleScopeAsync(ct);
                var visible = scope is null || scope.Contains(autre.VehicleId);
                return Conflict(new
                {
                    code = DriverBusyCode,
                    otherTourId = visible ? autre.Id : (int?)null,
                    otherTourName = visible ? autre.Name : null,
                    message = (visible ? $"Le chauffeur est déjà en tournée (« {autre.Name} »)." : "Le chauffeur est déjà en tournée.")
                              + " Il démarrera celle-ci par « Je pars » depuis son application, ou terminez d'abord l'autre tournée."
                });
            }
        }

        // Même règle que le démarrage automatique : avant le 18/09/2026 un
        // démarrage manuel en retard gardait les échéances calées sur l'heure
        // prévue (étapes aussitôt « temps dépassé ») et l'origine cochée restait
        // « pending ».
        var shift = TourPlanning.Start(tour, DateTime.UtcNow);

        await _context.SaveChangesAsync(ct);
        return Ok(new
        {
            message = "Tournée démarrée",
            actualStartTime = tour.ActualStartTime,
            estimatesShiftedMinutes = (int)Math.Round(shift.TotalMinutes)
        });
    }

    // ────────────────── COMPLETE WAYPOINT ──────────────────

    [HttpPost("{id}/waypoints/{waypointId}/complete")]
    public async Task<ActionResult> CompleteWaypoint(int id, int waypointId, CancellationToken ct = default)
    {
        var tour = await (await ScopedToursAsync(ct))
            .Include(t => t.Waypoints)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tour == null) return NotFound();
        if (tour.Status != "in_progress")
            return BadRequest(new { message = "La tournée doit être en cours" });

        var waypoint = tour.Waypoints.FirstOrDefault(w => w.Id == waypointId);
        if (waypoint == null) return NotFound(new { message = "Point de passage introuvable" });

        // Déjà atteinte (détection GPS, double clic) : l'heure d'arrivée
        // enregistrée est conservée, seul l'état est remis en cohérence.
        if (!waypoint.IsCompleted)
            TourPlanning.MarkReached(waypoint, DateTime.UtcNow);
        else
            waypoint.WaypointStatus = "completed";
        await _context.SaveChangesAsync(ct);

        return Ok(new { message = "Point de passage complété", actualArrivalTime = waypoint.ActualArrivalTime });
    }

    // ────────────────── ADD PAUSE ──────────────────

    [HttpPost("{id}/pauses")]
    public async Task<ActionResult> AddPause(int id, [FromBody] AddPauseRequest request, CancellationToken ct = default)
    {
        var tour = await (await ScopedToursAsync(ct)).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tour == null) return NotFound();
        if (tour.Status != "in_progress")
            return BadRequest(new { message = "La tournée doit être en cours" });

        var pause = new TourPause
        {
            TourId = id,
            StartTime = DateTime.UtcNow,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Reason = request.Reason ?? "break",
            Notes = request.Notes
        };
        _context.TourPauses.Add(pause);
        await _context.SaveChangesAsync(ct);

        return Ok(new { id = pause.Id, message = "Pause démarrée" });
    }

    // ────────────────── END PAUSE ──────────────────

    [HttpPost("{id}/pauses/{pauseId}/end")]
    public async Task<ActionResult> EndPause(int id, int pauseId, CancellationToken ct = default)
    {
        var companyId = GetCompanyId();
        var scope = await VehicleScopeAsync(ct);

        var query = _context.TourPauses
            .Include(p => p.Tour)
            .Where(p => p.Id == pauseId && p.TourId == id && p.Tour!.CompanyId == companyId);
        if (scope is not null)
            query = query.Where(p => scope.Contains(p.Tour!.VehicleId));

        var pause = await query.FirstOrDefaultAsync(ct);
        if (pause == null) return NotFound();
        if (pause.EndTime.HasValue)
            return BadRequest(new { message = "Cette pause est déjà terminée" });

        pause.EndTime = DateTime.UtcNow;
        pause.DurationMinutes = (int)(pause.EndTime.Value - pause.StartTime).TotalMinutes;

        // Update total pause time
        var tour = pause.Tour!;
        tour.TotalPauseMinutes += pause.DurationMinutes.Value;

        await _context.SaveChangesAsync(ct);
        return Ok(new { message = "Pause terminée", durationMinutes = pause.DurationMinutes });
    }

    // ────────────────── COMPLETE TOUR ──────────────────

    [HttpPost("{id}/complete")]
    public async Task<ActionResult> CompleteTour(int id, [FromBody] CompleteTourRequest? request = null, CancellationToken ct = default)
    {
        var tour = await (await ScopedToursAsync(ct))
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .Include(t => t.Pauses)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tour == null) return NotFound();
        if (tour.Status != "in_progress")
            return BadRequest(new { message = "La tournée doit être en cours pour être complétée" });

        var now = DateTime.UtcNow;
        tour.Status = "completed";
        tour.ActualEndTime = now;

        // Durée de CONDUITE : depuis la première mise en mouvement observée
        // (ActualDepartureTime), sinon depuis le démarrage — cohérent avec la
        // complétion automatique du TourMonitoringService.
        if (tour.ActualStartTime.HasValue)
        {
            var drivingStart = tour.ActualDepartureTime ?? tour.ActualStartTime.Value;
            tour.ActualDurationMinutes = (int)(tour.ActualEndTime.Value - drivingStart).TotalMinutes;
        }

        // Set actual values from request or GPS data
        if (request?.ActualDistanceKm.HasValue == true)
            tour.ActualDistanceKm = request.ActualDistanceKm.Value;
        if (request?.ActualFuelLiters.HasValue == true)
            tour.ActualFuelLiters = request.ActualFuelLiters.Value;

        // Destination atteinte, étapes jamais atteintes « skipped » (et non
        // « completed » : rien ne prouve le passage) — règle commune avec la
        // complétion automatique, cf. TourPlanning.CloseWaypointsOnCompletion.
        // Avant le 18/09/2026 seule la destination était cochée, sans que son
        // WaypointStatus suive.
        TourPlanning.CloseWaypointsOnCompletion(tour, now);

        // End any open pauses
        foreach (var pause in tour.Pauses.Where(p => !p.EndTime.HasValue))
        {
            pause.EndTime = now;
            pause.DurationMinutes = (int)(pause.EndTime.Value - pause.StartTime).TotalMinutes;
        }

        tour.TotalPauseMinutes = tour.Pauses.Sum(p => p.DurationMinutes ?? 0);

        await _context.SaveChangesAsync(ct);

        return Ok(MapToDetailDto(tour));
    }

    // ────────────────── CANCEL TOUR ──────────────────

    [HttpPost("{id}/cancel")]
    public async Task<ActionResult> CancelTour(int id, CancellationToken ct = default)
    {
        var tour = await (await ScopedToursAsync(ct)).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tour == null) return NotFound();
        if (tour.Status == "completed")
            return BadRequest(new { message = "Impossible d'annuler une tournée terminée" });

        tour.Status = "cancelled";
        await _context.SaveChangesAsync(ct);

        // Le chauffeur qui l'avait reçue sur son téléphone doit le savoir tout de suite.
        await PrevenirChauffeurAsync(tour, "tour_cancelled",
            $"Tournée annulée : {tour.Name}",
            "Cette tournée a été annulée par votre gestionnaire.", ct);

        return Ok(new { message = "Tournée annulée" });
    }

    // ────────────────── ENVOI AU CHAUFFEUR ──────────────────

    /// <summary>
    /// Envoie (ou renvoie) la tournée sur le téléphone de son chauffeur : notification
    /// push au compte relié à sa fiche (migration 050/051). La tournée doit être
    /// planifiée ou en cours, avoir un chauffeur, et ce chauffeur un compte actif.
    /// Rend l'issue du push, pour que l'écran dise si le téléphone a été joint.
    /// </summary>
    [HttpPost("{id}/send")]
    public async Task<ActionResult> SendToDriver(int id, CancellationToken ct = default)
    {
        var tour = await (await ScopedToursAsync(ct))
            .Include(t => t.Driver)
            .Include(t => t.Vehicle)
            .Include(t => t.Waypoints)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tour == null) return NotFound();
        if (tour.Status is not ("planned" or "in_progress"))
            return BadRequest(new { message = "Seule une tournée planifiée ou en cours peut être envoyée au chauffeur" });
        if (tour.Driver == null)
            return BadRequest(new { message = "Choisissez d'abord un chauffeur" });

        var compte = await CompteChauffeurAsync(tour.Driver, ct);
        if (compte == null)
            return BadRequest(new
            {
                code = "DRIVER_NO_APP_ACCOUNT",
                message = "Ce chauffeur n'a pas de compte application actif. Créez-le dans Utilisateurs (case « Chauffeur »)."
            });

        var now = DateTime.UtcNow;
        var renvoi = tour.SentAt.HasValue;
        tour.SentAt = now;
        // Le premier envoi reste la borne d'un « Je pars » rejoué hors ligne, renvoi ou non.
        tour.FirstSentAt ??= now;
        tour.SentByUserId = _tenant.UserId;
        // Un renvoi repart de « Envoyée, pas encore ouverte » (relecture du 21/09/2026) :
        // /api/driver-app/tours/{id}/opened n'écrit et ne diffuse TourOpened que si OpenedAt
        // est null, et le téléphone ne l'appelle que dans ce cas. Sans cette remise à zéro,
        // l'ouverture de la tournée MISE À JOUR n'était jamais signalée : l'écran, qui repasse
        // « Envoyée » au renvoi, y restait. (Premier envoi : OpenedAt est déjà null.)
        tour.OpenedAt = null;
        await _context.SaveChangesAsync(ct);

        var etapes = tour.Waypoints.Count;
        var (_, push) = await _notifications.CreateAndSendWithPushAsync(
            tour.CompanyId, compte.Id, "tour_assigned",
            (renvoi ? "Tournée mise à jour : " : "Nouvelle tournée : ") + tour.Name,
            $"Départ {HeureLocale(tour.ScheduledStartTime)} · {etapes} étape{(etapes > 1 ? "s" : "")}"
            + (tour.Vehicle != null ? $" · {tour.Vehicle.Plate ?? tour.Vehicle.Name}" : ""),
            "high", "tour", tour.Id, $"/tournees/{tour.Id}",
            new Dictionary<string, object> { ["tourId"] = tour.Id }, ct);

        _logger.LogInformation("Tournée {TourId} envoyée au chauffeur {DriverId} (compte {UserId}) : push {Push}",
            tour.Id, tour.Driver.Id, compte.Id, push);

        return Ok(new { sentAt = tour.SentAt, push, resent = renvoi });
    }

    /// <summary>Compte application ACTIF relié à la fiche chauffeur, sinon null.</summary>
    private async Task<User?> CompteChauffeurAsync(Driver driver, CancellationToken ct)
    {
        if (driver.UserId is not int userId) return null;
        return await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == driver.CompanyId
                                      && u.Status == "active" && u.AccountType == UserAccountTypes.Driver, ct);
    }

    /// <summary>Notification au chauffeur d'une tournée déjà envoyée (rien si elle ne l'était pas).</summary>
    private async Task PrevenirChauffeurAsync(Tour tour, string type, string titre, string message, CancellationToken ct)
    {
        if (!tour.SentAt.HasValue || tour.DriverId is not int ficheId) return;
        await PrevenirFicheAsync(tour, ficheId, type, titre, message, ct);
    }

    /// <summary>
    /// Notification au compte application relié à la fiche chauffeur <paramref name="ficheId"/>
    /// — qui peut ne plus être le chauffeur de la tournée (réaffectation). Rien si la
    /// fiche n'a pas de compte actif ; un échec n'empêche jamais l'action du gestionnaire.
    /// </summary>
    private async Task PrevenirFicheAsync(Tour tour, int ficheId, string type, string titre, string message, CancellationToken ct)
    {
        try
        {
            var driver = tour.Driver?.Id == ficheId
                ? tour.Driver
                : await _context.Drivers.AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == ficheId && d.CompanyId == tour.CompanyId, ct);
            if (driver == null) return;
            var compte = await CompteChauffeurAsync(driver, ct);
            if (compte == null) return;
            await _notifications.CreateAndSendAsync(tour.CompanyId, compte.Id, type, titre, message,
                "high", "tour", tour.Id, $"/tournees/{tour.Id}",
                new Dictionary<string, object> { ["tourId"] = tour.Id }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tournée {TourId} : notification {Type} au chauffeur non envoyée", tour.Id, type);
        }
    }

    private static string HeureLocale(DateTime utc)
    {
        var tz = GisAPI.Application.Common.QuietHoursPolicy.ResolveTimeZone(null);
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz).ToString("HH:mm");
    }

    // ────────────────── ESTIMATE ROUTE ──────────────────

    [HttpPost("estimate")]
    public async Task<ActionResult> EstimateRoute([FromBody] EstimateRouteRequest request)
    {
        if (request.Waypoints == null || request.Waypoints.Count < 2)
            return BadRequest(new { message = "Au moins 2 points requis" });

        var valhallaPoints = request.Waypoints.Select(w => new ValhallaPoint
        {
            Lat = w.Latitude,
            Lon = w.Longitude
        }).ToList();

        try
        {
            var routeResult = await _valhallaService.GetRouteFromWaypointsAsync(valhallaPoints);
            if (routeResult == null)
                return StatusCode(503, new { message = "Service de routage indisponible" });

            var fuelRate = request.FuelType == "essence" ? 7.0 : 9.0;
            var estimatedFuel = routeResult.TotalDistanceKm * fuelRate / 100.0;
            var totalPause = request.Waypoints.Sum(w => w.PlannedPauseMinutes);

            return Ok(new
            {
                distanceKm = Math.Round(routeResult.TotalDistanceKm, 1),
                durationMinutes = (int)Math.Ceiling(routeResult.TotalTimeSeconds / 60.0),
                durationWithPausesMinutes = (int)Math.Ceiling(routeResult.TotalTimeSeconds / 60.0) + totalPause,
                estimatedFuelLiters = Math.Round(estimatedFuel, 1),
                totalPauseMinutes = totalPause,
                routePolyline = routeResult.EncodedPolyline,
                routePoints = routeResult.DecodedPolyline?.Select(p => new { lat = p[0], lng = p[1] })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Route estimation failed");
            return StatusCode(503, new { message = "Erreur d'estimation de route" });
        }
    }

    // ────────────────── DASHBOARD STATS ──────────────────

    [HttpGet("stats")]
    public async Task<ActionResult> GetTourStats(CancellationToken ct = default)
    {
        var tours = await (await ScopedToursAsync(ct)).AsNoTracking().ToListAsync(ct);

        return Ok(new
        {
            total = tours.Count,
            planned = tours.Count(t => t.Status == "planned"),
            inProgress = tours.Count(t => t.Status == "in_progress"),
            completed = tours.Count(t => t.Status == "completed"),
            cancelled = tours.Count(t => t.Status == "cancelled"),
            totalDistanceKm = Math.Round(tours.Where(t => t.Status == "completed").Sum(t => t.ActualDistanceKm ?? t.EstimatedDistanceKm), 1),
            totalFuelLiters = Math.Round(tours.Where(t => t.Status == "completed").Sum(t => (double)(t.ActualFuelLiters ?? t.EstimatedFuelLiters ?? 0)), 1),
            avgDelayMinutes = tours.Where(t => t.Status == "completed" && t.ActualDurationMinutes.HasValue)
                .Select(t => t.ActualDurationMinutes!.Value - t.EstimatedDurationMinutes)
                .DefaultIfEmpty(0).Average()
        });
    }

    // ────────────────── LIVE TRACKING ──────────────────

    [HttpGet("{id}/tracking")]
    public async Task<ActionResult> GetTourTracking(int id, CancellationToken ct = default)
    {
        var tour = await (await ScopedToursAsync(ct))
            .Include(t => t.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tour == null) return NotFound();

        // Véhicule transféré depuis à une autre société : sa position est à
        // elle, plus à la société de la tournée (TourPlanning.VehicleBelongsToTourCompany).
        VehiclePositionCache? position = null;
        if (tour.Vehicle?.GpsDevice != null && TourPlanning.VehicleBelongsToTourCompany(tour))
        {
            position = await _redisCache.GetPositionAsync(tour.Vehicle.GpsDevice.DeviceUid);
        }

        var nextWaypoint = tour.Waypoints
            .OrderBy(w => w.SequenceOrder)
            .FirstOrDefault(w => !w.IsCompleted);

        double? distanceToNext = null;
        if (position != null && nextWaypoint != null)
        {
            distanceToNext = HaversineDistance(
                position.Latitude, position.Longitude,
                nextWaypoint.Latitude, nextWaypoint.Longitude);
        }

        var completedCount = tour.Waypoints.Count(w => w.IsCompleted);

        // Téléphone du chauffeur (migration 051) : dernier point reçu pour cette tournée.
        var phone = await _context.DriverAppPositions.AsNoTracking()
            .Where(p => p.TourId == tour.Id)
            .OrderByDescending(p => p.RecordedAt)
            .Select(p => new { p.Latitude, p.Longitude, p.AccuracyM, p.SpeedKph, p.Heading, p.RecordedAt, p.BatteryLevel, p.IsMocked })
            .FirstOrDefaultAsync(ct);
        var now = DateTime.UtcNow;
        var deviceAlive = position != null && Services.Tours.TrackingSourceSelector.IsDeviceAlive(
            now, new Services.Tours.TrackingSourceSelector.DeviceState(position.RecordedAt, position.IgnitionOn));
        var phoneAlive = phone != null && Services.Tours.TrackingSourceSelector.IsPhoneAlive(
            now, new Services.Tours.TrackingSourceSelector.PhoneState(phone.RecordedAt, phone.AccuracyM, phone.IsMocked));
        // Position à afficher : celle de la source qui suit (le moniteur la choisit), à
        // défaut la plus fraîche ; le téléphone ne prend le pas que si le boîtier est muet.
        var source = tour.TrackingSource ?? (deviceAlive ? "device" : phoneAlive ? "phone" : "none");
        if (source == "phone" && phone != null && nextWaypoint != null)
            distanceToNext = HaversineDistance(phone.Latitude, phone.Longitude, nextWaypoint.Latitude, nextWaypoint.Longitude);
        var lastSeen = new[] { position?.RecordedAt, phone?.RecordedAt }.Where(d => d.HasValue).Select(d => d!.Value).DefaultIfEmpty().Max();

        return Ok(new
        {
            tourId = tour.Id,
            tourStatus = tour.Status,
            source,
            sourceSince = tour.TrackingSourceSince,
            deviceAvailable = deviceAlive,
            phoneAvailable = phoneAlive,
            positionAgeSeconds = lastSeen == default ? (int?)null : (int)Math.Max(0, (now - lastSeen).TotalSeconds),
            vehicle = position != null ? new
            {
                latitude = position.Latitude,
                longitude = position.Longitude,
                speedKph = position.SpeedKph,
                headingDeg = position.HeadingDeg,
                ignitionOn = position.IgnitionOn,
                recordedAt = position.RecordedAt
            } : null,
            phone = phone != null ? new
            {
                latitude = phone.Latitude,
                longitude = phone.Longitude,
                accuracyM = phone.AccuracyM,
                speedKph = phone.SpeedKph,
                headingDeg = phone.Heading,
                recordedAt = phone.RecordedAt,
                batteryLevel = phone.BatteryLevel,
                isMocked = phone.IsMocked
            } : null,
            progress = new
            {
                completedWaypoints = completedCount,
                totalWaypoints = tour.Waypoints.Count,
                percentComplete = tour.Waypoints.Count > 0
                    ? Math.Round((double)completedCount / tour.Waypoints.Count * 100, 0) : 0,
                nextWaypointName = nextWaypoint?.Name ?? nextWaypoint?.Type,
                distanceToNextMeters = distanceToNext.HasValue ? Math.Round(distanceToNext.Value) : (double?)null
            },
            waypoints = tour.Waypoints.OrderBy(w => w.SequenceOrder).Select(w => new
            {
                w.Id, w.Name, w.Type, w.Latitude, w.Longitude,
                w.IsCompleted, w.ActualArrivalTime, w.ArrivalSource, w.DriverArrivedAt, w.DriverDeclarationDistanceM,
                unconfirmed = Services.Tours.DriverTourRules.IsUnconfirmed(w)
            })
        });
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        => GeoMath.HaversineDistance(lat1, lon1, lat2, lon2);

    // ────────────────── DTO MAPPING ──────────────────

    private static object MapToDto(Tour t) => new
    {
        t.Id,
        t.Name,
        t.Description,
        t.Status,
        vehicleId = t.VehicleId,
        vehicleName = t.Vehicle?.Name,
        vehiclePlate = t.Vehicle?.Plate,
        driverId = t.DriverId,
        driverName = t.Driver != null ? $"{t.Driver.FirstName} {t.Driver.LastName}" : null,
        t.ScheduledStartTime,
        t.ScheduledEndTime,
        t.ActualStartTime,
        t.ActualDepartureTime,
        waitBeforeDepartureMinutes = t.ActualDepartureTime.HasValue && t.ActualStartTime.HasValue
            ? (int?)Math.Max(0, (int)(t.ActualDepartureTime.Value - t.ActualStartTime.Value).TotalMinutes)
            : null,
        t.ActualEndTime,
        t.SentAt,
        t.OpenedAt,
        t.TrackingSource,
        t.EstimatedDistanceKm,
        t.EstimatedDurationMinutes,
        t.EstimatedFuelLiters,
        t.ActualDistanceKm,
        t.ActualDurationMinutes,
        t.ActualFuelLiters,
        t.TotalPauseMinutes,
        t.Recurrence,
        waypointCount = t.Waypoints.Count,
        origin = t.Waypoints.OrderBy(w => w.SequenceOrder).FirstOrDefault()?.Address,
        destination = t.Waypoints.OrderBy(w => w.SequenceOrder).LastOrDefault()?.Address,
        t.CreatedAt
    };

    /// <summary>
    /// Chronologie du trajet reconstruite depuis la trace GPS : départ réel,
    /// arrêts intermédiaires (vitesse ≤ 3 km/h pendant ≥ 3 min), arrivée, et
    /// bilan conduite/arrêts. Best-effort : null si pas de boîtier ou pas de
    /// données. Fenêtre bornée [départ réel → arrivée destination/fin].
    /// </summary>
    private async Task<object?> BuildTimelineAsync(Tour t)
    {
        // Même garde que le suivi : la trace d'un véhicule transféré à une autre
        // société n'est plus lue pour cette tournée.
        if (t.Vehicle?.GpsDeviceId == null || !t.ActualStartTime.HasValue
            || !TourPlanning.VehicleBelongsToTourCompany(t)) return null;

        var startTime = t.ActualStartTime.Value;
        var destination = t.Waypoints.Where(w => w.Type == "destination").OrderBy(w => w.SequenceOrder).LastOrDefault();
        var windowEnd = destination?.ActualArrivalTime ?? t.ActualEndTime ?? DateTime.UtcNow;
        if (windowEnd <= startTime) return null;

        var deviceId = t.Vehicle.GpsDeviceId.Value;
        var trace = await _context.GpsPositions.AsNoTracking()
            .Where(p => p.DeviceId == deviceId && p.RecordedAt >= startTime && p.RecordedAt <= windowEnd)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new { p.RecordedAt, p.SpeedKph, p.Latitude, p.Longitude, p.Address })
            .ToListAsync();
        if (trace.Count < 2) return null;

        // Départ RÉEL : la colonne persistée si le monitoring l'a observée,
        // sinon dérivé de la trace (première vraie mise en mouvement) — c'est
        // ce qui rend la chronologie exacte aussi pour les tournées créées
        // avant le suivi du départ. Repli : l'heure de lancement.
        var firstMoving = trace.FirstOrDefault(p => (p.SpeedKph ?? 0) >= 8);
        var departure = t.ActualDepartureTime ?? firstMoving?.RecordedAt ?? startTime;
        if (departure > windowEnd) departure = startTime;

        // Détection des arrêts APRÈS le départ : groupes consécutifs à
        // ≤ 3 km/h durant ≥ 3 min (l'attente initiale est comptée à part).
        var stops = new List<object>();
        int totalStopMinutes = 0;
        int i = 0;
        while (i < trace.Count && trace[i].RecordedAt < departure) i++;
        while (i < trace.Count)
        {
            if ((trace[i].SpeedKph ?? 0) <= 3)
            {
                int j = i;
                while (j + 1 < trace.Count && (trace[j + 1].SpeedKph ?? 0) <= 3) j++;
                var duration = trace[j].RecordedAt - trace[i].RecordedAt;
                if (duration >= TimeSpan.FromMinutes(3))
                {
                    var address = Enumerable.Range(i, j - i + 1)
                        .Select(k => trace[k].Address).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
                    stops.Add(new
                    {
                        startTime = trace[i].RecordedAt,
                        endTime = trace[j].RecordedAt,
                        durationMinutes = (int)Math.Round(duration.TotalMinutes),
                        latitude = trace[i].Latitude,
                        longitude = trace[i].Longitude,
                        address
                    });
                    totalStopMinutes += (int)Math.Round(duration.TotalMinutes);
                }
                i = j + 1;
            }
            else i++;
        }

        var totalMinutes = (int)Math.Round((windowEnd - departure).TotalMinutes);
        return new
        {
            departureTime = departure,
            startTime = t.ActualStartTime,
            scheduledStartTime = t.ScheduledStartTime,
            // « Parti X min en retard par rapport au moment indiqué » — le chiffre
            // clé du rapport. Signé : négatif = parti en avance.
            departureDelayMinutes = (int)Math.Round((departure - t.ScheduledStartTime).TotalMinutes),
            waitBeforeDepartureMinutes = Math.Max(0, (int)Math.Round((departure - t.ActualStartTime.Value).TotalMinutes)),
            arrivalTime = destination?.ActualArrivalTime,
            arrivalName = destination?.Name ?? destination?.Address,
            estimatedArrivalTime = destination?.EstimatedArrivalTime,
            totalMinutes,
            stoppedMinutes = totalStopMinutes,
            drivingMinutes = Math.Max(0, totalMinutes - totalStopMinutes),
            stops
        };
    }

    private static object MapToDetailDto(Tour t, object? timeline = null) => new
    {
        timeline,
        t.Id,
        t.Name,
        t.Description,
        t.Status,
        vehicleId = t.VehicleId,
        vehicleName = t.Vehicle?.Name,
        vehiclePlate = t.Vehicle?.Plate,
        vehicleFuelType = t.Vehicle?.FuelType,
        driverId = t.DriverId,
        driverName = t.Driver != null ? $"{t.Driver.FirstName} {t.Driver.LastName}" : null,
        t.ScheduledStartTime,
        t.ScheduledEndTime,
        t.ActualStartTime,
        t.ActualDepartureTime,
        waitBeforeDepartureMinutes = t.ActualDepartureTime.HasValue && t.ActualStartTime.HasValue
            ? (int?)Math.Max(0, (int)(t.ActualDepartureTime.Value - t.ActualStartTime.Value).TotalMinutes)
            : null,
        t.ActualEndTime,
        // Envoi au chauffeur et source de suivi (migration 051).
        t.SentAt,
        t.SentByUserId,
        t.OpenedAt,
        t.TrackingSource,
        t.TrackingSourceSince,
        t.EstimatedDistanceKm,
        t.EstimatedDurationMinutes,
        t.EstimatedFuelLiters,
        t.EstimatedRoutePolyline,
        t.ActualDistanceKm,
        t.ActualDurationMinutes,
        t.ActualFuelLiters,
        t.ActualRoutePolyline,
        t.TotalPauseMinutes,
        t.Recurrence,
        t.Notes,
        delayMinutes = t.ActualDurationMinutes.HasValue ? t.ActualDurationMinutes.Value - t.EstimatedDurationMinutes : (int?)null,
        distanceDiffKm = t.ActualDistanceKm.HasValue ? t.ActualDistanceKm.Value - t.EstimatedDistanceKm : (decimal?)null,
        fuelDiffLiters = t.ActualFuelLiters.HasValue && t.EstimatedFuelLiters.HasValue
            ? t.ActualFuelLiters.Value - t.EstimatedFuelLiters.Value : (decimal?)null,
        waypoints = t.Waypoints.OrderBy(w => w.SequenceOrder).Select(w => new
        {
            w.Id,
            w.SequenceOrder,
            w.Name,
            w.Address,
            w.Latitude,
            w.Longitude,
            w.Type,
            w.GeofenceId,
            w.EstimatedLegMinutes,
            w.DeadlineMarginMinutes,
            w.EstimatedArrivalTime,
            w.ActualArrivalTime,
            w.PlannedPauseMinutes,
            w.ActualPauseMinutes,
            w.IsCompleted,
            w.WaypointStatus,
            // Déclarations du chauffeur et source de validation (migration 051).
            w.ArrivalSource,
            w.DriverArrivedAt,
            w.DriverDepartedAt,
            w.ActualDepartureTime,
            w.DriverDeclarationDistanceM,
            unconfirmed = Services.Tours.DriverTourRules.IsUnconfirmed(w),
            arrivalDelay = w.ActualArrivalTime.HasValue && w.EstimatedArrivalTime.HasValue
                ? (int)(w.ActualArrivalTime.Value - w.EstimatedArrivalTime.Value).TotalMinutes : (int?)null,
            deadline = w.EstimatedArrivalTime.HasValue
                ? w.EstimatedArrivalTime.Value.AddMinutes(w.DeadlineMarginMinutes) : (DateTime?)null
        }),
        pauses = t.Pauses?.OrderBy(p => p.StartTime).Select(p => new
        {
            p.Id,
            p.StartTime,
            p.EndTime,
            p.DurationMinutes,
            p.Latitude,
            p.Longitude,
            p.Reason,
            p.Notes
        }),
        t.CreatedAt,
        t.UpdatedAt
    };
}

// ────────────────── DTOs ──────────────────

public class CreateTourRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int VehicleId { get; set; }
    public int? DriverId { get; set; }
    public DateTime ScheduledStartTime { get; set; }
    public string? Recurrence { get; set; }
    public string? Notes { get; set; }
    public List<TourWaypointRequest> Waypoints { get; set; } = new();
}

public class UpdateTourRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? VehicleId { get; set; }

    private int? _driverId;
    /// <summary>
    /// Chauffeur. Propriété absente du JSON = inchangé ; présente avec null =
    /// retirer le chauffeur. Un simple <c>int?</c> ne distingue pas les deux
    /// cas : le sérialiseur n'appelle ce setter que si la propriété est
    /// présente, d'où <see cref="DriverIdSpecified"/>.
    /// </summary>
    public int? DriverId
    {
        get => _driverId;
        set { _driverId = value; DriverIdSpecified = true; }
    }

    [JsonIgnore]
    public bool DriverIdSpecified { get; private set; }

    public DateTime? ScheduledStartTime { get; set; }
    public string? Notes { get; set; }
    public List<TourWaypointRequest>? Waypoints { get; set; }
}

public class TourWaypointRequest
{
    public string? Name { get; set; }
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int PlannedPauseMinutes { get; set; }
    // Optional: link to a geofence zone for automatic entry detection
    public int? GeofenceId { get; set; }
    // Deadline margin in minutes (default 60). Vehicle must arrive within estimated time + margin.
    public int DeadlineMarginMinutes { get; set; } = 60;
}

public class CompleteTourRequest
{
    public decimal? ActualDistanceKm { get; set; }
    public decimal? ActualFuelLiters { get; set; }
}

public class AddPauseRequest
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
}

public class EstimateRouteRequest
{
    public List<TourWaypointRequest> Waypoints { get; set; } = new();
    public string? FuelType { get; set; }
}
