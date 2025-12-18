using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Phone { get; set; }

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    // Roles stored as array: ["driver", "supervisor"]
    public string[] Roles { get; set; } = Array.Empty<string>();

    // Page permissions: ["dashboard", "monitoring", "vehicles", ...]
    public string[] Permissions { get; set; } = Array.Empty<string>();

    // Assigned vehicle IDs
    public int[] AssignedVehicleIds { get; set; } = Array.Empty<int>();

    [MaxLength(20)]
    public string Status { get; set; } = "active"; // active, inactive

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    // User settings
    public int? UserSettingsId { get; set; }

    [ForeignKey("UserSettingsId")]
    public UserSettings? Settings { get; set; }
}

public class UserSettings
{
    [Key]
    public int Id { get; set; }

    [MaxLength(10)]
    public string Language { get; set; } = "fr";

    [MaxLength(50)]
    public string Timezone { get; set; } = "Africa/Casablanca";

    [MaxLength(10)]
    public string Currency { get; set; } = "MAD";

    [MaxLength(20)]
    public string DateFormat { get; set; } = "dd/MM/yyyy";

    [MaxLength(10)]
    public string DistanceUnit { get; set; } = "km";

    [MaxLength(10)]
    public string SpeedUnit { get; set; } = "kmh";

    [MaxLength(10)]
    public string VolumeUnit { get; set; } = "L";

    [MaxLength(5)]
    public string TemperatureUnit { get; set; } = "C";

    // Notification settings as JSON
    [Column(TypeName = "jsonb")]
    public NotificationSettings? Notifications { get; set; }

    // Display settings as JSON
    [Column(TypeName = "jsonb")]
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
