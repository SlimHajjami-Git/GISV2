using System.ComponentModel.DataAnnotations.Schema;
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
    public string? PermitType { get; set; }
    public DateTime? PermitExpiry { get; set; }
    public string? CIN { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? EmployeeRole { get; set; }
    public DateTime? HireDate { get; set; }
    public int RoleId { get; set; }
    public string Status { get; set; } = "active";
    public DateTime? LastLoginAt { get; set; }

    // Module permissions (per-user access control)
    public string AccessLevel { get; set; } = "user"; // "admin" or "user"
    public bool CanMonitoring { get; set; } = true;
    public bool CanVehicles { get; set; } = true;
    public bool CanDrivers { get; set; } = true;
    public bool CanReports { get; set; } = false;
    public bool CanGeofences { get; set; } = false;
    public bool CanMaintenance { get; set; } = false;
    public bool CanCosts { get; set; } = false;
    public bool CanFuel { get; set; } = false;
    public bool CanDocuments { get; set; } = false;
    public bool CanAccidents { get; set; } = false;
    public bool CanUsers { get; set; } = false;
    public bool CanSettings { get; set; } = false;
    public bool CanSuppliers { get; set; } = false;
    public bool CanFleetManagement { get; set; } = false;
    public bool CanTours { get; set; } = false;
    public bool CanPlayback { get; set; } = false;

    // Per-report permissions (granular access when CanReports is true)
    [Column("can_report_trips")]
    public bool CanReportTrips { get; set; } = true;
    [Column("can_report_fuel")]
    public bool CanReportFuel { get; set; } = true;
    [Column("can_report_speed")]
    public bool CanReportSpeed { get; set; } = true;
    [Column("can_report_stops")]
    public bool CanReportStops { get; set; } = true;
    [Column("can_report_mileage")]
    public bool CanReportMileage { get; set; } = true;
    [Column("can_report_costs")]
    public bool CanReportCosts { get; set; } = true;
    [Column("can_report_maintenance")]
    public bool CanReportMaintenance { get; set; } = true;
    [Column("can_report_daily")]
    public bool CanReportDaily { get; set; } = true;
    [Column("can_report_monthly")]
    public bool CanReportMonthly { get; set; } = true;
    [Column("can_report_mileage_period")]
    public bool CanReportMileagePeriod { get; set; } = true;
    [Column("can_report_speed_infraction")]
    public bool CanReportSpeedInfraction { get; set; } = true;
    [Column("can_report_driving_behavior")]
    public bool CanReportDrivingBehavior { get; set; } = true;
    [Column("can_report_monthly_costs")]
    public bool CanReportMonthlyCosts { get; set; } = true;

    // Alert email preferences
    [Column("alert_assurance")]
    public bool AlertAssurance { get; set; } = false;
    [Column("alert_taxe_circulation")]
    public bool AlertTaxeCirculation { get; set; } = false;
    [Column("alert_visite_technique")]
    public bool AlertVisiteTechnique { get; set; } = false;
    [Column("alert_entretien")]
    public bool AlertEntretien { get; set; } = false;
    /// <summary>
    /// Vrai dès qu'un enregistrement EXPLICITE des préférences d'alerte a eu
    /// lieu pour cet utilisateur. Distingue « la société n'a jamais configuré
    /// les alertes » (fallback legacy vers tous les admins) de « tout a été
    /// volontairement décoché » (vrai opt-out : aucun envoi).
    /// Voir AlertEmailDispatcher.
    /// </summary>
    [Column("alert_prefs_configured")]
    public bool AlertPrefsConfigured { get; set; } = false;
    [Column("daily_report_email_enabled")]
    public bool DailyReportEmailEnabled { get; set; } = false;

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
    
    // Helper: is this user a driver?
    public bool IsDriver => string.Equals(EmployeeRole, "driver", StringComparison.OrdinalIgnoreCase);
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


