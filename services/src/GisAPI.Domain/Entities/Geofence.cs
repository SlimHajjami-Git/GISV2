using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Geofence : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = "polygon";
    public string Color { get; set; } = "#3b82f6";
    public string? IconName { get; set; }
    public GeofencePoint[]? Coordinates { get; set; }
    public double? CenterLat { get; set; }
    public double? CenterLng { get; set; }
    public double? Radius { get; set; }
    public bool AlertOnEntry { get; set; } = true;
    public bool AlertOnExit { get; set; } = true;
    public int? AlertSpeedLimit { get; set; }
    public int NotificationCooldownMinutes { get; set; } = 5;
    public int? MaxStayDurationMinutes { get; set; }
    public TimeSpan? ActiveStartTime { get; set; }
    public TimeSpan? ActiveEndTime { get; set; }
    public string[]? ActiveDays { get; set; }
    public bool IsActive { get; set; } = true;
    public int? GroupId { get; set; }
    public GeofenceGroup? Group { get; set; }

    public Company? Company { get; set; }
    
    public ICollection<GeofenceVehicle> AssignedVehicles { get; set; } = new List<GeofenceVehicle>();
    public ICollection<GeofenceEvent> Events { get; set; } = new List<GeofenceEvent>();
}

public class GeofencePoint
{
    public double Lat { get; set; }
    public double Lng { get; set; }
}

public class GeofenceVehicle
{
    public int GeofenceId { get; set; }
    public Geofence? Geofence { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

public class GeofenceEvent : Entity
{
    public int GeofenceId { get; set; }
    public Geofence? Geofence { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public int? DeviceId { get; set; }
    public string Type { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public double? Speed { get; set; }
    public int? DurationInsideSeconds { get; set; }
    public bool IsNotified { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class GeofenceGroup : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Color { get; set; } = "#6b7280";
    public string? IconName { get; set; }
    public Company? Company { get; set; }
    public ICollection<Geofence> Geofences { get; set; } = new List<Geofence>();
}
