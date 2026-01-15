using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class Subscription
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = "parc"; // parc, parc_gps, parc_gps_install

    [Column(TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    public string[] Features { get; set; } = Array.Empty<string>();

    public bool GpsTracking { get; set; }

    public bool GpsInstallation { get; set; }

    public int MaxVehicles { get; set; } = 10;

    public int MaxUsers { get; set; } = 5;

    public int MaxGpsDevices { get; set; } = 10;

    public int MaxGeofences { get; set; } = 20;

    [MaxLength(20)]
    public string BillingCycle { get; set; } = "monthly"; // monthly, quarterly, yearly

    /// <summary>
    /// Subscription status: active, expired, pending_renewal, cancelled, suspended
    /// </summary>
    [MaxLength(30)]
    public string Status { get; set; } = "active";

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Auto-renewal enabled
    /// </summary>
    public bool AutoRenew { get; set; } = true;

    /// <summary>
    /// Days before expiration to send renewal notification
    /// </summary>
    public int RenewalReminderDays { get; set; } = 7;

    /// <summary>
    /// Last renewal notification sent date
    /// </summary>
    public DateTime? LastRenewalNotificationAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Subscription Type relationship
    public int? SubscriptionTypeId { get; set; }

    [ForeignKey("SubscriptionTypeId")]
    public SubscriptionType? SubscriptionType { get; set; }

    // Custom access rights (overrides SubscriptionType defaults if set)
    [Column(TypeName = "jsonb")]
    public CampaignAccessRights? AccessRights { get; set; }

    // Navigation
    public ICollection<Company> Companies { get; set; } = new List<Company>();
}

/// <summary>
/// Subscription status enum
/// </summary>
public static class SubscriptionStatus
{
    public const string Active = "active";
    public const string Expired = "expired";
    public const string PendingRenewal = "pending_renewal";
    public const string Cancelled = "cancelled";
    public const string Suspended = "suspended";
    public const string Trial = "trial";
}
