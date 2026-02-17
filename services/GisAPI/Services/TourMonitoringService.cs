using System.Text.Json;
using GisAPI.Hubs;
using GisAPI.Domain.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Interfaces;

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
    private const int SCHEDULE_WINDOW_MINUTES = 30;
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
        var context = scope.ServiceProvider.GetRequiredService<IGisDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GpsHub>>();

        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-SCHEDULE_WINDOW_MINUTES);
        var windowEnd = now.AddMinutes(SCHEDULE_WINDOW_MINUTES);

        // 1. Check PLANNED tours within time window for auto-start
        var plannedTours = await context.Tours
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .Include(t => t.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Where(t => t.Status == "planned"
                && t.ScheduledStartTime >= windowStart
                && t.ScheduledStartTime <= windowEnd)
            .ToListAsync(ct);

        foreach (var tour in plannedTours)
        {
            await CheckAutoStart(tour, context, hubContext, ct);
        }

        // 2. Check IN_PROGRESS tours for waypoint completion and auto-complete
        var activeTours = await context.Tours
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .Include(t => t.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Where(t => t.Status == "in_progress")
            .ToListAsync(ct);

        foreach (var tour in activeTours)
        {
            await CheckWaypointProgress(tour, context, hubContext, ct);
        }
    }

    /// <summary>
    /// Auto-start: if vehicle is near origin and moving, start the tour
    /// </summary>
    private async Task CheckAutoStart(Tour tour, IGisDbContext context, IHubContext<GpsHub> hubContext, CancellationToken ct)
    {
        var position = await GetVehiclePosition(tour);
        if (position == null) return;

        var origin = tour.Waypoints.FirstOrDefault(w => w.Type == "origin");
        if (origin == null) return;

        var distanceToOrigin = HaversineDistance(
            position.Latitude, position.Longitude,
            origin.Latitude, origin.Longitude);

        // Vehicle must have been near origin AND now be moving
        if (distanceToOrigin <= DEPARTURE_RADIUS_METERS && position.SpeedKph >= MIN_SPEED_FOR_DEPARTURE_KPH)
        {
            _logger.LogInformation(
                "Auto-starting tour {TourId} '{TourName}': vehicle at {Distance:F0}m from origin, speed={Speed:F1} km/h",
                tour.Id, tour.Name, distanceToOrigin, position.SpeedKph);

            tour.Status = "in_progress";
            tour.ActualStartTime = DateTime.UtcNow;

            // Mark origin as completed
            origin.IsCompleted = true;
            origin.ActualArrivalTime = DateTime.UtcNow;

            await context.SaveChangesAsync(ct);

            // Notify via SignalR
            await hubContext.Clients.Group($"company_{tour.CompanyId}")
                .SendAsync("TourStatusChanged", new
                {
                    tourId = tour.Id,
                    status = "in_progress",
                    tourName = tour.Name,
                    message = $"Tournee '{tour.Name}' demarree automatiquement",
                    timestamp = DateTime.UtcNow
                }, ct);

            _logger.LogInformation("Tour {TourId} auto-started successfully", tour.Id);
        }
    }

    /// <summary>
    /// Check if vehicle has reached any uncompleted waypoints or the destination
    /// </summary>
    private async Task CheckWaypointProgress(Tour tour, IGisDbContext context, IHubContext<GpsHub> hubContext, CancellationToken ct)
    {
        var position = await GetVehiclePosition(tour);
        if (position == null) return;

        var waypoints = tour.Waypoints.OrderBy(w => w.SequenceOrder).ToList();
        var changed = false;

        foreach (var wp in waypoints)
        {
            if (wp.IsCompleted) continue;

            var distance = HaversineDistance(
                position.Latitude, position.Longitude,
                wp.Latitude, wp.Longitude);

            var radius = wp.Type == "destination" ? DESTINATION_RADIUS_METERS : WAYPOINT_RADIUS_METERS;

            if (distance <= radius)
            {
                _logger.LogInformation(
                    "Tour {TourId}: vehicle reached waypoint '{WpName}' ({WpType}) at {Distance:F0}m",
                    tour.Id, wp.Name ?? wp.Type, wp.Type, distance);

                wp.IsCompleted = true;
                wp.ActualArrivalTime = DateTime.UtcNow;
                changed = true;

                // Notify waypoint completion
                await hubContext.Clients.Group($"company_{tour.CompanyId}")
                    .SendAsync("TourWaypointCompleted", new
                    {
                        tourId = tour.Id,
                        waypointId = wp.Id,
                        waypointName = wp.Name ?? wp.Type,
                        waypointType = wp.Type,
                        actualArrivalTime = wp.ActualArrivalTime,
                        timestamp = DateTime.UtcNow
                    }, ct);

                // If destination reached, complete the tour
                if (wp.Type == "destination")
                {
                    await CompleteTourAutomatically(tour, position, context, hubContext, ct);
                    return;
                }
            }
        }

        // Check for route deviation
        await CheckRouteDeviation(tour, position, waypoints, hubContext, ct);

        if (changed)
        {
            await context.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Auto-complete the tour when destination is reached
    /// </summary>
    private async Task CompleteTourAutomatically(Tour tour, VehiclePositionCache position,
        IGisDbContext context, IHubContext<GpsHub> hubContext, CancellationToken ct)
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

        _logger.LogInformation(
            "Tour {TourId} auto-completed. Duration={Duration}min, Distance={Distance}km",
            tour.Id, tour.ActualDurationMinutes, tour.ActualDistanceKm);
    }

    /// <summary>
    /// Calculate actual distance from GPS position history between start and end
    /// </summary>
    private async Task CalculateActualMetrics(Tour tour, IGisDbContext context, CancellationToken ct)
    {
        if (!tour.ActualStartTime.HasValue || tour.Vehicle?.GpsDeviceId == null) return;

        var deviceId = tour.Vehicle.GpsDeviceId.Value;
        var startTime = tour.ActualStartTime.Value;
        var endTime = DateTime.UtcNow;

        // Get GPS positions during the tour
        var positions = await context.GpsPositions
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

    // ═══════ GEO MATH ═══════

    /// <summary>
    /// Haversine distance between two points in meters
    /// </summary>
    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    /// <summary>
    /// Distance from point to line segment (for route deviation detection)
    /// </summary>
    private static double DistanceFromSegment(double pLat, double pLon,
        double aLat, double aLon, double bLat, double bLon)
    {
        var ap = new[] { pLat - aLat, pLon - aLon };
        var ab = new[] { bLat - aLat, bLon - aLon };
        var abLen2 = ab[0] * ab[0] + ab[1] * ab[1];

        if (abLen2 == 0)
            return HaversineDistance(pLat, pLon, aLat, aLon);

        var t = Math.Max(0, Math.Min(1, (ap[0] * ab[0] + ap[1] * ab[1]) / abLen2));
        var closestLat = aLat + t * ab[0];
        var closestLon = aLon + t * ab[1];

        return HaversineDistance(pLat, pLon, closestLat, closestLon);
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;
}
