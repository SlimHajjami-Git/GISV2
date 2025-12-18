using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class Geofence
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(20)]
    public string Type { get; set; } = "polygon"; // polygon, circle

    [MaxLength(20)]
    public string Color { get; set; } = "#3b82f6";

    // For polygon type - stored as JSON array of {lat, lng}
    [Column(TypeName = "jsonb")]
    public GeofencePoint[]? Coordinates { get; set; }

    // For circle type
    public double? CenterLat { get; set; }

    public double? CenterLng { get; set; }

    public double? Radius { get; set; } // in meters

    // Alert settings
    public bool AlertOnEntry { get; set; } = true;

    public bool AlertOnExit { get; set; } = true;

    public int? AlertSpeedLimit { get; set; } // Speed limit inside geofence

    public bool IsActive { get; set; } = true;

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation - Many-to-many with vehicles
    public ICollection<GeofenceVehicle> AssignedVehicles { get; set; } = new List<GeofenceVehicle>();
    public ICollection<GeofenceEvent> Events { get; set; } = new List<GeofenceEvent>();
}

public class GeofencePoint
{
    public double Lat { get; set; }
    public double Lng { get; set; }
}

// Pivot table for Geofence <-> Vehicle many-to-many
public class GeofenceVehicle
{
    public int GeofenceId { get; set; }
    public Geofence? Geofence { get; set; }

    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

public class GeofenceEvent
{
    [Key]
    public int Id { get; set; }

    public int GeofenceId { get; set; }

    [ForeignKey("GeofenceId")]
    public Geofence? Geofence { get; set; }

    public int VehicleId { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    [Required]
    [MaxLength(30)]
    public string Type { get; set; } = string.Empty; // entry, exit, speed_violation

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double? Speed { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
