using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class User : TenantEntity
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? PermitNumber { get; set; }
    public int RoleId { get; set; }
    public string Status { get; set; } = "active";
    public DateTime? LastLoginAt { get; set; }

    // Navigation
    public Role Role { get; set; } = null!;
    public Societe Societe { get; set; } = null!;
    public ICollection<UserVehicle> UserVehicles { get; set; } = new List<UserVehicle>();

    // Computed properties (not stored in DB)
    public string FullName => $"{FirstName} {LastName}".Trim();
    public bool IsCompanyAdmin => Role?.IsCompanyAdmin ?? false;
    public bool IsSystemAdmin => Role?.IsSystemAdmin ?? false;
    public bool IsAnyAdmin => IsSystemAdmin || IsCompanyAdmin;
    
    // Legacy compatibility - getters return from Role, setters are no-op (for object initializers)
    private string[] _rolesCache = Array.Empty<string>();
    public string[] Roles { get => Role != null ? new[] { Role.Name } : _rolesCache; set => _rolesCache = value ?? Array.Empty<string>(); }
    
    private string[] _permissionsCache = Array.Empty<string>();
    public string[] Permissions { get => Role?.Permissions?.Where(p => p.Value is bool b && b).Select(p => p.Key).ToArray() ?? _permissionsCache; set => _permissionsCache = value ?? Array.Empty<string>(); }
    
    private int[] _assignedVehicleIdsCache = Array.Empty<int>();
    public int[] AssignedVehicleIds { get => UserVehicles?.Any() == true ? UserVehicles.Select(uv => uv.VehicleId).ToArray() : _assignedVehicleIdsCache; set => _assignedVehicleIdsCache = value ?? Array.Empty<int>(); }
    
    private string? _userTypeCache;
    public string UserType { get => _userTypeCache ?? (Role?.IsCompanyAdmin == true ? "company_admin" : "employee"); set => _userTypeCache = value; }
    
    // Legacy setter for Name (splits into FirstName/LastName)
    public string Name { get => FullName; set { var parts = (value ?? "").Split(' ', 2); FirstName = parts.Length > 0 ? parts[0] : ""; LastName = parts.Length > 1 ? parts[1] : ""; } }
    
    // Legacy nullable properties (not stored in DB, for backwards compatibility)
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? CIN { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DateTime? DateOfBirth { get; set; }
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


