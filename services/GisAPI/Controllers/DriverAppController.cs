using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Hubs;
using GisAPI.Services;
using GisAPI.Services.Tours;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Controllers;

/// <summary>
/// Ce que l'application mobile d'un CHAUFFEUR appelle, et rien d'autre (migration 051,
/// décision de Slim du 21/09/2026 : « le chauffeur est un utilisateur »).
///
/// Périmètre : la fiche <see cref="Driver"/> reliée au compte de l'appelant
/// (drivers.user_id), et les tournées ENVOYÉES à cette fiche. Une tournée d'un autre
/// chauffeur, ou pas encore envoyée, répond 404. Un compte sans fiche reliée répond 403 :
/// un gestionnaire ordinaire n'a rien à faire ici tant qu'aucune fiche ne lui est reliée.
/// Le PermissionMiddleware, lui, empêche un compte chauffeur d'appeler quoi que ce soit
/// d'autre que ces routes.
///
/// Déclarations (« Je pars », « Je suis arrivé », « Je repars ») : règles dans
/// <see cref="DriverTourRules"/> ; positions du téléphone : table driver_app_positions,
/// jamais gps_positions.
/// </summary>
[ApiController]
[Route("api/driver-app")]
[Authorize]
public class DriverAppController : ControllerBase
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly IRedisCacheService _redisCache;
    private readonly INotificationService _notifications;
    private readonly IHubContext<GpsHub> _hub;
    private readonly ILogger<DriverAppController> _logger;

    public const string NoDriverProfileCode = "NO_DRIVER_PROFILE";
    public const string PendingStopsCode = "PENDING_STOPS";
    public const string TooFarCode = "TOO_FAR";

    public DriverAppController(IGisDbContext context, ICurrentTenantService tenant, IRedisCacheService redisCache,
        INotificationService notifications, IHubContext<GpsHub> hub, ILogger<DriverAppController> logger)
    {
        _context = context;
        _tenant = tenant;
        _redisCache = redisCache;
        _notifications = notifications;
        _hub = hub;
        _logger = logger;
    }

    // ────────────────── Périmètre ──────────────────

    private sealed record Profil(Driver Driver, int CompanyId, int UserId);

    /// <summary>La fiche chauffeur de l'appelant, ou null (→ 403).</summary>
    private async Task<Profil?> ProfilAsync(CancellationToken ct)
    {
        var companyId = _tenant.CompanyId ?? 0;
        var userId = _tenant.UserId ?? 0;
        if (companyId <= 0 || userId <= 0) return null;

        var driver = await _context.Drivers.AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == userId && d.CompanyId == companyId, ct);
        return driver == null ? null : new Profil(driver, companyId, userId);
    }

    private ObjectResult SansFiche() => StatusCode(403, new
    {
        code = NoDriverProfileCode,
        message = "Aucune fiche chauffeur n'est reliée à ce compte. Demandez à votre gestionnaire de vous créer un accès chauffeur."
    });

    /// <summary>Tournées envoyées à ce chauffeur : les seules qu'il voit.</summary>
    private IQueryable<Tour> MesTournees(Profil p) =>
        _context.Tours.Where(t => t.CompanyId == p.CompanyId && t.DriverId == p.Driver.Id && t.SentAt != null);

    // ────────────────── Moi ──────────────────

    [HttpGet("me")]
    public async Task<ActionResult> Me(CancellationToken ct = default)
    {
        var p = await ProfilAsync(ct);
        if (p == null) return SansFiche();

        var societe = await _context.Societes.AsNoTracking()
            .Where(s => s.Id == p.CompanyId).Select(s => new { s.Id, s.Name }).FirstOrDefaultAsync(ct);
        var vehicule = p.Driver.AssignedVehicleId is int vid
            ? await _context.Vehicles.AsNoTracking()
                .Where(v => v.Id == vid && v.CompanyId == p.CompanyId)
                .Select(v => new { v.Id, v.Name, v.Plate, hasGps = v.GpsDeviceId != null })
                .FirstOrDefaultAsync(ct)
            : null;

        // Ni CIN, ni permis, ni les autres chauffeurs : l'application n'en a pas besoin.
        return Ok(new
        {
            driverId = p.Driver.Id,
            firstName = p.Driver.FirstName,
            lastName = p.Driver.LastName,
            company = societe,
            assignedVehicle = vehicule
        });
    }

    // ────────────────── Mes tournées ──────────────────

    /// <param name="scope">« active » (planifiées et en cours) ou « history » (terminées / annulées, 7 jours).</param>
    [HttpGet("tours")]
    public async Task<ActionResult> Tours([FromQuery] string scope = "active", CancellationToken ct = default)
    {
        var p = await ProfilAsync(ct);
        if (p == null) return SansFiche();

        var query = MesTournees(p).AsNoTracking()
            .Include(t => t.Vehicle)
            .Include(t => t.Waypoints)
            .AsQueryable();

        if (string.Equals(scope, "history", StringComparison.OrdinalIgnoreCase))
        {
            var depuis = DateTime.UtcNow.AddDays(-7);
            query = query.Where(t => (t.Status == "completed" || t.Status == "cancelled")
                                     && (t.ActualEndTime ?? t.UpdatedAt) >= depuis);
        }
        else
        {
            query = query.Where(t => t.Status == "planned" || t.Status == "in_progress");
        }

        var tours = await query.OrderBy(t => t.ScheduledStartTime).Take(50).ToListAsync(ct);
        return Ok(tours.Select(Resume));
    }

    [HttpGet("tours/{id:int}")]
    public async Task<ActionResult> Tour(int id, CancellationToken ct = default)
    {
        var p = await ProfilAsync(ct);
        if (p == null) return SansFiche();

        var tour = await MesTournees(p).AsNoTracking()
            .Include(t => t.Vehicle)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tour == null) return NotFound();

        return Ok(Detail(tour));
    }

    /// <summary>Première ouverture de la fiche sur le téléphone : le gestionnaire voit « Ouverte 07:12 ».</summary>
    [HttpPost("tours/{id:int}/opened")]
    public async Task<ActionResult> Opened(int id, CancellationToken ct = default)
    {
        var p = await ProfilAsync(ct);
        if (p == null) return SansFiche();

        var tour = await MesTournees(p).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tour == null) return NotFound();

        if (tour.OpenedAt == null)
        {
            tour.OpenedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            await _hub.Clients.Group($"company_{tour.CompanyId}")
                .SendAsync("TourOpened", new { tourId = tour.Id, openedAt = tour.OpenedAt }, ct);
        }
        return NoContent();
    }

    // ────────────────── Déclarations ──────────────────

    /// <summary>« Je pars » à l'origine (démarre la tournée) ou « Je repars » d'une étape.</summary>
    [HttpPost("tours/{id:int}/waypoints/{waypointId:int}/depart")]
    public async Task<ActionResult> Depart(int id, int waypointId, [FromBody] DriverEventRequest? request, CancellationToken ct = default)
    {
        var p = await ProfilAsync(ct);
        if (p == null) return SansFiche();

        var tour = await MesTournees(p)
            .Include(t => t.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tour == null) return NotFound();
        var wp = tour.Waypoints.FirstOrDefault(w => w.Id == waypointId);
        if (wp == null) return NotFound(new { message = "Étape introuvable" });

        var now = DateTime.UtcNow;
        var origine = TourPlanning.OriginOf(tour);
        var estOrigine = origine != null && origine.Id == wp.Id;

        if (tour.Status == "planned")
        {
            if (!estOrigine)
                return BadRequest(new { message = "Touchez d'abord « Je pars » au point de départ." });

            // Décision D6 : « En cours » veut dire « parti ». Même règle que le bouton
            // Démarrer du gestionnaire (décalage des estimations d'un départ en retard).
            var declaredAt = DriverTourRules.BoundDeclaredTime(request?.ClientTime, now, tour.ScheduledStartTime);
            TourPlanning.Start(tour, declaredAt);
            wp.ArrivalSource = DriverTourRules.SourceDriver;
            DriverTourRules.DeclareDeparture(wp, declaredAt);
            tour.TrackingSource = TrackingSourceSelector.None;
            tour.TrackingSourceSince = declaredAt;
            await _context.SaveChangesAsync(ct);

            await _hub.Clients.Group($"company_{tour.CompanyId}").SendAsync("TourStatusChanged", new
            {
                tourId = tour.Id, status = "in_progress", tourName = tour.Name,
                message = $"Le chauffeur est parti : {tour.Name}", timestamp = declaredAt
            }, ct);
            await NotifierGestionnairesAsync(tour, "tour_departed",
                $"Départ : {tour.Name}",
                $"{p.Driver.FirstName} {p.Driver.LastName} a signalé son départ ({HeureLocale(declaredAt)}).",
                "normal", ct);
        }
        else if (tour.Status == "in_progress")
        {
            var declaredAt = DriverTourRules.BoundDeclaredTime(request?.ClientTime, now, tour.ActualStartTime ?? tour.ScheduledStartTime);
            DriverTourRules.DeclareDeparture(wp, declaredAt);
            await _context.SaveChangesAsync(ct);
        }
        else
        {
            return BadRequest(new { message = "Cette tournée n'est plus en cours." });
        }

        return Ok(new
        {
            tourStatus = tour.Status,
            actualStartTime = tour.ActualStartTime,
            tracking = tour.Status == "in_progress",
            mode = await PhoneModeAsync(tour, ct),
            waypoint = Etape(wp)
        });
    }

    /// <summary>« Je suis arrivé » à une étape (la destination clôt la tournée).</summary>
    [HttpPost("tours/{id:int}/waypoints/{waypointId:int}/arrive")]
    public async Task<ActionResult> Arrive(int id, int waypointId, [FromBody] DriverEventRequest? request, CancellationToken ct = default)
    {
        var p = await ProfilAsync(ct);
        if (p == null) return SansFiche();

        var tour = await MesTournees(p)
            .Include(t => t.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tour == null) return NotFound();
        var wp = tour.Waypoints.FirstOrDefault(w => w.Id == waypointId);
        if (wp == null) return NotFound(new { message = "Étape introuvable" });
        if (tour.Status != "in_progress")
            return BadRequest(new { message = tour.Status == "planned"
                ? "Touchez d'abord « Je pars » au point de départ."
                : "Cette tournée n'est plus en cours." });

        var now = DateTime.UtcNow;
        var declaredAt = DriverTourRules.BoundDeclaredTime(request?.ClientTime, now, tour.ActualStartTime ?? tour.ScheduledStartTime);

        // Où est-il vraiment ? Boîtier (position fraîche) et téléphone (position jointe à
        // la déclaration, précision acceptable). La déclaration n'est refusée que si les
        // DEUX disent « loin » ; sinon elle passe, avec sa distance pour le gestionnaire.
        var boitier = await PositionBoitierAsync(tour, now);
        int? distBoitier = boitier != null ? DriverTourRules.DistanceToStop(boitier.Latitude, boitier.Longitude, wp) : null;
        var telephoneFiable = request?.Latitude != null && request.Longitude != null
                              && (request.AccuracyM ?? 0) <= TrackingSourceSelector.PhoneMaxAccuracyM;
        int? distTelephone = telephoneFiable ? DriverTourRules.DistanceToStop(request!.Latitude, request.Longitude, wp) : null;

        if (DriverTourRules.IsArrivalDeclarationRefused(distBoitier, distTelephone))
            return Conflict(new
            {
                code = TooFarCode,
                distanceM = Math.Min(distBoitier!.Value, distTelephone!.Value),
                message = "Le véhicule et votre téléphone sont tous deux loin de cette étape. Rapprochez-vous avant de signaler l'arrivée."
            });

        var estDestination = wp.Type == "destination";
        if (estDestination && TourPlanning.HasPendingStopBefore(tour.Waypoints, wp) && request?.ConfirmSkipPending != true)
        {
            var restantes = tour.Waypoints
                .Where(w => w.SequenceOrder < wp.SequenceOrder && !w.IsCompleted && w.WaypointStatus == "pending")
                .OrderBy(w => w.SequenceOrder)
                .Select(w => new { w.Id, name = w.Name ?? w.Address ?? "Arrêt" });
            return Conflict(new
            {
                code = PendingStopsCode,
                pending = restantes,
                message = "Des étapes n'ont pas été signalées. Confirmer l'arrivée les marquera comme non visitées."
            });
        }

        var changed = DriverTourRules.DeclareArrival(wp, declaredAt, distBoitier ?? distTelephone);
        string? warning = null;
        var distance = distBoitier ?? distTelephone;
        if (distance > DriverTourRules.DeclarationWarnBeyondM)
            warning = $"Arrivée déclarée à {distance / 1000.0:0.0} km de l'étape.";

        if (changed)
        {
            await _hub.Clients.Group($"company_{tour.CompanyId}").SendAsync("TourWaypointCompleted", new
            {
                tourId = tour.Id, waypointId = wp.Id, waypointName = wp.Name ?? wp.Type, waypointType = wp.Type,
                waypointStatus = "completed", actualArrivalTime = wp.ActualArrivalTime,
                arrivalSource = wp.ArrivalSource, declarationDistanceM = wp.DriverDeclarationDistanceM, timestamp = now
            }, ct);

            var libelle = wp.Name ?? wp.Address ?? (estDestination ? "Destination" : "Arrêt");
            if (estDestination)
            {
                tour.Status = "completed";
                tour.ActualEndTime = declaredAt;
                if (tour.ActualStartTime.HasValue)
                    tour.ActualDurationMinutes = (int)(declaredAt - (tour.ActualDepartureTime ?? tour.ActualStartTime.Value)).TotalMinutes;
                TourPlanning.CloseWaypointsOnCompletion(tour, declaredAt);
                tour.TrackingSource = TrackingSourceSelector.None;
                tour.TrackingSourceSince = declaredAt;
                await _context.SaveChangesAsync(ct);

                await _hub.Clients.Group($"company_{tour.CompanyId}").SendAsync("TourStatusChanged", new
                {
                    tourId = tour.Id, status = "completed", tourName = tour.Name,
                    message = $"Tournée terminée par le chauffeur : {tour.Name}", timestamp = declaredAt
                }, ct);
                await NotifierGestionnairesAsync(tour, "tour_completed",
                    $"Tournée terminée : {tour.Name}",
                    $"{p.Driver.FirstName} {p.Driver.LastName} a signalé son arrivée à destination ({HeureLocale(declaredAt)})."
                    + (warning != null ? " " + warning : ""),
                    "normal", ct);
            }
            else
            {
                await _context.SaveChangesAsync(ct);
                await NotifierGestionnairesAsync(tour, "tour_waypoint",
                    $"Étape signalée : {libelle}",
                    $"Tournée '{tour.Name}' — le chauffeur a signalé son arrivée à '{libelle}' ({HeureLocale(declaredAt)})."
                    + (warning != null ? " " + warning : ""),
                    "normal", ct);
            }
        }
        else
        {
            await _context.SaveChangesAsync(ct);   // déclaration notée sur une étape déjà validée
        }

        return Ok(new
        {
            tourStatus = tour.Status,
            tracking = tour.Status == "in_progress",
            mode = await PhoneModeAsync(tour, ct),
            waypoint = Etape(wp),
            warning
        });
    }

    // ────────────────── Positions du téléphone ──────────────────

    [HttpPost("positions")]
    public async Task<ActionResult> Positions([FromBody] PhonePositionsRequest request, CancellationToken ct = default)
    {
        var p = await ProfilAsync(ct);
        if (p == null) return SansFiche();

        // Au plus une tournée en cours par chauffeur : la plus ancienne démarrée si
        // plusieurs (données incohérentes), pour ne jamais perdre la trace.
        var tour = await MesTournees(p)
            .Include(t => t.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Where(t => t.Status == "in_progress")
            .OrderBy(t => t.ActualStartTime)
            .FirstOrDefaultAsync(ct);
        if (tour == null)
            return Ok(new { tracking = false, mode = TrackingSourceSelector.PhoneMode(false), activeTourId = (int?)null, accepted = 0 });

        var now = DateTime.UtcNow;
        var start = tour.ActualStartTime ?? tour.ScheduledStartTime;

        // Horloge du téléphone : corrigée du décalage mesuré sur « sentAt », seulement
        // s'il est net (> 30 s) — un petit écart est du réseau, pas une horloge fausse.
        var skew = TimeSpan.Zero;
        if (request.SentAt.HasValue)
        {
            var mesure = now - DateTime.SpecifyKind(request.SentAt.Value, DateTimeKind.Utc);
            if (Math.Abs(mesure.TotalSeconds) > 30) skew = mesure;
        }

        var acceptes = 0;
        foreach (var pt in (request.Points ?? new List<PhonePoint>()).Take(DriverTourRules.MaxPositionsPerBatch))
        {
            var recordedAt = DateTime.SpecifyKind(pt.RecordedAt, DateTimeKind.Utc) + skew;
            if (!DriverTourRules.IsPositionInWindow(recordedAt, start, now)) continue;
            if (pt.Latitude is < -90 or > 90 || pt.Longitude is < -180 or > 180) continue;

            _context.DriverAppPositions.Add(new DriverAppPosition
            {
                CompanyId = tour.CompanyId,
                UserId = p.UserId,
                DriverId = p.Driver.Id,
                TourId = tour.Id,
                RecordedAt = recordedAt,
                ReceivedAt = now,
                Latitude = pt.Latitude,
                Longitude = pt.Longitude,
                AccuracyM = pt.AccuracyM,
                SpeedKph = pt.SpeedKph,
                Heading = pt.Heading,
                IsMocked = pt.IsMocked,
                BatteryLevel = request.BatteryLevel
            });
            acceptes++;
        }
        if (acceptes > 0) await _context.SaveChangesAsync(ct);

        // Le suivi s'arrête de lui-même au plus tard 12 h après le départ.
        var encore = now - start <= DriverTourRules.MaxTrackingDuration;
        return Ok(new { tracking = encore, mode = await PhoneModeAsync(tour, ct), activeTourId = tour.Id, accepted = acceptes });
    }

    // ────────────────── Aides ──────────────────

    /// <summary>Position du boîtier si elle est fraîche (règle TrackingSourceSelector), sinon null.</summary>
    private async Task<VehiclePositionCache?> PositionBoitierAsync(Tour tour, DateTime now)
    {
        var uid = tour.Vehicle?.GpsDevice?.DeviceUid;
        if (string.IsNullOrEmpty(uid) || !TourPlanning.VehicleBelongsToTourCompany(tour)) return null;
        var cached = await _redisCache.GetPositionAsync(uid);
        if (cached == null) return null;
        var etat = new TrackingSourceSelector.DeviceState(cached.RecordedAt, cached.IgnitionOn);
        return TrackingSourceSelector.IsDeviceAlive(now, etat) ? cached : null;
    }

    private async Task<string> PhoneModeAsync(Tour tour, CancellationToken ct)
    {
        var boitier = await PositionBoitierAsync(tour, DateTime.UtcNow);
        return TrackingSourceSelector.PhoneMode(boitier != null);
    }

    /// <summary>
    /// Gestionnaires à prévenir : l'audience habituelle du véhicule (administrateurs et
    /// utilisateurs affectés) PLUS la personne qui a envoyé la tournée, qui n'est pas
    /// forcément l'une ni l'autre. Jamais le chauffeur lui-même.
    /// </summary>
    private async Task NotifierGestionnairesAsync(Tour tour, string type, string titre, string message, string priorite, CancellationToken ct)
    {
        try
        {
            var ids = await NotificationAudience.ForVehicleAsync(_context, tour.CompanyId, tour.VehicleId, ct);
            if (tour.SentByUserId is int envoyeur && !ids.Contains(envoyeur)) ids.Add(envoyeur);
            foreach (var userId in ids.Distinct())
            {
                await _notifications.CreateAndSendAsync(tour.CompanyId, userId, type, titre, message,
                    priorite, "tour", tour.Id, $"/tournees/{tour.Id}",
                    new Dictionary<string, object> { ["tourId"] = tour.Id }, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tournée {TourId} : notification {Type} non envoyée", tour.Id, type);
        }
    }

    private static string HeureLocale(DateTime utc)
    {
        // Tunisie (UTC+1, sans heure d'été) : le fuseau des clients de TN. Le réglage par
        // société existe (Societe.Settings.Timezone) ; il sera lu ici quand un client hors
        // de ce fuseau utilisera les tournées.
        var tz = GisAPI.Application.Common.QuietHoursPolicy.ResolveTimeZone(null);
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz).ToString("HH:mm");
    }

    private static object Resume(Tour t) => new
    {
        t.Id,
        t.Name,
        t.Status,
        t.ScheduledStartTime,
        t.ScheduledEndTime,
        t.ActualStartTime,
        t.ActualEndTime,
        t.SentAt,
        t.OpenedAt,
        vehicleName = t.Vehicle?.Name,
        vehiclePlate = t.Vehicle?.Plate,
        vehicleHasGps = t.Vehicle?.GpsDeviceId != null,
        waypointCount = t.Waypoints.Count,
        completedCount = t.Waypoints.Count(w => w.IsCompleted),
        nextWaypointName = t.Waypoints.OrderBy(w => w.SequenceOrder).FirstOrDefault(w => !w.IsCompleted)?.Name,
        origin = t.Waypoints.OrderBy(w => w.SequenceOrder).FirstOrDefault()?.Address,
        destination = t.Waypoints.OrderBy(w => w.SequenceOrder).LastOrDefault()?.Address,
        t.EstimatedDistanceKm,
        t.EstimatedDurationMinutes
    };

    private static object Detail(Tour t) => new
    {
        t.Id,
        t.Name,
        t.Description,
        t.Status,
        t.ScheduledStartTime,
        t.ScheduledEndTime,
        t.ActualStartTime,
        t.ActualEndTime,
        t.SentAt,
        t.OpenedAt,
        t.Notes,
        vehicleName = t.Vehicle?.Name,
        vehiclePlate = t.Vehicle?.Plate,
        vehicleHasGps = t.Vehicle?.GpsDeviceId != null,
        t.EstimatedDistanceKm,
        t.EstimatedDurationMinutes,
        t.EstimatedRoutePolyline,
        tracking = t.Status == "in_progress",
        waypoints = t.Waypoints.OrderBy(w => w.SequenceOrder).Select(Etape)
    };

    private static object Etape(TourWaypoint w) => new
    {
        w.Id,
        w.SequenceOrder,
        w.Name,
        w.Address,
        w.Latitude,
        w.Longitude,
        w.Type,
        w.EstimatedArrivalTime,
        w.PlannedPauseMinutes,
        w.IsCompleted,
        w.WaypointStatus,
        w.ActualArrivalTime,
        w.ArrivalSource,
        w.DriverArrivedAt,
        w.DriverDepartedAt
    };
}

/// <summary>Déclaration du chauffeur : heure du téléphone et position au moment du geste (facultatives).</summary>
public class DriverEventRequest
{
    public DateTime? ClientTime { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AccuracyM { get; set; }
    /// <summary>Arrivée à destination : oui, marquer « non visitées » les étapes restantes.</summary>
    public bool? ConfirmSkipPending { get; set; }
}

public class PhonePoint
{
    public DateTime RecordedAt { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public float? AccuracyM { get; set; }
    public float? SpeedKph { get; set; }
    public float? Heading { get; set; }
    public bool IsMocked { get; set; }
}

public class PhonePositionsRequest
{
    public List<PhonePoint>? Points { get; set; }
    /// <summary>Heure du téléphone à l'envoi : sert à corriger son horloge.</summary>
    public DateTime? SentAt { get; set; }
    public short? BatteryLevel { get; set; }
}
