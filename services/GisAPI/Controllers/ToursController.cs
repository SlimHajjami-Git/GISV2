using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using GisAPI.Services;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ToursController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly IValhallaService _valhallaService;
    private readonly IRedisCacheService _redisCache;
    private readonly ILogger<ToursController> _logger;

    public ToursController(GisDbContext context, IValhallaService valhallaService, IRedisCacheService redisCache, ILogger<ToursController> logger)
    {
        _context = context;
        _valhallaService = valhallaService;
        _redisCache = redisCache;
        _logger = logger;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    // ────────────────── LIST ──────────────────

    [HttpGet]
    public async Task<ActionResult> GetTours(
        [FromQuery] string? status = null,
        [FromQuery] int? vehicleId = null,
        [FromQuery] int? driverId = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var companyId = GetCompanyId();
        var query = _context.Tours
            .Where(t => t.CompanyId == companyId)
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

        var totalCount = await query.CountAsync();
        var tours = await query
            .OrderByDescending(t => t.ScheduledStartTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => MapToDto(t))
            .ToListAsync();

        return Ok(new { items = tours, totalCount, page, pageSize });
    }

    // ────────────────── GET BY ID ──────────────────

    [HttpGet("{id}")]
    public async Task<ActionResult> GetTour(int id)
    {
        var companyId = GetCompanyId();
        var tour = await _context.Tours
            .Where(t => t.Id == id && t.CompanyId == companyId)
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .Include(t => t.Pauses.OrderBy(p => p.StartTime))
            .FirstOrDefaultAsync();

        if (tour == null) return NotFound();
        return Ok(MapToDetailDto(tour));
    }

    // ────────────────── CREATE ──────────────────

    [HttpPost]
    public async Task<ActionResult> CreateTour([FromBody] CreateTourRequest request)
    {
        var companyId = GetCompanyId();

        if (request.Waypoints == null || request.Waypoints.Count < 2)
            return BadRequest(new { message = "Au moins 2 points (origine + destination) sont requis" });

        var vehicle = await _context.Vehicles.FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId);
        if (vehicle == null) return BadRequest(new { message = "Véhicule introuvable" });

        // Calculate route estimation via Valhalla
        var valhallaPoints = request.Waypoints.Select(w => new ValhallaPoint
        {
            Lat = w.Latitude,
            Lon = w.Longitude
        }).ToList();

        decimal estimatedDistanceKm = 0;
        int estimatedDurationMinutes = 0;
        string? routePolyline = null;
        List<double[]>? decodedRoute = null;

        try
        {
            var routeResult = await _valhallaService.GetRouteFromWaypointsAsync(valhallaPoints);
            if (routeResult != null)
            {
                estimatedDistanceKm = (decimal)routeResult.TotalDistanceKm;
                estimatedDurationMinutes = (int)Math.Ceiling(routeResult.TotalTimeSeconds / 60.0);
                routePolyline = routeResult.EncodedPolyline;
                decodedRoute = routeResult.DecodedPolyline;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Valhalla route estimation failed for tour");
        }

        // Estimate fuel based on vehicle type (avg 8L/100km for trucks, 6L/100km for cars)
        var fuelRate = vehicle.FuelType == "essence" ? 7.0m : 9.0m;
        var estimatedFuel = estimatedDistanceKm * fuelRate / 100m;

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
        await _context.SaveChangesAsync();

        // Add waypoints with estimated arrival times
        var cumulativeMinutes = 0.0;
        for (int i = 0; i < request.Waypoints.Count; i++)
        {
            var wp = request.Waypoints[i];
            var waypointType = i == 0 ? "origin" : (i == request.Waypoints.Count - 1 ? "destination" : "waypoint");

            // Estimate arrival time per waypoint proportionally
            DateTime? estimatedArrival = null;
            if (i == 0)
            {
                estimatedArrival = scheduledStart;
            }
            else if (estimatedDurationMinutes > 0 && decodedRoute != null)
            {
                // Simple proportional distribution based on sequence
                var fraction = (double)i / (request.Waypoints.Count - 1);
                cumulativeMinutes = fraction * estimatedDurationMinutes;
                // Add cumulative pauses from previous waypoints
                var pausesSoFar = request.Waypoints.Take(i).Sum(w => w.PlannedPauseMinutes);
                estimatedArrival = scheduledStart.AddMinutes(cumulativeMinutes + pausesSoFar);
            }

            var waypoint = new TourWaypoint
            {
                TourId = tour.Id,
                SequenceOrder = i,
                Name = wp.Name,
                Address = wp.Address,
                Latitude = wp.Latitude,
                Longitude = wp.Longitude,
                Type = waypointType,
                EstimatedArrivalTime = estimatedArrival,
                PlannedPauseMinutes = wp.PlannedPauseMinutes
            };
            _context.TourWaypoints.Add(waypoint);
        }
        await _context.SaveChangesAsync();

        // Reload with includes
        var created = await _context.Tours
            .Include(t => t.Vehicle)
            .Include(t => t.Driver)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .FirstAsync(t => t.Id == tour.Id);

        return CreatedAtAction(nameof(GetTour), new { id = tour.Id }, MapToDetailDto(created));
    }

    // ────────────────── UPDATE ──────────────────

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateTour(int id, [FromBody] UpdateTourRequest request)
    {
        var companyId = GetCompanyId();
        var tour = await _context.Tours
            .Include(t => t.Waypoints)
            .FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);

        if (tour == null) return NotFound();
        if (tour.Status != "planned")
            return BadRequest(new { message = "Seules les tournées planifiées peuvent être modifiées" });

        if (request.Name != null) tour.Name = request.Name;
        if (request.Description != null) tour.Description = request.Description;
        if (request.VehicleId.HasValue) tour.VehicleId = request.VehicleId.Value;
        if (request.DriverId.HasValue) tour.DriverId = request.DriverId.Value;
        if (request.Notes != null) tour.Notes = request.Notes;

        if (request.ScheduledStartTime.HasValue)
            tour.ScheduledStartTime = DateTime.SpecifyKind(request.ScheduledStartTime.Value, DateTimeKind.Utc);

        // If waypoints changed, recalculate route
        if (request.Waypoints != null && request.Waypoints.Count >= 2)
        {
            // Remove old waypoints
            _context.TourWaypoints.RemoveRange(tour.Waypoints);

            // Recalculate route
            var valhallaPoints = request.Waypoints.Select(w => new ValhallaPoint { Lat = w.Latitude, Lon = w.Longitude }).ToList();
            try
            {
                var routeResult = await _valhallaService.GetRouteFromWaypointsAsync(valhallaPoints);
                if (routeResult != null)
                {
                    tour.EstimatedDistanceKm = (decimal)routeResult.TotalDistanceKm;
                    tour.EstimatedDurationMinutes = (int)Math.Ceiling(routeResult.TotalTimeSeconds / 60.0);
                    tour.EstimatedRoutePolyline = routeResult.EncodedPolyline;

                    var vehicle = await _context.Vehicles.FindAsync(tour.VehicleId);
                    var fuelRate = vehicle?.FuelType == "essence" ? 7.0m : 9.0m;
                    tour.EstimatedFuelLiters = tour.EstimatedDistanceKm * fuelRate / 100m;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Valhalla route recalculation failed");
            }

            int totalPause = request.Waypoints.Sum(w => w.PlannedPauseMinutes);
            tour.TotalPauseMinutes = totalPause;
            tour.ScheduledEndTime = tour.ScheduledStartTime.AddMinutes(tour.EstimatedDurationMinutes + totalPause);

            for (int i = 0; i < request.Waypoints.Count; i++)
            {
                var wp = request.Waypoints[i];
                _context.TourWaypoints.Add(new TourWaypoint
                {
                    TourId = tour.Id,
                    SequenceOrder = i,
                    Name = wp.Name,
                    Address = wp.Address,
                    Latitude = wp.Latitude,
                    Longitude = wp.Longitude,
                    Type = i == 0 ? "origin" : (i == request.Waypoints.Count - 1 ? "destination" : "waypoint"),
                    PlannedPauseMinutes = wp.PlannedPauseMinutes
                });
            }
        }

        await _context.SaveChangesAsync();

        var updated = await _context.Tours
            .Include(t => t.Vehicle).Include(t => t.Driver)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .FirstAsync(t => t.Id == tour.Id);

        return Ok(MapToDetailDto(updated));
    }

    // ────────────────── DELETE ──────────────────

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteTour(int id)
    {
        var companyId = GetCompanyId();
        var tour = await _context.Tours.FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);
        if (tour == null) return NotFound();

        _context.Tours.Remove(tour);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // ────────────────── START TOUR ──────────────────

    [HttpPost("{id}/start")]
    public async Task<ActionResult> StartTour(int id)
    {
        var companyId = GetCompanyId();
        var tour = await _context.Tours
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);

        if (tour == null) return NotFound();
        if (tour.Status != "planned")
            return BadRequest(new { message = "La tournée doit être en statut 'planifiée' pour démarrer" });

        tour.Status = "in_progress";
        tour.ActualStartTime = DateTime.UtcNow;

        // Mark first waypoint as completed
        var firstWp = tour.Waypoints.FirstOrDefault();
        if (firstWp != null)
        {
            firstWp.IsCompleted = true;
            firstWp.ActualArrivalTime = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = "Tournée démarrée", actualStartTime = tour.ActualStartTime });
    }

    // ────────────────── COMPLETE WAYPOINT ──────────────────

    [HttpPost("{id}/waypoints/{waypointId}/complete")]
    public async Task<ActionResult> CompleteWaypoint(int id, int waypointId)
    {
        var companyId = GetCompanyId();
        var tour = await _context.Tours
            .Include(t => t.Waypoints)
            .FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);

        if (tour == null) return NotFound();
        if (tour.Status != "in_progress")
            return BadRequest(new { message = "La tournée doit être en cours" });

        var waypoint = tour.Waypoints.FirstOrDefault(w => w.Id == waypointId);
        if (waypoint == null) return NotFound(new { message = "Point de passage introuvable" });

        waypoint.IsCompleted = true;
        waypoint.ActualArrivalTime = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Point de passage complété", actualArrivalTime = waypoint.ActualArrivalTime });
    }

    // ────────────────── ADD PAUSE ──────────────────

    [HttpPost("{id}/pauses")]
    public async Task<ActionResult> AddPause(int id, [FromBody] AddPauseRequest request)
    {
        var companyId = GetCompanyId();
        var tour = await _context.Tours.FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);
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
        await _context.SaveChangesAsync();

        return Ok(new { id = pause.Id, message = "Pause démarrée" });
    }

    // ────────────────── END PAUSE ──────────────────

    [HttpPost("{id}/pauses/{pauseId}/end")]
    public async Task<ActionResult> EndPause(int id, int pauseId)
    {
        var companyId = GetCompanyId();
        var pause = await _context.TourPauses
            .Include(p => p.Tour)
            .FirstOrDefaultAsync(p => p.Id == pauseId && p.TourId == id && p.Tour!.CompanyId == companyId);

        if (pause == null) return NotFound();
        if (pause.EndTime.HasValue)
            return BadRequest(new { message = "Cette pause est déjà terminée" });

        pause.EndTime = DateTime.UtcNow;
        pause.DurationMinutes = (int)(pause.EndTime.Value - pause.StartTime).TotalMinutes;

        // Update total pause time
        var tour = pause.Tour!;
        tour.TotalPauseMinutes += pause.DurationMinutes.Value;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Pause terminée", durationMinutes = pause.DurationMinutes });
    }

    // ────────────────── COMPLETE TOUR ──────────────────

    [HttpPost("{id}/complete")]
    public async Task<ActionResult> CompleteTour(int id, [FromBody] CompleteTourRequest? request = null)
    {
        var companyId = GetCompanyId();
        var tour = await _context.Tours
            .Include(t => t.Vehicle)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .Include(t => t.Pauses)
            .FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);

        if (tour == null) return NotFound();
        if (tour.Status != "in_progress")
            return BadRequest(new { message = "La tournée doit être en cours pour être complétée" });

        tour.Status = "completed";
        tour.ActualEndTime = DateTime.UtcNow;

        if (tour.ActualStartTime.HasValue)
            tour.ActualDurationMinutes = (int)(tour.ActualEndTime.Value - tour.ActualStartTime.Value).TotalMinutes;

        // Set actual values from request or GPS data
        if (request?.ActualDistanceKm.HasValue == true)
            tour.ActualDistanceKm = request.ActualDistanceKm.Value;
        if (request?.ActualFuelLiters.HasValue == true)
            tour.ActualFuelLiters = request.ActualFuelLiters.Value;

        // Mark last waypoint as completed
        var lastWp = tour.Waypoints.LastOrDefault();
        if (lastWp != null && !lastWp.IsCompleted)
        {
            lastWp.IsCompleted = true;
            lastWp.ActualArrivalTime = DateTime.UtcNow;
        }

        // End any open pauses
        foreach (var pause in tour.Pauses.Where(p => !p.EndTime.HasValue))
        {
            pause.EndTime = DateTime.UtcNow;
            pause.DurationMinutes = (int)(pause.EndTime.Value - pause.StartTime).TotalMinutes;
        }

        tour.TotalPauseMinutes = tour.Pauses.Sum(p => p.DurationMinutes ?? 0);

        await _context.SaveChangesAsync();

        return Ok(MapToDetailDto(tour));
    }

    // ────────────────── CANCEL TOUR ──────────────────

    [HttpPost("{id}/cancel")]
    public async Task<ActionResult> CancelTour(int id)
    {
        var companyId = GetCompanyId();
        var tour = await _context.Tours.FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);
        if (tour == null) return NotFound();
        if (tour.Status == "completed")
            return BadRequest(new { message = "Impossible d'annuler une tournée terminée" });

        tour.Status = "cancelled";
        await _context.SaveChangesAsync();
        return Ok(new { message = "Tournée annulée" });
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
    public async Task<ActionResult> GetTourStats()
    {
        var companyId = GetCompanyId();
        var tours = await _context.Tours.Where(t => t.CompanyId == companyId).ToListAsync();

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
    public async Task<ActionResult> GetTourTracking(int id)
    {
        var companyId = GetCompanyId();
        var tour = await _context.Tours
            .Include(t => t.Vehicle).ThenInclude(v => v!.GpsDevice)
            .Include(t => t.Waypoints.OrderBy(w => w.SequenceOrder))
            .FirstOrDefaultAsync(t => t.Id == id && t.CompanyId == companyId);

        if (tour == null) return NotFound();

        VehiclePositionCache? position = null;
        if (tour.Vehicle?.GpsDevice != null)
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

        return Ok(new
        {
            tourId = tour.Id,
            tourStatus = tour.Status,
            vehicle = position != null ? new
            {
                latitude = position.Latitude,
                longitude = position.Longitude,
                speedKph = position.SpeedKph,
                headingDeg = position.HeadingDeg,
                ignitionOn = position.IgnitionOn,
                recordedAt = position.RecordedAt
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
                w.IsCompleted, w.ActualArrivalTime
            })
        });
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

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
        t.ActualEndTime,
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

    private static object MapToDetailDto(Tour t) => new
    {
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
        t.ActualEndTime,
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
            w.EstimatedArrivalTime,
            w.ActualArrivalTime,
            w.PlannedPauseMinutes,
            w.ActualPauseMinutes,
            w.IsCompleted,
            arrivalDelay = w.ActualArrivalTime.HasValue && w.EstimatedArrivalTime.HasValue
                ? (int)(w.ActualArrivalTime.Value - w.EstimatedArrivalTime.Value).TotalMinutes : (int?)null
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
    public int? DriverId { get; set; }
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
