using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class SubscriptionType : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TargetCompanyType { get; set; } = "all";

    // Pricing
    public decimal MonthlyPrice { get; set; }
    public decimal QuarterlyPrice { get; set; }
    public decimal YearlyPrice { get; set; }

    // Duration in days for each billing cycle
    public int MonthlyDurationDays { get; set; } = 30;
    public int QuarterlyDurationDays { get; set; } = 90;
    public int YearlyDurationDays { get; set; } = 365;

    // Limits
    public int MaxVehicles { get; set; } = 10;
    public int MaxUsers { get; set; } = 5;
    public int MaxGpsDevices { get; set; } = 10;
    public int MaxGeofences { get; set; } = 20;

    // Features
    public bool GpsTracking { get; set; }
    public bool GpsInstallation { get; set; }
    public bool ApiAccess { get; set; }
    public bool AdvancedReports { get; set; }
    public bool RealTimeAlerts { get; set; } = true;
    public bool HistoryPlayback { get; set; } = true;
    public bool FuelAnalysis { get; set; }
    public bool DrivingBehavior { get; set; }

    public int HistoryRetentionDays { get; set; } = 30;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Access rights for features (JSONB in PostgreSQL)
    public Dictionary<string, object>? AccessRights { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Societe> Societes { get; set; } = new List<Societe>();
}
