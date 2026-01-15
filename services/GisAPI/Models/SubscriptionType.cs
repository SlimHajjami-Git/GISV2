using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

/// <summary>
/// Defines subscription types/tiers with default access rights configuration
/// </summary>
public class SubscriptionType
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty; // parc, parc_gps, parc_gps_install, enterprise

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Target company types: transport, location, autre, all
    /// </summary>
    [MaxLength(50)]
    public string TargetCompanyType { get; set; } = "all";

    // Pricing
    [Column(TypeName = "decimal(10,2)")]
    public decimal MonthlyPrice { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal QuarterlyPrice { get; set; }

    [Column(TypeName = "decimal(10,2)")]
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
    public bool GpsTracking { get; set; } = false;
    public bool GpsInstallation { get; set; } = false;
    public bool ApiAccess { get; set; } = false;
    public bool AdvancedReports { get; set; } = false;
    public bool RealTimeAlerts { get; set; } = true;
    public bool HistoryPlayback { get; set; } = true;
    public bool FuelAnalysis { get; set; } = false;
    public bool DrivingBehavior { get; set; } = false;

    public int HistoryRetentionDays { get; set; } = 30;

    public int SortOrder { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    // Default access rights for this subscription type
    [Column(TypeName = "jsonb")]
    public CampaignAccessRights? DefaultAccessRights { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}

/// <summary>
/// Billing cycle options
/// </summary>
public enum BillingCycle
{
    Monthly,
    Quarterly,
    Yearly
}
