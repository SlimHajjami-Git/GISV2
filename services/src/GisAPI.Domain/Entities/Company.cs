using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Company : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "transport";
    public string? Address { get; set; }
    public string? City { get; set; }
    public string Country { get; set; } = "TN";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public CompanySettings? Settings { get; set; }
    
    public string? LogoUrl { get; set; }
    public string? TaxId { get; set; }
    public string? RC { get; set; }
    public string? IF { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? SubscriptionExpiresAt { get; set; }
    
    public int SubscriptionId { get; set; }
    public Subscription? Subscription { get; set; }

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
    public string Currency { get; set; } = "DT";
    public string Timezone { get; set; } = "Africa/Tunis";
    public string Language { get; set; } = "fr";
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public string DistanceUnit { get; set; } = "km";
    public string SpeedUnit { get; set; } = "kmh";
    public string VolumeUnit { get; set; } = "L";
}
