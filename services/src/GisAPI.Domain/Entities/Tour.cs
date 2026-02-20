using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Tour : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Assignment
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public int? DriverId { get; set; }
    public User? Driver { get; set; }

    // Status: planned, in_progress, completed, cancelled
    public string Status { get; set; } = "planned";

    // Schedule
    public DateTime ScheduledStartTime { get; set; }
    public DateTime? ScheduledEndTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    public DateTime? ActualEndTime { get; set; }

    // Estimated (from Valhalla route calculation)
    public decimal EstimatedDistanceKm { get; set; }
    public int EstimatedDurationMinutes { get; set; }
    public decimal? EstimatedFuelLiters { get; set; }
    public string? EstimatedRoutePolyline { get; set; }

    // Actual (from GPS tracking after completion)
    public decimal? ActualDistanceKm { get; set; }
    public int? ActualDurationMinutes { get; set; }
    public decimal? ActualFuelLiters { get; set; }
    public string? ActualRoutePolyline { get; set; }

    // Pause tracking
    public int TotalPauseMinutes { get; set; }

    // Recurrence: none, daily, weekly
    public string Recurrence { get; set; } = "none";

    public string? Notes { get; set; }

    public ICollection<TourWaypoint> Waypoints { get; set; } = new List<TourWaypoint>();
    public ICollection<TourPause> Pauses { get; set; } = new List<TourPause>();
}

public class TourWaypoint : Entity
{
    public int TourId { get; set; }
    public Tour? Tour { get; set; }

    public int SequenceOrder { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    // Type: origin, waypoint, destination
    public string Type { get; set; } = "waypoint";

    // Optional: link to a geofence zone for automatic entry/exit detection
    public int? GeofenceId { get; set; }
    public Geofence? Geofence { get; set; }

    // Estimated travel time from previous waypoint (minutes), calculated per-leg by Valhalla
    public int EstimatedLegMinutes { get; set; }

    // Deadline margin in minutes (default 60). If vehicle doesn't arrive within
    // EstimatedArrivalTime + DeadlineMarginMinutes, status becomes "temps_depasse"
    public int DeadlineMarginMinutes { get; set; } = 60;

    public DateTime? EstimatedArrivalTime { get; set; }
    public DateTime? ActualArrivalTime { get; set; }

    // Planned pause at this waypoint (minutes)
    public int PlannedPauseMinutes { get; set; }
    public int? ActualPauseMinutes { get; set; }

    public bool IsCompleted { get; set; }

    // Status: pending, completed, temps_depasse, skipped
    public string WaypointStatus { get; set; } = "pending";
}

public class TourPause : Entity
{
    public int TourId { get; set; }
    public Tour? Tour { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int? DurationMinutes { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Reason: break, fuel, delivery, rest, other
    public string Reason { get; set; } = "break";
    public string? Notes { get; set; }
}
