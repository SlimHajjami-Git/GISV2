using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class PointOfInterest : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? SubCategory { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Radius { get; set; } = 50;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? ContactName { get; set; }
    public string? ExternalId { get; set; }
    public OperatingHours? Hours { get; set; }
    public string Color { get; set; } = "#3b82f6";
    public string? Icon { get; set; }
    public bool AlertOnArrival { get; set; }
    public bool AlertOnDeparture { get; set; }
    public int? ExpectedStayMinutes { get; set; }
    public int NotificationCooldownMinutes { get; set; } = 5;
    public string[]? Tags { get; set; }
    public bool IsActive { get; set; } = true;
    public int VisitCount { get; set; }
    public DateTime? LastVisitAt { get; set; }
    public string? FuelBrand { get; set; }
    public bool? HasDiesel { get; set; }
    public bool? HasGasoline { get; set; }
    public bool? HasElectricCharging { get; set; }
    public Company? Company { get; set; }
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

public class PoiVisit : Entity
{
    public int PoiId { get; set; }
    public PointOfInterest? Poi { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public int? DeviceId { get; set; }
    public DateTime ArrivalAt { get; set; }
    public DateTime? DepartureAt { get; set; }
    public int? DurationMinutes { get; set; }
    public double ArrivalLat { get; set; }
    public double ArrivalLng { get; set; }
    public double? DepartureLat { get; set; }
    public double? DepartureLng { get; set; }
    public string? Notes { get; set; }
    public bool IsNotified { get; set; }
    public int CompanyId { get; set; }
}
