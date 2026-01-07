using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class Company
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Type { get; set; } = "transport"; // location, transport, other

    [MaxLength(200)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(10)]
    public string Country { get; set; } = "MA";

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? LogoUrl { get; set; }

    [MaxLength(50)]
    public string? TaxId { get; set; } // ICE

    [MaxLength(50)]
    public string? RC { get; set; } // Registre de commerce

    [MaxLength(50)]
    public string? IF { get; set; } // Identifiant fiscal

    public bool IsActive { get; set; } = true;

    public DateTime? SubscriptionExpiresAt { get; set; }

    // Settings stored as JSON
    [Column(TypeName = "jsonb")]
    public CompanySettings? Settings { get; set; }

    public int SubscriptionId { get; set; }

    [ForeignKey("SubscriptionId")]
    public Subscription? Subscription { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    public ICollection<Geofence> Geofences { get; set; } = new List<Geofence>();
    public ICollection<GpsDevice> GpsDevices { get; set; } = new List<GpsDevice>();
    public ICollection<PointOfInterest> PointsOfInterest { get; set; } = new List<PointOfInterest>();
    public ICollection<Report> Reports { get; set; } = new List<Report>();
    public ICollection<ReportSchedule> ReportSchedules { get; set; } = new List<ReportSchedule>();
    public ICollection<Supplier> Suppliers { get; set; } = new List<Supplier>();
}

public class CompanySettings
{
    public string Currency { get; set; } = "MAD";
    public string Timezone { get; set; } = "Africa/Casablanca";
    public string Language { get; set; } = "fr";
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public string DistanceUnit { get; set; } = "km";
    public string SpeedUnit { get; set; } = "kmh";
    public string VolumeUnit { get; set; } = "L";
}
