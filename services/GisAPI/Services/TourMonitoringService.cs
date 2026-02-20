using System.Text.Json;
using GisAPI.Hubs;
using GisAPI.Domain.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Infrastructure.Persistence;

namespace GisAPI.Services;

/// <summary>
/// Background service that monitors active tours and automatically:
/// - Starts tours when vehicle leaves origin within scheduled time window
/// - Validates waypoints when vehicle arrives nearby (< 200m)
/// - Completes tours when vehicle reaches destination
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
    private const double MIN_SPEED_FOR_DEPARTURE_KPH = 5;
    private const double DEVIATION_THRESHOLD_METERS = 2000;

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
        var windowStart = now.AddMinutes(-SCHEDULE_WINDOW_MINUTES);
        var windowEnd = now.AddMinutes(SCHEDULE_WINDOW_MINUTES);

        // 1. Check PLANNED tours within time window for auto-start
        var plannedTours = await context.Tours
            .IgnoreQueryFilters()
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .Include(t => t.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Where(t => t.Status == "planned"
                && t.ScheduledStartTime >= windowStart
                && t.ScheduledStartTime <= windowEnd)
            .ToListAsync(ct);

        if (plannedTours.Count > 0)
        {
            _logger.LogInformation(
                "Tour Monitor: {Count} planned tour(s) in window [{Start:HH:mm} - {End:HH:mm}] UTC (now={Now:HH:mm:ss} UTC)",
                plannedTours.Count, windowStart, windowEnd, now);
        }

        foreach (var tour in plannedTours)
        {
            await CheckAutoStart(tour, context, hubContext, notifService, ct);
        }

        // 2. Check IN_PROGRESS tours for waypoint completion and auto-complete
        var activeTours = await context.Tours
            .IgnoreQueryFilters()
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .Include(t => t.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Where(t => t.Status == "in_progress")
            .ToListAsync(ct);

        foreach (var tour in activeTours)
        {
            await CheckWaypointProgress(tour, context, hubContext, notifService, ct);
        }
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

        tour.Status = "in_progress";
        tour.ActualStartTime = now;

        // Mark origin waypoint as completed
        var origin = tour.Waypoints.FirstOrDefault(w => w.Type == "origin");
        if (origin != null)
        {
            origin.IsCompleted = true;
            origin.ActualArrivalTime = now;
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
        await SendNotificationToCompanyUsers(context, notifService, tour.CompanyId,
            "tour_started",
            $"Tournee demarree: {tour.Name}",
            $"Tournee '{tour.Name}' demarree a l'heure prevue.",
            "normal", "tour", tour.Id, $"/tournees", ct);

        _logger.LogInformation("Tour {TourId} auto-started successfully", tour.Id);
    }

    /// <summary>
    /// Check if vehicle has reached any uncompleted waypoints or the destination.
    /// Uses geofence events when waypoint is linked to a geofence, otherwise radius check.
    /// Also checks deadlines: if estimated arrival + margin is exceeded, marks as "temps_depasse".
    /// </summary>
    private async Task CheckWaypointProgress(Tour tour, GisDbContext context, IHubContext<GpsHub> hubContext, INotificationService notifService, CancellationToken ct)
    {
        var position = await GetVehiclePosition(tour);
        var waypoints = tour.Waypoints.OrderBy(w => w.SequenceOrder).ToList();
        var now = DateTime.UtcNow;
        var changed = false;

        foreach (var wp in waypoints)
        {
            if (wp.IsCompleted || wp.WaypointStatus == "completed" || wp.WaypointStatus == "skipped") continue;

            bool arrived = false;

            // Method 1: Geofence-based detection (if waypoint is linked to a geofence)
            if (wp.GeofenceId.HasValue)
            {
                // Check if there's a recent geofence entry event for this vehicle in this zone
                var recentEntry = await context.GeofenceEvents
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(e => e.GeofenceId == wp.GeofenceId.Value
                        && e.VehicleId == tour.VehicleId
                        && e.Type == "entry"
                        && e.Timestamp >= (tour.ActualStartTime ?? tour.ScheduledStartTime))
                    .OrderByDescending(e => e.Timestamp)
                    .FirstOrDefaultAsync(ct);

                if (recentEntry != null)
                {
                    arrived = true;
                    wp.ActualArrivalTime = recentEntry.Timestamp;
                    _logger.LogInformation(
                        "Tour {TourId}: vehicle entered geofence zone for waypoint '{WpName}' at {Time:HH:mm}",
                        tour.Id, wp.Name ?? wp.Type, recentEntry.Timestamp);
                }
            }

            // Method 2: Radius-based detection (fallback, or if no geofence linked)
            if (!arrived && position != null && !wp.GeofenceId.HasValue)
            {
                var distance = HaversineDistance(
                    position.Latitude, position.Longitude,
                    wp.Latitude, wp.Longitude);

                var radius = wp.Type == "destination" ? DESTINATION_RADIUS_METERS : WAYPOINT_RADIUS_METERS;

                if (distance <= radius)
                {
                    arrived = true;
                    wp.ActualArrivalTime = now;
                    _logger.LogInformation(
                        "Tour {TourId}: vehicle reached waypoint '{WpName}' ({WpType}) at {Distance:F0}m",
                        tour.Id, wp.Name ?? wp.Type, wp.Type, distance);
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
                await SendNotificationToCompanyUsers(context, notifService, tour.CompanyId,
                    "tour_waypoint",
                    $"Point atteint: {wpLabel}",
                    $"Tournee '{tour.Name}' - le vehicule est arrive a '{wpLabel}'.",
                    "normal", "tour", tour.Id, $"/tournees", ct);

                if (wp.Type == "destination")
                {
                    await CompleteTourAutomatically(tour, position!, context, hubContext, notifService, ct);
                    return;
                }

                continue;
            }

            // Deadline check: EstimatedArrivalTime + DeadlineMarginMinutes exceeded?
            if (wp.WaypointStatus == "pending" && wp.EstimatedArrivalTime.HasValue)
            {
                var deadline = wp.EstimatedArrivalTime.Value.AddMinutes(wp.DeadlineMarginMinutes);
                if (now > deadline)
                {
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
                    await SendNotificationToCompanyUsers(context, notifService, tour.CompanyId,
                        "tour_overdue",
                        $"Temps depasse: {wpLabel}",
                        $"Tournee '{tour.Name}' — le vehicule n'est pas arrive a '{wpLabel}' dans le delai imparti (prevu {wp.EstimatedArrivalTime.Value:HH:mm} + {wp.DeadlineMarginMinutes}min de marge).",
                        "high", "tour", tour.Id, $"/tournees", ct);
                }
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
    }

    /// <summary>
    /// Auto-complete the tour when destination is reached
    /// </summary>
    private async Task CompleteTourAutomatically(Tour tour, VehiclePositionCache position,
        GisDbContext context, IHubContext<GpsHub> hubContext, INotificationService notifService, CancellationToken ct)
    {
        tour.Status = "completed";
        tour.ActualEndTime = DateTime.UtcNow;

        // Calculate actual duration
        if (tour.ActualStartTime.HasValue)
        {
            tour.ActualDurationMinutes = (int)(DateTime.UtcNow - tour.ActualStartTime.Value).TotalMinutes;
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
        await SendNotificationToCompanyUsers(context, notifService, tour.CompanyId,
            "tour_completed",
            $"Tournee terminee: {tour.Name}",
            $"La tournee '{tour.Name}' est terminee. Duree: {tour.ActualDurationMinutes} min, Distance: {tour.ActualDistanceKm} km.",
            "normal", "tour", tour.Id, $"/tournees", ct);

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
        var endTime = DateTime.UtcNow;

        // Get GPS positions during the tour
        var positions = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                && p.RecordedAt >= startTime
                && p.RecordedAt <= endTime
                && p.IsValid)
            .OrderBy(p => p.RecordedAt)
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

    /// <summary>
    /// Get latest vehicle position from Redis cache (fast) or fall back to DB
    /// </summary>
    private async Task<VehiclePositionCache?> GetVehiclePosition(Tour tour)
    {
        if (tour.Vehicle?.GpsDevice == null) return null;

        var deviceUid = tour.Vehicle.GpsDevice.DeviceUid;
        if (string.IsNullOrEmpty(deviceUid)) return null;

        // Try Redis first (real-time, < 1ms)
        var cached = await _redisCache.GetPositionAsync(deviceUid);
        if (cached != null)
        {
            // Only use if position is recent (< 5 minutes)
            if ((DateTime.UtcNow - cached.RecordedAt).TotalMinutes < 5)
                return cached;
        }

        return null;
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
    private async Task SendNotificationToCompanyUsers(
        GisDbContext context, INotificationService notifService,
        int companyId, string type, string title, string message,
        string priority, string? refType, int? refId, string? actionUrl,
        CancellationToken ct)
    {
        try
        {
            var userIds = await context.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => u.CompanyId == companyId)
                .Select(u => u.Id)
                .ToListAsync(ct);

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
