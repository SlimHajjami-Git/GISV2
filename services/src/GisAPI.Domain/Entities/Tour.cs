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
    public Driver? Driver { get; set; }

    // Status: planned, in_progress, completed, cancelled
    public string Status { get; set; } = "planned";

    // Schedule
    public DateTime ScheduledStartTime { get; set; }
    public DateTime? ScheduledEndTime { get; set; }
    public DateTime? ActualStartTime { get; set; }
    // Première mise en mouvement RÉELLE (sortie du rayon du point de départ),
    // détectée par TourMonitoringService. Peut être bien après ActualStartTime
    // (clic « démarrer ») si le chauffeur attend avant de partir — la durée
    // réelle de conduite se mesure à partir d'ici, l'attente est exposée à part.
    public DateTime? ActualDepartureTime { get; set; }
    public DateTime? ActualEndTime { get; set; }

    // ── Envoi au chauffeur et suivi (migration 051) ──
    /// <summary>Dernier envoi au compte du chauffeur (push) ; remis à NULL si le chauffeur change.</summary>
    public DateTime? SentAt { get; set; }
    public int? SentByUserId { get; set; }
    /// <summary>Première ouverture de la fiche dans l'application mobile.</summary>
    public DateTime? OpenedAt { get; set; }
    /// <summary>Source qui suit la tournée : device | phone | none (TrackingSourceSelector).</summary>
    public string? TrackingSource { get; set; }
    public DateTime? TrackingSourceSince { get; set; }

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

    // ── Déclarations du chauffeur et source de validation (migration 051) ──
    /// <summary>« Je suis arrivé » (heure du téléphone, bornée) ; à côté de l'heure détectée, jamais à sa place.</summary>
    public DateTime? DriverArrivedAt { get; set; }
    /// <summary>« Je repars ».</summary>
    public DateTime? DriverDepartedAt { get; set; }
    /// <summary>Départ de l'étape retenu (détecté, sinon déclaré).</summary>
    public DateTime? ActualDepartureTime { get; set; }
    /// <summary>Qui a validé l'étape : device | phone | geofence | driver | manager (DriverTourRules).</summary>
    public string? ArrivalSource { get; set; }
    /// <summary>Distance à l'étape (m) au moment de la déclaration, mesurée par le boîtier ou le téléphone.</summary>
    public int? DriverDeclarationDistanceM { get; set; }
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
