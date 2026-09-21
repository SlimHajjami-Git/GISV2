using System.Collections.Concurrent;
using System.Text.Json;
using GisAPI.Hubs;
using GisAPI.Domain.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Services.Tours;

namespace GisAPI.Services;

/// <summary>
/// Background service that monitors active tours and automatically:
/// - Starts tours when their scheduled time passes (pure time-based)
/// - Validates waypoints from the vehicle's GPS TRACE (not just the current
///   position — a 30s polling cycle would miss a drive-through at 60 km/h)
/// - Completes tours when the destination is reached AND every earlier stop
///   is resolved (prevents instant completion of round trips where the
///   destination equals the origin)
/// - Calculates actual distance/duration from GPS data
/// - Sends real-time notifications via SignalR
/// </summary>
public class TourMonitoringService : BackgroundService
{
    private readonly ILogger<TourMonitoringService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IRedisCacheService _redisCache;

    // Configuration constants
    private const int CHECK_INTERVAL_SECONDS = 30;
    private const double WAYPOINT_RADIUS_METERS = 300;
    private const double DEPARTURE_RADIUS_METERS = 400;
    private const double DESTINATION_RADIUS_METERS = 300;
    private const int SCHEDULE_WINDOW_MINUTES = 60;
    /// <summary>Planned tours whose scheduled time passed up to this long ago
    /// still auto-start (service restarts, tours created for earlier today…).
    /// Beyond that they stay "planned" for the operator to decide.</summary>
    private const int AUTO_START_LOOKBACK_HOURS = 12;
    /// <summary>A round trip's destination shares the origin's coordinates —
    /// without a grace period the tour would complete the moment it starts.</summary>
    private const int DESTINATION_GRACE_MINUTES = 3;
    private const double MIN_SPEED_FOR_DEPARTURE_KPH = 5;
    private const double DEVIATION_THRESHOLD_METERS = 2000;
    /// <summary>Radius arrivals must look like a STOP, not a drive-by: either a
    /// sample slower than this inside the radius…</summary>
    private const double ARRIVAL_MAX_SPEED_KPH = 15;
    /// <summary>…or two in-radius samples at least this far apart (a drive-through
    /// at road speed crosses a 300 m zone in ≈35 s).</summary>
    private const int ARRIVAL_DWELL_SECONDS = 45;
    private static readonly TimeSpan DeviationAlertCooldown = TimeSpan.FromMinutes(10);

    /// <summary>Last gps_positions.Id already analysed per tour — each cycle only
    /// scans the NEW slice of the trace. Keyed on the INSERTION id, not the
    /// device-reported recorded_at: devices demonstrably re-send buffered frames
    /// that commit late with older timestamps (see migration
    /// 028_unique_device_recorded_at), and a time-based cursor would skip them
    /// forever. After a restart the first cycle re-scans from the tour start,
    /// which also recovers arrivals missed while the service was down.</summary>
    private static readonly ConcurrentDictionary<int, long> _traceCursor = new();
    private static readonly ConcurrentDictionary<int, DateTime> _lastDeviationAlertAt = new();
    /// <summary>When the vehicle first left the origin (&gt; DEPARTURE_RADIUS_METERS).
    /// The destination may only validate AFTER an actual departure — auto-start is
    /// pure time-based, so without this a round trip (destination = origin) would
    /// complete from the parked-at-depot frames as soon as the grace expires.
    /// In-memory only: rebuilt from the catch-up trace scan after a restart.</summary>
    private static readonly ConcurrentDictionary<int, DateTime> _departedAt = new();
    /// <summary>Destination arrival observed while the destination was still gated
    /// (earlier stop pending / grace) — stashed so the samples consumed by the
    /// advancing cursor are not lost, and applied on the first unblocked cycle.
    /// Without it a vehicle that reaches the destination early and cuts ignition
    /// would leave the tour "in_progress" forever.</summary>
    private static readonly ConcurrentDictionary<int, TracePoint> _pendingDestArrival = new();

    // ── Tournée envoyée au chauffeur (migration 051) ──
    /// <summary>Curseur sur driver_app_positions.id : la trace du TÉLÉPHONE, seconde source.</summary>
    private static readonly ConcurrentDictionary<int, long> _phoneCursor = new();
    /// <summary>Compteur d'hystérésis de TrackingSourceSelector, par tournée.</summary>
    private static readonly ConcurrentDictionary<int, int> _recoveryCount = new();
    /// <summary>Depuis quand aucune source ne suit la tournée (alerte après 10 min, une fois par coupure).</summary>
    private static readonly ConcurrentDictionary<int, DateTime> _lostSince = new();
    private static readonly ConcurrentDictionary<int, byte> _lostAlerted = new();
    /// <summary>Tournées « non démarrée » déjà signalées (une alerte par tournée).</summary>
    private static readonly ConcurrentDictionary<int, byte> _notStartedAlerted = new();

