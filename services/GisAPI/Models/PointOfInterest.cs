using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class PointOfInterest
{
    [Key]
    public int Id { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = string.Empty; // fuel_station, garage, parking, warehouse, client, supplier, checkpoint, other

    [MaxLength(50)]
    public string? SubCategory { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double Radius { get; set; } = 50; // Detection radius in meters

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Website { get; set; }

    // Contact info for clients/suppliers
    [MaxLength(100)]
    public string? ContactName { get; set; }

    [MaxLength(100)]
    public string? ExternalId { get; set; } // ID in external system (ERP, CRM)

    // Operating hours (JSON)
    [Column(TypeName = "jsonb")]
    public OperatingHours? Hours { get; set; }

    [MaxLength(20)]
    public string Color { get; set; } = "#3b82f6";

    [MaxLength(50)]
    public string? Icon { get; set; }

    // Alert settings
    public bool AlertOnArrival { get; set; } = false;

    public bool AlertOnDeparture { get; set; } = false;

    public int? ExpectedStayMinutes { get; set; } // Expected duration for deliveries

    public int NotificationCooldownMinutes { get; set; } = 5;

    // Tags for filtering
    [Column(TypeName = "jsonb")]
    public string[]? Tags { get; set; } // ["prioritaire", "24h", "fragile"]

    public bool IsActive { get; set; } = true;

    // Visit statistics
    public int VisitCount { get; set; } = 0;

    public DateTime? LastVisitAt { get; set; }

    // For fuel stations
    [MaxLength(50)]
    public string? FuelBrand { get; set; }

    public bool? HasDiesel { get; set; }

    public bool? HasGasoline { get; set; }

    public bool? HasElectricCharging { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<PoiVisit> Visits { get; set; } = new List<PoiVisit>();
}

public class OperatingHours
{
    public DayHours? Monday { get; set; }
    public DayHours? Tuesday { get; set; }
    public DayHours? Wednesday { get; set; }
    public DayHours? Thursday { get; set; }
    public DayHours? Friday { get; set; }
    public DayHours? Saturday { get; set; }
    public DayHours? Sunday { get; set; }
    public bool Is24Hours { get; set; }
}

public class DayHours
{
    public string? Open { get; set; }
    public string? Close { get; set; }
    public bool IsClosed { get; set; }
}

public class PoiVisit
{
    [Key]
    public int Id { get; set; }

    public int PoiId { get; set; }

    [ForeignKey("PoiId")]
    public PointOfInterest? Poi { get; set; }

    public int VehicleId { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    public int? DeviceId { get; set; }

    public DateTime ArrivalAt { get; set; }

    public DateTime? DepartureAt { get; set; }

    public int? DurationMinutes { get; set; }

    public double ArrivalLat { get; set; }

    public double ArrivalLng { get; set; }

    public double? DepartureLat { get; set; }

    public double? DepartureLng { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsNotified { get; set; } = false;

    public int CompanyId { get; set; }
}



