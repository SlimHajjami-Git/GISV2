using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Societe : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "transport"; // transport, location, autre
    public string? Description { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string Country { get; set; } = "TN";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public SocieteSettings? Settings { get; set; }
    
    public string? LogoUrl { get; set; }
    public string? TaxId { get; set; }
    public string? RC { get; set; }
    public string? IF { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Subscription dates and status
    public DateTime SubscriptionStartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubscriptionExpiresAt { get; set; }
    public string BillingCycle { get; set; } = "yearly";
    public string SubscriptionStatus { get; set; } = "active";
    public DateTime? LastPaymentAt { get; set; }
    public decimal? NextPaymentAmount { get; set; }
    
    // Subscription relation (direct link to subscription type/plan)
    public int? SubscriptionTypeId { get; set; }
    public SubscriptionType? SubscriptionType { get; set; }

    // Navigation collections
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Role> Roles { get; set; } = new List<Role>();
    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
    public ICollection<Geofence> Geofences { get; set; } = new List<Geofence>();
    public ICollection<GpsDevice> GpsDevices { get; set; } = new List<GpsDevice>();
    public ICollection<PointOfInterest> PointsOfInterest { get; set; } = new List<PointOfInterest>();
    public ICollection<Report> Reports { get; set; } = new List<Report>();
    public ICollection<ReportSchedule> ReportSchedules { get; set; } = new List<ReportSchedule>();
    public ICollection<Supplier> Suppliers { get; set; } = new List<Supplier>();
}

public class SocieteSettings
{
    public string Currency { get; set; } = "DT";
    public string Timezone { get; set; } = "Africa/Tunis";
    public string Language { get; set; } = "fr";
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public string DistanceUnit { get; set; } = "km";
    public string SpeedUnit { get; set; } = "kmh";
    public string VolumeUnit { get; set; } = "L";
}