    public TourMonitoringService(
        ILogger<TourMonitoringService> logger,
        IServiceProvider serviceProvider,
        IRedisCacheService redisCache)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _redisCache = redisCache;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Tour Monitoring Service starting...");
        await Task.Delay(5000, stoppingToken); // Wait for other services to initialize

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorTours(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Tour Monitoring cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(CHECK_INTERVAL_SECONDS), stoppingToken);
        }
    }

    private async Task MonitorTours(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GpsHub>>();
        var notifService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var now = DateTime.UtcNow;
        // Lookback widened: a tour scheduled at 08:00 must still start when the
        // service only sees it at 09:30 (restart, deploy…). The old ±60 min
        // window silently left such tours "planned" forever.
        var windowStart = now.AddHours(-AUTO_START_LOOKBACK_HOURS);
        var windowEnd = now.AddMinutes(SCHEDULE_WINDOW_MINUTES);

        // 1. Check PLANNED tours within time window for auto-start
        var plannedLoaded = await context.Tours
            .IgnoreQueryFilters()
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .Include(t => t.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Where(t => t.Status == "planned"
                && t.ScheduledStartTime >= windowStart
                && t.ScheduledStartTime <= windowEnd)
            .ToListAsync(ct);
        var plannedTours = KeepToursOfOwnVehicle(plannedLoaded);

        if (plannedTours.Count > 0)
        {
            _logger.LogInformation(
                "Tour Monitor: {Count} planned tour(s) in window [{Start:HH:mm} - {End:HH:mm}] UTC (now={Now:HH:mm:ss} UTC)",
                plannedTours.Count, windowStart, windowEnd, now);
        }

        foreach (var tour in plannedTours)
        {
            try
            {
                // Tournée ENVOYÉE à un chauffeur : elle démarre à son « Je pars », pas à
                // l'heure (décision D6, « en cours » veut dire « parti ») ; passé 15 min, le
                // gestionnaire est prévenu qu'elle n'a pas démarré.
                if (DriverTourRules.StartsOnDriverDeparture(tour))
                {
                    await CheckNotStarted(tour, context, notifService, ct);
                    continue;
                }
                await CheckAutoStart(tour, context, hubContext, notifService, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Tour {TourId}: auto-start failed, skipping this cycle", tour.Id);
                DetachDirtyEntries(context);
            }
        }

        // 2. Check IN_PROGRESS tours for waypoint completion and auto-complete
        var activeLoaded = await context.Tours
            .IgnoreQueryFilters()
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .Include(t => t.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Where(t => t.Status == "in_progress")
            .ToListAsync(ct);
        var activeTours = KeepToursOfOwnVehicle(activeLoaded);

        foreach (var tour in activeTours)
        {
            // Per-tour isolation: one failing tour (e.g. deleted concurrently →
            // DbUpdateConcurrencyException) must not abort the other tenants'
            // monitoring for the cycle. The shared context is cleaned of the
            // failed tour's dirty entries so they can't poison the next save.
            try
            {
                await CheckWaypointProgress(tour, context, hubContext, notifService, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Tour {TourId}: monitoring failed, skipping this cycle", tour.Id);
                DetachDirtyEntries(context);
            }
        }

        // Drop per-tour state for tours no longer active (completed/cancelled
        // via the controller — CompleteTourAutomatically only covers the
        // service's own path).
        var activeIds = activeTours.Select(t => t.Id).ToHashSet();
        SweepStale(_traceCursor, activeIds);
        SweepStale(_lastDeviationAlertAt, activeIds);
        SweepStale(_departedAt, activeIds);
        SweepStale(_pendingDestArrival, activeIds);
        SweepStale(_phoneCursor, activeIds);
        SweepStale(_recoveryCount, activeIds);
        SweepStale(_lostSince, activeIds);
        SweepStale(_lostAlerted, activeIds);
        SweepStale(_foreignVehicleReported,
            plannedLoaded.Concat(activeLoaded).Select(t => t.Id).ToHashSet());
        SweepStale(_notStartedAlerted, plannedLoaded.Select(t => t.Id).ToHashSet());
    }

    /// <summary>
    /// Tournée envoyée au chauffeur et toujours « planifiée » 15 min après l'heure
    /// prévue : une seule alerte au gestionnaire. La tournée reste planifiée — c'est
    /// au chauffeur (« Je pars ») ou au gestionnaire (Démarrer) de la lancer.
    /// </summary>
    private async Task CheckNotStarted(Tour tour, GisDbContext context, INotificationService notifService, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (!DriverTourRules.IsNotStartedAlertDue(tour, now)) return;
        if (!_notStartedAlerted.TryAdd(tour.Id, 0)) return;

        _logger.LogWarning("Tour {TourId} '{TourName}': envoyée au chauffeur, non démarrée {Minutes:F0} min après l'heure prévue",
            tour.Id, tour.Name, (now - tour.ScheduledStartTime).TotalMinutes);
        await SendTourNotification(context, notifService, tour.CompanyId, tour.VehicleId,
            "tour_not_started",
            $"Tournee non demarree: {tour.Name}",
            $"Le chauffeur n'a pas signale son depart (prevu {LocalTime(tour.ScheduledStartTime)}).",
            "high", "tour", tour.Id, $"/tournees/{tour.Id}", ct, tour.SentByUserId);
    }

    private static string LocalTime(DateTime utc)
    {
        var tz = QuietHoursPolicy.ResolveTimeZone(null);
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz).ToString("HH:mm");
    }

    /// <summary>Tournées déjà signalées comme écartées (un avertissement par
    /// tournée, pas un toutes les 30 s).</summary>
    private static readonly ConcurrentDictionary<int, byte> _foreignVehicleReported = new();

    /// <summary>
    /// Écarte les tournées dont le véhicule n'appartient plus à la société de
    /// la tournée (transfert par un administrateur système) : ni démarrage
    /// automatique, ni lecture de trace, ni notification. Le contrôleur empêche
    /// d'affecter un véhicule étranger, mais ce service lit hors filtre société
    /// — défense en profondeur, quelle que soit l'origine des données
    /// (relecture du 18/09/2026, cf. TourPlanning.VehicleBelongsToTourCompany).
    /// La tournée reste visible et annulable par sa société.
    /// </summary>
    private List<Tour> KeepToursOfOwnVehicle(List<Tour> tours)
    {
        var kept = new List<Tour>(tours.Count);
        foreach (var tour in tours)
        {
            if (TourPlanning.VehicleBelongsToTourCompany(tour))
            {
                kept.Add(tour);
                continue;
            }

            if (_foreignVehicleReported.TryAdd(tour.Id, 0))
            {
                _logger.LogWarning(
                    "Tour {TourId} (company {CompanyId}, status {Status}) not monitored: vehicle {VehicleId} belongs to company {VehicleCompanyId}",
                    tour.Id, tour.CompanyId, tour.Status, tour.VehicleId, tour.Vehicle?.CompanyId);
            }
        }
        return kept;
    }

    private static void SweepStale<TValue>(ConcurrentDictionary<int, TValue> dict, HashSet<int> activeIds)
    {
        foreach (var staleId in dict.Keys.Where(id => !activeIds.Contains(id)))
            dict.TryRemove(staleId, out _);
    }

    private static void DetachDirtyEntries(GisDbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries()
                     .Where(e => e.State != Microsoft.EntityFrameworkCore.EntityState.Unchanged).ToList())
            entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
    }

    /// <summary>
    /// Auto-start: when scheduled time has passed, start the tour automatically.
    /// Pure time-based — no GPS/position/speed requirements for starting.
    /// </summary>
    private async Task CheckAutoStart(Tour tour, GisDbContext context, IHubContext<GpsHub> hubContext, INotificationService notifService, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (now < tour.ScheduledStartTime)
        {
            _logger.LogInformation(
                "Tour {TourId} '{TourName}': waiting for scheduled time ({Scheduled:HH:mm} UTC, now={Now:HH:mm} UTC)",
                tour.Id, tour.Name, tour.ScheduledStartTime, now);
            return;
        }

        // Scheduled time has passed → auto-start
        _logger.LogInformation(
            "Auto-starting tour {TourId} '{TourName}': scheduled={Scheduled:HH:mm} UTC, now={Now:HH:mm} UTC",
            tour.Id, tour.Name, tour.ScheduledStartTime, now);

        // Règle commune avec le bouton « Démarrer » (TourPlanning.Start) : statut,
        // décalage des estimations d'un départ en retard (les échéances mesurent
        // le temps de CONDUITE de chaque tronçon, pas le retard au départ),
        // origine atteinte — IsCompleted ET WaypointStatus, l'origine restait
        // « pending » avant le 18/09/2026.
        var startDelay = TourPlanning.Start(tour, now);
        if (startDelay > TimeSpan.Zero)
        {
            _logger.LogInformation(
                "Tour {TourId}: started {Delay:F0} min late — estimated arrival times shifted accordingly",
                tour.Id, startDelay.TotalMinutes);
        }

        await context.SaveChangesAsync(ct);

        // Notify via SignalR (real-time)
        await hubContext.Clients.Group($"company_{tour.CompanyId}")
            .SendAsync("TourStatusChanged", new
            {
                tourId = tour.Id,
                status = "in_progress",
                tourName = tour.Name,
                message = $"Tournee '{tour.Name}' demarree automatiquement",
                timestamp = now
            }, ct);

        // Persist notification
        await SendTourNotification(context, notifService, tour.CompanyId, tour.VehicleId,
            "tour_started",
            $"Tournee demarree: {tour.Name}",
            $"Tournee '{tour.Name}' demarree a l'heure prevue.",
            "normal", "tour", tour.Id, $"/tournees/{tour.Id}", ct, tour.SentByUserId);

        _logger.LogInformation("Tour {TourId} auto-started successfully", tour.Id);
    }

    /// <summary>
    /// Check if vehicle has reached any uncompleted waypoints or the destination.
    ///
    /// PRECISION: detection runs on the GPS TRACE since the last cycle, not on a
    /// single "current position" sample — the previous implementation missed any
    /// waypoint the vehicle crossed between two 30-second checks (500 m at
    /// 60 km/h), and stalled entirely when the Redis position cache was cold.
    /// Geofence-linked waypoints keep the entry-event detection and gain the
    /// radius check as a REAL fallback (a vehicle already inside the zone at
    /// tour start never produces an "entry" event).
    ///
    /// The destination only validates once every earlier stop is resolved and a
    /// short grace period has elapsed — otherwise a round trip (destination =
    /// origin) auto-completes the instant it starts.
    ///
    /// Also checks deadlines: if estimated arrival + margin is exceeded, marks
    /// the stop as "temps_depasse".
    /// </summary>
    private async Task CheckWaypointProgress(Tour tour, GisDbContext context, IHubContext<GpsHub> hubContext, INotificationService notifService, CancellationToken ct)
    {
        var waypoints = tour.Waypoints.OrderBy(w => w.SequenceOrder).ToList();
        var now = DateTime.UtcNow;
        var startFloor = tour.ActualStartTime ?? tour.ScheduledStartTime;
        var changed = false;

        // GPS trace slice to analyse this cycle: rows with Id above the cursor.
        // First cycle after a (re)start scans from the tour start — one bounded
        // catch-up query that also recovers arrivals missed while the API was
        // down. Cursoring on the insertion Id (not recorded_at) means buffered
        // frames that commit late still land in the next slice.
        var sinceId = _traceCursor.TryGetValue(tour.Id, out var cursor) ? cursor : 0L;
        var traceFloor = startFloor < now.AddHours(-24) ? now.AddHours(-24) : startFloor; // volume guard
        var trace = await GetTraceSlice(context, tour, sinceId, traceFloor, now, ct);
        // NB: the cursor only advances AFTER a successful SaveChanges below —
        // a transient DB failure must not permanently skip this trace slice.
        var newCursor = trace.Count > 0 ? trace[^1].Id : (long?)null;

        // SECONDE trace : le téléphone du chauffeur (migration 051), même curseur par id.
        // Les deux traces sont analysées ENSEMBLE, dans l'ordre du temps : la première qui
        // montre l'arrivée valide l'étape, et l'étape retient laquelle (ArrivalSource).
        var phoneSinceId = _phoneCursor.TryGetValue(tour.Id, out var pcur) ? pcur : 0L;
        var phoneTrace = await GetPhoneTraceSlice(context, tour, phoneSinceId, traceFloor, now, ct);
        var newPhoneCursor = phoneTrace.Count > 0 ? phoneTrace[^1].Id : (long?)null;
        // Catch-up slices can interleave live and buffered frames: walk in TIME order.
        var timeOrdered = trace.Concat(phoneTrace).OrderBy(p => p.RecordedAt).ToList();

        // "Current" position for deviation checks: freshest trace point, else cache/DB.
        var freshest = timeOrdered.Count > 0 ? timeOrdered[^1] : null;
        var freshestDevice = trace.Count > 0 ? trace.OrderBy(p => p.RecordedAt).Last() : null;
        var devicePosition = freshestDevice != null
            ? new VehiclePositionCache { Latitude = freshestDevice.Latitude, Longitude = freshestDevice.Longitude, RecordedAt = freshestDevice.RecordedAt, IgnitionOn = true }
            : await GetVehiclePosition(tour, context, ct);
        var position = freshest != null
            ? new VehiclePositionCache { Latitude = freshest.Latitude, Longitude = freshest.Longitude, RecordedAt = freshest.RecordedAt }
            : devicePosition;

        // Source de suivi (boîtier, téléphone, aucune) : choisie à chaque cycle, écrite sur
        // la tournée quand elle change, alerte « suivi perdu » après 10 min sans rien.
        if (await UpdateTrackingSourceAsync(tour, context, hubContext, notifService, devicePosition,
                phoneTrace.Count > 0 ? phoneTrace[^1] : null, now, ct))
            changed = true;

        // Departure tracking: the first sample farther than DEPARTURE_RADIUS_METERS
        // from the origin. Rebuilt from the catch-up scan after a restart.
        var origin = waypoints.FirstOrDefault(w => w.Type == "origin");
        DateTime? departedAt = _departedAt.TryGetValue(tour.Id, out var dep) ? dep : null;
        if (departedAt == null)
        {
            if (origin == null)
            {
                departedAt = startFloor;               // no origin defined → nothing to depart from
            }
            else
            {
                var firstAway = timeOrdered.FirstOrDefault(p =>
                    HaversineDistance(p.Latitude, p.Longitude, origin.Latitude, origin.Longitude) > DEPARTURE_RADIUS_METERS);
                if (firstAway != null)
                {
                    departedAt = firstAway.RecordedAt;
                }
                else if (startFloor < traceFloor)
                {
                    // Trace window clamped (tour older than 24 h): the departure
                    // may predate the window — assume it happened rather than
                    // lock the destination forever.
                    departedAt = traceFloor;
                }
            }
            if (departedAt.HasValue) _departedAt[tour.Id] = departedAt.Value;

            // Persiste la PREMIÈRE mise en mouvement réelle : la durée de conduite
            // se mesure à partir d'ici, pas du clic « démarrer ». Uniquement sur
            // preuve GPS (firstAway) — les fallbacks (pas d'origine, fenêtre
            // tronquée) ne représentent pas un vrai départ observé.
            if (tour.ActualDepartureTime == null && departedAt.HasValue && origin != null && departedAt.Value > startFloor)
            {
                tour.ActualDepartureTime = departedAt.Value;
                changed = true;

                // Même logique que le décalage « démarrage en retard » : les
                // échéances par étape mesurent le temps de CONDUITE. Si le
                // chauffeur attend N minutes après le lancement avant de partir,
                // décaler les arrivées estimées des étapes non atteintes, sinon
                // l'attente compte comme du retard de trajet.
                var waitDelay = departedAt.Value - startFloor;
                if (TourPlanning.ShiftPendingEstimates(waypoints, waitDelay))
                {
                    _logger.LogInformation(
                        "Tour {TourId}: vehicle departed {Wait:F0} min after start — estimates shifted to measure driving time",
                        tour.Id, waitDelay.TotalMinutes);
                }
            }
        }

        foreach (var wp in waypoints)
        {
            // Étape DÉCLARÉE par le chauffeur (« Je suis arrivé ») : le statut est acquis,
            // mais l'heure qui fait foi est l'heure DÉTECTÉE — on continue de chercher
            // l'arrivée dans les traces et on remplace l'heure déclarée dès qu'on la voit.
            if (wp.IsCompleted && wp.ArrivalSource == DriverTourRules.SourceDriver)
            {
                var radiusDeclared = wp.Type == "destination" ? DESTINATION_RADIUS_METERS : WAYPOINT_RADIUS_METERS;
                var detected = FindRadiusArrival(timeOrdered, wp.Latitude, wp.Longitude, radiusDeclared, null);
                if (detected != null)
                {
                    DriverTourRules.ConfirmDeclaredArrival(wp, detected.RecordedAt, detected.Source);
                    changed = true;
                    _logger.LogInformation("Tour {TourId}: declared arrival at '{WpName}' confirmed by {Source} at {Time:HH:mm:ss}",
                        tour.Id, wp.Name ?? wp.Type, detected.Source, detected.RecordedAt);
                }
                continue;
            }
            if (wp.IsCompleted || wp.WaypointStatus == "completed" || wp.WaypointStatus == "skipped") continue;

            var radius = wp.Type == "destination" ? DESTINATION_RADIUS_METERS : WAYPOINT_RADIUS_METERS;

            // Destination gating — see summary. "Resolved" = anything but pending
            // (completed, skipped or temps_depasse), so a missed stop doesn't
            // block the completion of the tour forever. A blocked destination
            // skips the ARRIVAL checks only — its deadline check below still runs.
            var destinationBlocked = false;
            if (wp.Type == "destination")
            {
                var earlierStillPending = TourPlanning.HasPendingStopBefore(waypoints, wp);
                var inGrace = now < startFloor.AddMinutes(DESTINATION_GRACE_MINUTES);
                // The vehicle must have actually LEFT the origin: auto-start is
                // time-based, so a round trip would otherwise complete from the
                // parked-at-depot frames as soon as the grace expires.
                destinationBlocked = earlierStillPending || inGrace || departedAt == null;
            }

            bool arrived = false;

            // Method 1: Geofence events (if waypoint is linked to a geofence).
            // Intermediate stops: any entry since the tour start counts (the
            // vehicle may have serviced the stop and left again).
            // Destination: the LATEST zone event must be an entry (i.e. the
            // vehicle is still inside) and be after the actual departure — a
            // mid-tour depot reload leaves a durable stale "entry" row that
            // would otherwise complete the tour the moment the gate opens.
            if (!destinationBlocked && wp.GeofenceId.HasValue)
            {
                if (wp.Type == "destination")
                {
                    var eventFloor = departedAt.HasValue && departedAt.Value > startFloor ? departedAt.Value : startFloor;
                    var lastZoneEvent = await context.GeofenceEvents
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(e => e.GeofenceId == wp.GeofenceId.Value
                            && e.VehicleId == tour.VehicleId
                            && (e.Type == "entry" || e.Type == "exit")
                            && e.Timestamp >= eventFloor)
                        .OrderByDescending(e => e.Timestamp)
                        .FirstOrDefaultAsync(ct);

                    if (lastZoneEvent?.Type == "entry")
                    {
                        arrived = true;
                        wp.ActualArrivalTime = lastZoneEvent.Timestamp;
                        wp.ArrivalSource = DriverTourRules.SourceGeofence;
                        _logger.LogInformation(
                            "Tour {TourId}: vehicle inside destination geofence since {Time:HH:mm}",
                            tour.Id, lastZoneEvent.Timestamp);
                    }
                }
                else
                {
                    var recentEntry = await context.GeofenceEvents
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(e => e.GeofenceId == wp.GeofenceId.Value
                            && e.VehicleId == tour.VehicleId
                            && e.Type == "entry"
                            && e.Timestamp >= startFloor)
                        .OrderByDescending(e => e.Timestamp)
                        .FirstOrDefaultAsync(ct);

                    if (recentEntry != null)
                    {
                        arrived = true;
                        wp.ActualArrivalTime = recentEntry.Timestamp;
                        wp.ArrivalSource = DriverTourRules.SourceGeofence;
                        _logger.LogInformation(
                            "Tour {TourId}: vehicle entered geofence zone for waypoint '{WpName}' at {Time:HH:mm}",
                            tour.Id, wp.Name ?? wp.Type, recentEntry.Timestamp);
                    }
                }
            }

            // Method 2: trace-based radius detection. A qualifying arrival must
            // look like a STOP (slow sample inside the radius, or dwell across
            // two samples ≥ ARRIVAL_DWELL_SECONDS) — a single drive-by sample at
            // road speed no longer completes a stop the route merely passes near.
            // Runs for plain waypoints AND as fallback for geofence-linked ones
            // (a vehicle already inside the zone at start never emits "entry").
            if (!arrived)
            {
                var minTime = wp.Type == "destination" ? departedAt : null;
                var candidate = FindRadiusArrival(timeOrdered, wp.Latitude, wp.Longitude, radius, minTime);

                if (candidate != null)
                {
                    if (wp.Type == "destination" && destinationBlocked)
                    {
                        // Gate still closed, but the cursor is about to consume
                        // these samples — stash the true arrival time and apply
                        // it on the first unblocked cycle (otherwise a vehicle
                        // that parks and cuts ignition at the destination leaves
                        // the tour "in_progress" forever).
                        _pendingDestArrival.TryAdd(tour.Id, candidate);
                    }
                    else
                    {
                        arrived = true;
                        wp.ActualArrivalTime = candidate.RecordedAt;
                        wp.ArrivalSource = candidate.Source;
                        _logger.LogInformation(
                            "Tour {TourId}: vehicle reached waypoint '{WpName}' ({WpType}) at {Time:HH:mm:ss} ({Source})",
                            tour.Id, wp.Name ?? wp.Type, wp.Type, candidate.RecordedAt, candidate.Source);
                    }
                }

                // Apply a previously stashed destination arrival once unblocked.
                if (!arrived && wp.Type == "destination" && !destinationBlocked
                    && _pendingDestArrival.TryRemove(tour.Id, out var stashed))
                {
                    arrived = true;
                    wp.ActualArrivalTime = stashed.RecordedAt;
                    wp.ArrivalSource = stashed.Source;
                    _logger.LogInformation(
                        "Tour {TourId}: destination had been reached at {Time:HH:mm:ss} (while earlier stops were still pending)",
                        tour.Id, stashed.RecordedAt);
                }
            }

            // Waypoint reached → mark as completed
            if (arrived)
            {
                wp.IsCompleted = true;
                wp.WaypointStatus = "completed";
                changed = true;

                await hubContext.Clients.Group($"company_{tour.CompanyId}")
                    .SendAsync("TourWaypointCompleted", new
                    {
                        tourId = tour.Id,
                        waypointId = wp.Id,
                        waypointName = wp.Name ?? wp.Type,
                        waypointType = wp.Type,
                        waypointStatus = "completed",
                        actualArrivalTime = wp.ActualArrivalTime,
                        timestamp = now
                    }, ct);

                var wpLabel = wp.Name ?? wp.Address ?? GetWaypointTypeLabel(wp.Type);
                await SendTourNotification(context, notifService, tour.CompanyId, tour.VehicleId,
                    "tour_waypoint",
                    $"Point atteint: {wpLabel}",
                    $"Tournee '{tour.Name}' - le vehicule est arrive a '{wpLabel}'.",
                    "normal", "tour", tour.Id, $"/tournees/{tour.Id}", ct, tour.SentByUserId);

                if (wp.Type == "destination")
                {
                    await CompleteTourAutomatically(tour, context, hubContext, notifService, ct);
                    return;
                }

                continue;
            }

            // Deadline check: EstimatedArrivalTime + DeadlineMarginMinutes exceeded?
            // (TourPlanning.IsOverdue — every stop gets an estimate, even when
            // routing was down, so a missed stop always ends up resolved here.)
            if (TourPlanning.IsOverdue(wp, now))
            {
                var deadline = TourPlanning.DeadlineOf(wp)!.Value;
                wp.WaypointStatus = "temps_depasse";
                changed = true;

                _logger.LogWarning(
                    "Tour {TourId}: waypoint '{WpName}' deadline exceeded (deadline={Deadline:HH:mm}, now={Now:HH:mm})",
                    tour.Id, wp.Name ?? wp.Type, deadline, now);

                await hubContext.Clients.Group($"company_{tour.CompanyId}")
                    .SendAsync("TourWaypointOverdue", new
                    {
                        tourId = tour.Id,
                        waypointId = wp.Id,
                        waypointName = wp.Name ?? wp.Type,
                        waypointStatus = "temps_depasse",
                        deadline,
                        estimatedArrival = wp.EstimatedArrivalTime,
                        marginMinutes = wp.DeadlineMarginMinutes,
                        timestamp = now
                    }, ct);

                var wpLabel = wp.Name ?? wp.Address ?? GetWaypointTypeLabel(wp.Type);
                await SendTourNotification(context, notifService, tour.CompanyId, tour.VehicleId,
                    "tour_overdue",
                    $"Temps depasse: {wpLabel}",
                    $"Tournee '{tour.Name}' — le vehicule n'est pas arrive a '{wpLabel}' dans le delai imparti (prevu {wp.EstimatedArrivalTime.Value:HH:mm} + {wp.DeadlineMarginMinutes}min de marge).",
                    "high", "tour", tour.Id, $"/tournees/{tour.Id}", ct, tour.SentByUserId);
            }
        }

        // Check for route deviation
        if (position != null)
        {
            await CheckRouteDeviation(tour, position, waypoints, hubContext, ct);
        }

        if (changed)
        {
            await context.SaveChangesAsync(ct);
        }

        if (newCursor.HasValue)
            _traceCursor[tour.Id] = newCursor.Value;
        if (newPhoneCursor.HasValue)
            _phoneCursor[tour.Id] = newPhoneCursor.Value;
    }

    /// <summary>
    /// Source qui suit la tournée en ce moment (TrackingSourceSelector) : écrite sur la
    /// tournée quand elle change, annoncée en SignalR (« Suivi par téléphone ») et, sans
    /// aucune source pendant 10 min, une alerte « suivi perdu » au gestionnaire — une seule
    /// par coupure. Rend vrai si la tournée a été modifiée.
    /// </summary>
    private async Task<bool> UpdateTrackingSourceAsync(Tour tour, GisDbContext context, IHubContext<GpsHub> hubContext,
        INotificationService notifService, VehiclePositionCache? device, TracePoint? lastPhoneInSlice, DateTime now, CancellationToken ct)
    {
        // Dernier point du téléphone : celui de la tranche, sinon le dernier en base
        // (le téléphone peut n'avoir rien envoyé depuis plusieurs cycles).
        DateTime? phoneAt = lastPhoneInSlice?.RecordedAt;
        double? phoneAcc = null;
        var phoneMocked = false;
        if (phoneAt == null)
        {
            var last = await context.DriverAppPositions.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.TourId == tour.Id)
                .OrderByDescending(p => p.RecordedAt)
                .Select(p => new { p.RecordedAt, p.AccuracyM, p.IsMocked })
                .FirstOrDefaultAsync(ct);
            if (last != null) { phoneAt = last.RecordedAt; phoneAcc = last.AccuracyM; phoneMocked = last.IsMocked; }
        }

        var deviceState = new TrackingSourceSelector.DeviceState(device?.RecordedAt, device?.IgnitionOn ?? false);
        var phoneState = new TrackingSourceSelector.PhoneState(phoneAt, phoneAcc, phoneMocked);
        var previous = tour.TrackingSource;
        var count = _recoveryCount.TryGetValue(tour.Id, out var c) ? c : 0;
        var choice = TrackingSourceSelector.Choose(now, deviceState, phoneState, previous, count);
        _recoveryCount[tour.Id] = choice.RecoveryCount;

        var changed = false;
        if (choice.Source != previous)
        {
            tour.TrackingSource = choice.Source;
            tour.TrackingSourceSince = now;
            changed = true;
            if (choice.Source == TrackingSourceSelector.None) _lostSince.TryAdd(tour.Id, now);
            else _lostSince.TryRemove(tour.Id, out _);

            _logger.LogInformation("Tour {TourId}: tracking source {Previous} → {Source}", tour.Id, previous ?? "?", choice.Source);
            await hubContext.Clients.Group($"company_{tour.CompanyId}").SendAsync("TourTrackingSourceChanged", new
            {
                tourId = tour.Id, source = choice.Source, since = now,
                deviceAvailable = choice.DeviceAlive, phoneAvailable = choice.PhoneAlive, timestamp = now
            }, ct);
        }

        if (choice.Source == TrackingSourceSelector.None)
        {
            var since = _lostSince.GetOrAdd(tour.Id, now);
            if (now - since >= TrackingSourceSelector.LostAlertAfter && _lostAlerted.TryAdd(tour.Id, 0))
            {
                await SendTourNotification(context, notifService, tour.CompanyId, tour.VehicleId,
                    "tour_tracking_lost",
                    $"Suivi interrompu: {tour.Name}",
                    $"Ni le boitier ni le telephone du chauffeur n'ont donne de position depuis {(int)(now - since).TotalMinutes} min.",
                    "high", "tour", tour.Id, $"/tournees/{tour.Id}", ct, tour.SentByUserId);
            }
        }
        else
        {
            _lostAlerted.TryRemove(tour.Id, out _);
        }
        return changed;
    }

    /// <summary>
    /// Auto-complete the tour when destination is reached
    /// </summary>
    private async Task CompleteTourAutomatically(Tour tour,
        GisDbContext context, IHubContext<GpsHub> hubContext, INotificationService notifService, CancellationToken ct)
    {
        tour.Status = "completed";
        tour.ActualEndTime = DateTime.UtcNow;

        // Même état final des étapes que la clôture manuelle (la destination
        // vient d'être atteinte ; remet aussi en cohérence les étapes cochées
        // restées « pending » avant le 18/09/2026).
        TourPlanning.CloseWaypointsOnCompletion(tour, tour.ActualEndTime.Value);

        // Durée réelle de CONDUITE : depuis la première mise en mouvement
        // (ActualDepartureTime) si elle a été observée, sinon depuis le
        // démarrage. L'attente avant départ est exposée séparément côté API.
        if (tour.ActualStartTime.HasValue)
        {
            var drivingStart = tour.ActualDepartureTime ?? tour.ActualStartTime.Value;
            tour.ActualDurationMinutes = (int)(DateTime.UtcNow - drivingStart).TotalMinutes;
        }

        // Try to calculate actual distance from GPS history
        try
        {
            await CalculateActualMetrics(tour, context, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate actual metrics for tour {TourId}", tour.Id);
        }

        await context.SaveChangesAsync(ct);

        // Free the per-tour monitoring state AFTER the successful save — a
        // transient failure must not retrigger a full catch-up scan every 30 s.
        _traceCursor.TryRemove(tour.Id, out _);
        _lastDeviationAlertAt.TryRemove(tour.Id, out _);
        _departedAt.TryRemove(tour.Id, out _);
        _pendingDestArrival.TryRemove(tour.Id, out _);

        // Notify completion
        await hubContext.Clients.Group($"company_{tour.CompanyId}")
            .SendAsync("TourStatusChanged", new
            {
                tourId = tour.Id,
                status = "completed",
                tourName = tour.Name,
                message = $"Tournee '{tour.Name}' terminee automatiquement",
                actualDurationMinutes = tour.ActualDurationMinutes,
                actualDistanceKm = tour.ActualDistanceKm,
                timestamp = DateTime.UtcNow
            }, ct);

        // Persist notification
        await SendTourNotification(context, notifService, tour.CompanyId, tour.VehicleId,
            "tour_completed",
            $"Tournee terminee: {tour.Name}",
            $"La tournee '{tour.Name}' est terminee. Duree: {tour.ActualDurationMinutes} min, Distance: {tour.ActualDistanceKm} km.",
            "normal", "tour", tour.Id, $"/tournees/{tour.Id}", ct, tour.SentByUserId);

        _logger.LogInformation(
            "Tour {TourId} auto-completed. Duration={Duration}min, Distance={Distance}km",
            tour.Id, tour.ActualDurationMinutes, tour.ActualDistanceKm);
    }

    /// <summary>
    /// Calculate actual distance from GPS position history between start and end
    /// </summary>
    private async Task CalculateActualMetrics(Tour tour, GisDbContext context, CancellationToken ct)
    {
        if (!tour.ActualStartTime.HasValue || tour.Vehicle?.GpsDeviceId == null) return;

        var deviceId = tour.Vehicle.GpsDeviceId.Value;
        var startTime = tour.ActualStartTime.Value;
        // Anchor the end at the real arrival, not "now" — and bound the window
        // like GetTraceSlice does: a tour that sat in_progress for days would
        // otherwise materialize its vehicle's ENTIRE history in one query.
        var endTime = tour.Waypoints.FirstOrDefault(w => w.Type == "destination")?.ActualArrivalTime
            ?? tour.ActualEndTime ?? DateTime.UtcNow;
        if (startTime < endTime.AddHours(-24)) startTime = endTime.AddHours(-24);

        // Get GPS positions during the tour
        var positions = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                && p.RecordedAt >= startTime
                && p.RecordedAt <= endTime
                && p.IsValid)
            .OrderBy(p => p.RecordedAt)
            .Take(20_000)
            .Select(p => new { p.Latitude, p.Longitude, p.SpeedKph })
            .ToListAsync(ct);

        if (positions.Count < 2) return;

        // Sum distances between consecutive points
        double totalDistanceKm = 0;
        for (int i = 1; i < positions.Count; i++)
        {
            var dist = HaversineDistance(
                positions[i - 1].Latitude, positions[i - 1].Longitude,
                positions[i].Latitude, positions[i].Longitude);
            totalDistanceKm += dist / 1000.0;
        }

        tour.ActualDistanceKm = (decimal)Math.Round(totalDistanceKm, 2);

        // Estimate fuel from distance (rough: 8L/100km for diesel)
        var estimatedConsumption = 8.0m; // L/100km
        tour.ActualFuelLiters = Math.Round(tour.ActualDistanceKm.Value * estimatedConsumption / 100, 2);
    }

    /// <summary>
    /// Check if vehicle has deviated too far from the planned route
    /// </summary>
    private async Task CheckRouteDeviation(Tour tour, VehiclePositionCache position,
        List<TourWaypoint> waypoints, IHubContext<GpsHub> hubContext, CancellationToken ct)
    {
        // Find the next uncompleted waypoint
        var nextWp = waypoints.FirstOrDefault(w => !w.IsCompleted);
        if (nextWp == null) return;

        // Find the last completed waypoint
        var lastCompleted = waypoints.LastOrDefault(w => w.IsCompleted);
        if (lastCompleted == null) return;

        // Check distance from the line segment between last completed and next waypoint
        var distFromSegment = DistanceFromSegment(
            position.Latitude, position.Longitude,
            lastCompleted.Latitude, lastCompleted.Longitude,
            nextWp.Latitude, nextWp.Longitude);

        if (distFromSegment > DEVIATION_THRESHOLD_METERS)
        {
            // Cooldown: without it the alert fires every 30-second cycle for as
            // long as the vehicle stays off-route — pure notification spam.
            if (_lastDeviationAlertAt.TryGetValue(tour.Id, out var lastAlert)
                && DateTime.UtcNow - lastAlert < DeviationAlertCooldown)
                return;
            _lastDeviationAlertAt[tour.Id] = DateTime.UtcNow;

            _logger.LogWarning(
                "Tour {TourId}: vehicle deviated {Distance:F0}m from planned route",
                tour.Id, distFromSegment);

            await hubContext.Clients.Group($"company_{tour.CompanyId}")
                .SendAsync("TourDeviation", new
                {
                    tourId = tour.Id,
                    tourName = tour.Name,
                    deviationMeters = Math.Round(distFromSegment),
                    vehicleLatitude = position.Latitude,
                    vehicleLongitude = position.Longitude,
                    message = $"Vehicule devie de {Math.Round(distFromSegment)}m du trajet prevu",
                    timestamp = DateTime.UtcNow
                }, ct);
        }
    }

    /// <summary>GPS sample used for trace-based waypoint detection. <paramref name="Source"/> :
    /// « device » (boîtier, gps_positions) ou « phone » (téléphone du chauffeur, driver_app_positions).</summary>
    private sealed record TracePoint(long Id, double Latitude, double Longitude, double? SpeedKph, DateTime RecordedAt, string Source = DriverTourRules.SourceDevice);

    /// <summary>
    /// Trace du TÉLÉPHONE du chauffeur pour cette tournée (migration 051) : mêmes bornes que
    /// la trace du boîtier. Une position simulée ou imprécise (&gt; 100 m) n'est jamais une
    /// preuve d'arrivée.
    /// </summary>
    private static async Task<List<TracePoint>> GetPhoneTraceSlice(
        GisDbContext context, Tour tour, long sinceId, DateTime floor, DateTime until, CancellationToken ct)
    {
        var ceiling = until.AddMinutes(5);
        return await context.DriverAppPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.TourId == tour.Id
                && p.Id > sinceId
                && p.RecordedAt >= floor
                && p.RecordedAt <= ceiling
                && !p.IsMocked
                && (p.AccuracyM == null || p.AccuracyM <= TrackingSourceSelector.PhoneMaxAccuracyM))
            .OrderBy(p => p.Id)
            .Take(20_000)
            .Select(p => new TracePoint(p.Id, p.Latitude, p.Longitude, p.SpeedKph, p.RecordedAt, DriverTourRules.SourcePhone))
            .ToListAsync(ct);
    }

    /// <summary>
    /// GPS trace of the tour's vehicle: rows with Id &gt; <paramref name="sinceId"/>
    /// recorded in [floor, until+5min]. Cursoring on the insertion Id keeps
    /// late-committed buffered frames from being skipped; the recorded_at floor
    /// keeps the first catch-up cycle from scanning pre-tour history, and the
    /// small ceiling guards against far-future device clocks.
    /// </summary>
    private static async Task<List<TracePoint>> GetTraceSlice(
        GisDbContext context, Tour tour, long sinceId, DateTime floor, DateTime until, CancellationToken ct)
    {
        var deviceId = tour.Vehicle?.GpsDeviceId;
        if (deviceId == null) return new List<TracePoint>();

        var ceiling = until.AddMinutes(5);
        return await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId.Value
                && p.Id > sinceId
                && p.RecordedAt >= floor
                && p.RecordedAt <= ceiling
                && p.IsValid)
            .OrderBy(p => p.Id)
            .Take(20_000) // catch-up guard (24 h of 5 s frames ≈ 17 k)
            // Source explicite : un argument facultatif omis ne compile pas dans un arbre d'expression (CS0854).
            .Select(p => new TracePoint(p.Id, p.Latitude, p.Longitude, p.SpeedKph, p.RecordedAt, DriverTourRules.SourceDevice))
            .ToListAsync(ct);
    }

    /// <summary>
    /// First sample that looks like a genuine STOP at (lat, lon): inside the
    /// radius AND either slow (≤ ARRIVAL_MAX_SPEED_KPH) or corroborated by a
    /// CONTIGUOUS in-radius dwell of ≥ ARRIVAL_DWELL_SECONDS. Samples at or
    /// before <paramref name="minTime"/> are ignored (used to require an actual
    /// departure before a round-trip return can count).
    /// </summary>
    private static TracePoint? FindRadiusArrival(
        List<TracePoint> timeOrdered, double lat, double lon, double radius, DateTime? minTime)
    {
        TracePoint? firstInside = null;
        foreach (var pt in timeOrdered)
        {
            if (minTime.HasValue && pt.RecordedAt <= minTime.Value) continue;

            var inside = GeoMath.HaversineDistance(pt.Latitude, pt.Longitude, lat, lon) <= radius;
            if (!inside) { firstInside = null; continue; }   // dwell must be contiguous

            if (pt.SpeedKph.HasValue && pt.SpeedKph.Value <= ARRIVAL_MAX_SPEED_KPH)
                return firstInside ?? pt;

            firstInside ??= pt;
            if ((pt.RecordedAt - firstInside.RecordedAt).TotalSeconds >= ARRIVAL_DWELL_SECONDS)
                return firstInside;
        }
        return null;
    }

    /// <summary>
    /// Latest vehicle position: Redis cache when fresh (&lt; 5 min), otherwise
    /// the most recent DB position (&lt; 15 min). The old implementation had NO
    /// DB fallback despite its doc comment — a cold Redis cache silently
    /// disabled waypoint detection for the whole tour.
    /// </summary>
    private async Task<VehiclePositionCache?> GetVehiclePosition(Tour tour, GisDbContext context, CancellationToken ct)
    {
        if (tour.Vehicle?.GpsDevice == null) return null;

        var deviceUid = tour.Vehicle.GpsDevice.DeviceUid;
        if (!string.IsNullOrEmpty(deviceUid))
        {
            var cached = await _redisCache.GetPositionAsync(deviceUid);
            if (cached != null && (DateTime.UtcNow - cached.RecordedAt).TotalMinutes < 5)
                return cached;
        }

        // DB fallback — one index seek on (device_id, recorded_at DESC).
        var deviceId = tour.Vehicle.GpsDeviceId;
        if (deviceId == null) return null;

        var floor = DateTime.UtcNow.AddMinutes(-15);
        var latest = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId.Value && p.RecordedAt >= floor && p.IsValid)
            .OrderByDescending(p => p.RecordedAt)
            .Select(p => new { p.Latitude, p.Longitude, p.RecordedAt })
            .FirstOrDefaultAsync(ct);

        return latest == null ? null : new VehiclePositionCache
        {
            Latitude = latest.Latitude,
            Longitude = latest.Longitude,
            RecordedAt = latest.RecordedAt
        };
    }

    // Geo math delegated to GeoMath shared utility
    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        => GeoMath.HaversineDistance(lat1, lon1, lat2, lon2);

    private static double DistanceFromSegment(double pLat, double pLon,
        double aLat, double aLon, double bLat, double bLon)
        => GeoMath.DistanceFromSegment(pLat, pLon, aLat, aLon, bLat, bLon);

    // ═══════ HELPERS ═══════

    private static string GetWaypointTypeLabel(string type) => type switch
    {
        "origin" => "Depart",
        "destination" => "Destination",
        _ => "Arret"
    };

    /// <summary>
    /// Send a persisted notification to all users of the company
    /// </summary>
    private async Task SendTourNotification(
        GisDbContext context, INotificationService notifService,
        int companyId, int vehicleId, string type, string title, string message,
        string priority, string? refType, int? refId, string? actionUrl,
        CancellationToken ct, int? alsoUserId = null)
    {
        try
        {
            // Une tournee est le trajet d'UN vehicule : seuls les administrateurs
            // et les utilisateurs affectes a ce vehicule sont concernes. Avant,
            // la requete prenait tous les comptes de la societe — sans meme
            // filtrer sur "active" (incident Hertz du 15/09/2026).
            // PLUS la personne qui a envoye la tournee au chauffeur (alsoUserId), qui
            // n'est ni forcement administratrice ni affectee au vehicule.
            var userIds = await NotificationAudience.ForVehicleAsync(context, companyId, vehicleId, ct);
            if (alsoUserId is int extra && !userIds.Contains(extra)) userIds.Add(extra);

            foreach (var userId in userIds)
            {
                await notifService.CreateAndSendAsync(
                    companyId, userId, type, title, message,
                    priority, refType, refId, actionUrl, null, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send tour notifications for company {CompanyId}", companyId);
        }
    }
}
