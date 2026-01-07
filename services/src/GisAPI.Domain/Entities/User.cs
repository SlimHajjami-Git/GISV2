using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class User : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string[] Roles { get; set; } = [];
    public string[] Permissions { get; set; } = [];
    public int[] AssignedVehicleIds { get; set; } = [];
    public string Status { get; set; } = "active";
    public DateTime? LastLoginAt { get; set; }

    public Company? Company { get; set; }
    
    public int? UserSettingsId { get; set; }
    public UserSettings? Settings { get; set; }
}

public class UserSettings : Entity
{
    public string Language { get; set; } = "fr";
    public string Timezone { get; set; } = "Africa/Casablanca";
    public string Currency { get; set; } = "MAD";
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public string DistanceUnit { get; set; } = "km";
    public string SpeedUnit { get; set; } = "kmh";
    public string VolumeUnit { get; set; } = "L";
    public string TemperatureUnit { get; set; } = "C";
    public NotificationSettings? Notifications { get; set; }
    public DisplaySettings? Display { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class NotificationSettings
{
    public bool Speeding { get; set; } = true;
    public bool Geofence { get; set; } = true;
    public bool Idling { get; set; } = true;
    public bool Maintenance { get; set; } = true;
    public bool Push { get; set; } = true;
    public bool Email { get; set; } = true;
    public bool Sms { get; set; } = false;
    public bool QuietHours { get; set; } = false;
    public string QuietStart { get; set; } = "22:00";
    public string QuietEnd { get; set; } = "07:00";
}

public class DisplaySettings
{
    public string Theme { get; set; } = "light";
    public string MapStyle { get; set; } = "streets";
    public bool ShowVehicleLabels { get; set; } = true;
    public bool Clustering { get; set; } = true;
    public int RefreshInterval { get; set; } = 30;
    public bool Animations { get; set; } = true;
}
