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
            // Borne basse = l'envoi de la tournée, pas l'heure prévue : un départ anticipé
            // hors ligne garde son heure au rejeu (DriverTourRules.DepartureReference).
            var declaredAt = DriverTourRules.ReadDeclarationTime(request?.ClientTime, request?.SentAt, now,
                DriverTourRules.DepartureReference(tour)).DeclaredAt;
            TourPlanning.Start(tour, declaredAt);
            wp.ArrivalSource = DriverTourRules.SourceDriver;
            DriverTourRules.DeclareDeparture(wp, declaredAt);
            tour.TrackingSource = TrackingSourceSelector.None;
            // Début d'une éventuelle coupure de suivi = l'heure où le SERVEUR apprend le
            // départ, pas l'heure déclarée (relecture du 21/09/2026, R1c). Le moniteur compte
            // la coupure depuis cette valeur : un « Je pars » rejoué 30 min après le geste
            // déclenchait aussitôt « Suivi interrompu depuis 30 min », alors que les points
            // du téléphone, envoyés par lots derrière la déclaration, arrivaient.
            tour.TrackingSourceSince = now;
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
            var declaredAt = DriverTourRules.ReadDeclarationTime(request?.ClientTime, request?.SentAt, now,
                tour.ActualStartTime ?? tour.ScheduledStartTime).DeclaredAt;
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
        var heure = DriverTourRules.ReadDeclarationTime(request?.ClientTime, request?.SentAt, now,
            tour.ActualStartTime ?? tour.ScheduledStartTime);
        var declaredAt = heure.DeclaredAt;

        // Où est-il vraiment ? Boîtier (position fraîche) et téléphone (position jointe à
        // la déclaration, précision acceptable). La déclaration n'est refusée que si les
        // DEUX disent « loin » ; sinon elle passe, avec sa distance pour le gestionnaire.
        // Déclaration REJOUÉE par la file hors ligne : le boîtier est lu à l'heure du geste,
        // pas maintenant — le camion est peut-être déjà 15 km plus loin (relecture du
        // 21/09/2026, F18). Sans trame à ± 3 min, seul le téléphone, capturé au moment du
        // geste, témoigne ; la même règle vaut pour le refus TOO_FAR. L'heure du geste est
        // celle du téléphone corrigée de son horloge, même quand l'heure retenue a été
        // bornée à « maintenant » (R10c) : c'est là qu'était le camion.
        var boitier = heure.Replayed
            ? await PositionBoitierAuMomentAsync(tour, heure.GestureAt, ct)
            : await PositionBoitierAsync(tour, now);
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
        }

        var libelle = wp.Name ?? wp.Address ?? (estDestination ? "Destination" : "Arrêt");
        if (estDestination)
        {
            // Arrivée déclarée à destination = tournée terminée, même si l'étape était déjà
            // validée (gestionnaire, détection) : sinon ni le chauffeur ni le moniteur — qui
            // saute une étape déjà complétée — ne la clôturaient plus, et le téléphone
            // restait en suivi jusqu'à 12 h (relecture du 21/09/2026, F19).
            tour.Status = "completed";
            tour.ActualEndTime = declaredAt;
            if (tour.ActualStartTime.HasValue)
                tour.ActualDurationMinutes = (int)(declaredAt - (tour.ActualDepartureTime ?? tour.ActualStartTime.Value)).TotalMinutes;
            TourPlanning.CloseWaypointsOnCompletion(tour, declaredAt);
            tour.TrackingSource = TrackingSourceSelector.None;
            tour.TrackingSourceSince = declaredAt;

            // Mêmes chiffres réels (distance, carburant) que la clôture par le moniteur (F21).
            try
            {
                await TourMetrics.CalculateActualMetricsAsync(tour, _context, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tournée {TourId} : métriques réelles non calculées à la clôture par le chauffeur", tour.Id);
            }
            await _context.SaveChangesAsync(ct);

            await _hub.Clients.Group($"company_{tour.CompanyId}").SendAsync("TourStatusChanged", new
            {
                tourId = tour.Id, status = "completed", tourName = tour.Name,
                message = $"Tournée terminée par le chauffeur : {tour.Name}",
                actualDistanceKm = tour.ActualDistanceKm, timestamp = declaredAt
            }, ct);
            await NotifierGestionnairesAsync(tour, "tour_completed",
                $"Tournée terminée : {tour.Name}",
                $"{p.Driver.FirstName} {p.Driver.LastName} a signalé son arrivée à destination ({HeureLocale(declaredAt)})."
                + (warning != null ? " " + warning : ""),
                "normal", ct);
        }
        else if (changed)
        {
            await _context.SaveChangesAsync(ct);
            await NotifierGestionnairesAsync(tour, "tour_waypoint",
                $"Étape signalée : {libelle}",
                $"Tournée '{tour.Name}' — le chauffeur a signalé son arrivée à '{libelle}' ({HeureLocale(declaredAt)})."
                + (warning != null ? " " + warning : ""),
                "normal", ct);
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

        // Tournée en cours dont la fenêtre de suivi contient maintenant (départ il y a moins
        // de 12 h). Relecture du 21/09/2026 (F17) : on prenait la plus ANCIENNE en cours ;
        // une tournée de la veille jamais clôturée captait alors tous les points, hors de sa
        // fenêtre (acceptés : 0), et répondait tracking:false — le téléphone arrêtait le
        // suivi de la tournée du jour.
        // D'abord celle que le téléphone SUIT (activeTourId, qu'il connaît) si elle est
        // toujours à lui, en cours et dans sa fenêtre (R11c) : prendre d'office la plus
        // récemment partie laissait un « Démarrer » du gestionnaire sur la tournée suivante
        // détourner la trace de la tournée en cours et faire basculer le suivi du téléphone.
        // À défaut (application plus ancienne, tournée close entre-temps), la plus récente.
        var now = DateTime.UtcNow;
        var plancher = now - DriverTourRules.MaxTrackingDuration;
        var enCours = MesTournees(p)
            .Include(t => t.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Where(t => t.Status == "in_progress" && (t.ActualStartTime ?? t.ScheduledStartTime) >= plancher);
        Tour? tour = null;
        if (request.ActiveTourId is int suivie)
            tour = await enCours.FirstOrDefaultAsync(t => t.Id == suivie, ct);
        tour ??= await enCours
            .OrderByDescending(t => t.ActualStartTime ?? t.ScheduledStartTime)
            .FirstOrDefaultAsync(ct);
        if (tour == null)
            return Ok(new { tracking = false, mode = TrackingSourceSelector.PhoneMode(false), activeTourId = (int?)null, accepted = 0 });

        var start = tour.ActualStartTime ?? tour.ScheduledStartTime;

        // Horloge du téléphone : corrigée du décalage mesuré sur « sentAt », seulement
        // s'il est net (> 30 s) — un petit écart est du réseau, pas une horloge fausse.
        var skew = DriverTourRules.ClockSkew(request.SentAt, now);

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

    /// <summary>
    /// Position du boîtier à l'heure d'une déclaration rejouée : la trame valide la plus
    /// proche de <paramref name="at"/> à ± 3 min (deux recherches par index, avant et
    /// après), sinon null — le téléphone reste alors seul témoin.
    /// </summary>
    private async Task<VehiclePositionCache?> PositionBoitierAuMomentAsync(Tour tour, DateTime at, CancellationToken ct)
    {
        var deviceId = tour.Vehicle?.GpsDeviceId;
        if (deviceId == null || !TourPlanning.VehicleBelongsToTourCompany(tour)) return null;

        var debut = at - DriverTourRules.ReplayedDeviceFrameWindow;
        var fin = at + DriverTourRules.ReplayedDeviceFrameWindow;
        var avant = await _context.GpsPositions.AsNoTracking()
            .Where(g => g.DeviceId == deviceId.Value && g.RecordedAt >= debut && g.RecordedAt <= at && g.IsValid)
            .OrderByDescending(g => g.RecordedAt)
            .Select(g => new { g.Latitude, g.Longitude, g.RecordedAt })
            .FirstOrDefaultAsync(ct);
        var apres = await _context.GpsPositions.AsNoTracking()
            .Where(g => g.DeviceId == deviceId.Value && g.RecordedAt > at && g.RecordedAt <= fin && g.IsValid)
            .OrderBy(g => g.RecordedAt)
            .Select(g => new { g.Latitude, g.Longitude, g.RecordedAt })
            .FirstOrDefaultAsync(ct);

        var trame = avant == null ? apres
            : apres == null ? avant
            : (at - avant.RecordedAt) <= (apres.RecordedAt - at) ? avant : apres;
        return trame == null ? null
            : new VehiclePositionCache { Latitude = trame.Latitude, Longitude = trame.Longitude, RecordedAt = trame.RecordedAt };
    }

    private async Task<string> PhoneModeAsync(Tour tour, CancellationToken ct)
    {
        var boitier = await PositionBoitierAsync(tour, DateTime.UtcNow);
        return TrackingSourceSelector.PhoneMode(boitier != null);
    }

    /// <summary>
    /// Gestionnaires à prévenir : l'audience habituelle du véhicule (administrateurs et
    /// utilisateurs affectés, comptes actifs). Jamais le chauffeur lui-même.
    ///
    /// La personne qui a envoyé la tournée n'est plus ajoutée à part (relecture du
    /// 21/09/2026, F9/F25) : pour l'envoyer, elle devait voir le véhicule, donc elle figure
    /// déjà dans cette audience. L'ajout ne jouait que lorsqu'elle avait perdu ce droit ou
    /// été désactivée — exactement ce que le cloisonnement du 16/09 veut fermer.
    /// </summary>
    private async Task NotifierGestionnairesAsync(Tour tour, string type, string titre, string message, string priorite, CancellationToken ct)
    {
        try
        {
            var ids = await NotificationAudience.ForVehicleAsync(_context, tour.CompanyId, tour.VehicleId, ct);
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
    /// <summary>Heure du geste, lue sur l'horloge du téléphone.</summary>
    public DateTime? ClientTime { get; set; }
    /// <summary>Heure de l'ENVOI de cette requête, lue sur la même horloge, reposée à chaque
    /// envoi (rejeu de la file hors ligne compris) : corrige l'horloge et dit si la
    /// déclaration est rejouée (DriverTourRules.ReadDeclarationTime). Absente avant 1.2.x.</summary>
    public DateTime? SentAt { get; set; }
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
    /// <summary>Tournée que le téléphone suit (son état de suivi) : retenue si elle est au
    /// chauffeur, en cours et dans sa fenêtre de 12 h, sinon la plus récente en cours.</summary>
    public int? ActiveTourId { get; set; }
}
