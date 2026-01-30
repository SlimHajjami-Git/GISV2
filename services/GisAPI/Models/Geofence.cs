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

    [MaxLength(50)]
    public string? IconName { get; set; } // Lucide icon name (warehouse, home, building, etc.)

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

    public int NotificationCooldownMinutes { get; set; } = 5; // Avoid spam notifications

    public int? MaxStayDurationMinutes { get; set; } // Alert if vehicle stays too long

    // Time-based activation (optional)
    public TimeSpan? ActiveStartTime { get; set; } // Zone active from (e.g., 08:00)

    public TimeSpan? ActiveEndTime { get; set; } // Zone active until (e.g., 18:00)

    [Column(TypeName = "jsonb")]
    public string[]? ActiveDays { get; set; } // ["Mon","Tue","Wed","Thu","Fri"]

    public bool IsActive { get; set; } = true;

    // Optional grouping
    public int? GroupId { get; set; }

    [ForeignKey("GroupId")]
    public GeofenceGroup? Group { get; set; }

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

    public int? DeviceId { get; set; }

    [Required]
    [MaxLength(30)]
    public string Type { get; set; } = string.Empty; // entry, exit, speed_violation, overstay

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    public double? Speed { get; set; }

    public int? DurationInsideSeconds { get; set; } // For exit events - time spent inside

    public bool IsNotified { get; set; } = false;

    public DateTime? NotifiedAt { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class GeofenceGroup
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(20)]
    public string Color { get; set; } = "#6b7280";

    [MaxLength(50)]
    public string? IconName { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Geofence> Geofences { get; set; } = new List<Geofence>();
}



