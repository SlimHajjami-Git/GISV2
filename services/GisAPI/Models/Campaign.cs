using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

/// <summary>
/// Represents a marketing/sales campaign for client subscription management
/// </summary>
public class Campaign
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = "standard"; // standard, promotional, enterprise, trial

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "draft"; // draft, active, paused, ended

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal? DiscountPercentage { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? DiscountAmount { get; set; }

    public int? MaxSubscriptions { get; set; }

    public int CurrentSubscriptions { get; set; } = 0;

    // Target subscription type for this campaign
    public int? TargetSubscriptionId { get; set; }

    [ForeignKey("TargetSubscriptionId")]
    public Subscription? TargetSubscription { get; set; }

    // Access rights configuration (JSON stored)
    [Column(TypeName = "jsonb")]
    public CampaignAccessRights? AccessRights { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int? CreatedById { get; set; }

    [ForeignKey("CreatedById")]
    public User? CreatedBy { get; set; }

    // Navigation - Companies enrolled in this campaign
    public ICollection<Company> Companies { get; set; } = new List<Company>();
}

/// <summary>
/// Access rights configuration for a campaign - defines which features/pages are accessible
/// </summary>
public class CampaignAccessRights
{
    // Core application features
    public bool Dashboard { get; set; } = true;
    public bool Monitoring { get; set; } = true;
    public bool Vehicles { get; set; } = true;
    public bool Employees { get; set; } = true;
    public bool GpsDevices { get; set; } = true;
    public bool Maintenance { get; set; } = false;
    public bool Costs { get; set; } = false;
    public bool Reports { get; set; } = false;
    public bool Geofences { get; set; } = false;
    public bool Notifications { get; set; } = true;
    public bool Settings { get; set; } = true;
    public bool Users { get; set; } = true;

    // Advanced features
    public bool ApiAccess { get; set; } = false;
    public bool CustomBranding { get; set; } = false;
    public bool AdvancedReports { get; set; } = false;
    public bool RealTimeAlerts { get; set; } = true;
    public bool HistoryPlayback { get; set; } = true;
    public bool FuelAnalysis { get; set; } = false;
    public bool DrivingBehavior { get; set; } = false;
    public bool MultiCompany { get; set; } = false;

    // Limits
    public int MaxVehicles { get; set; } = 10;
    public int MaxUsers { get; set; } = 5;
    public int MaxGpsDevices { get; set; } = 10;
    public int MaxGeofences { get; set; } = 20;
    public int HistoryRetentionDays { get; set; } = 30;
}



