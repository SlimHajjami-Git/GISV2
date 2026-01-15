using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Campaign : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = "promotion";
    public string Status { get; set; } = "draft";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public int? MaxSubscriptions { get; set; }
    public int? TargetSubscriptionId { get; set; }
    public Subscription? TargetSubscription { get; set; }
    public int? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Company> Companies { get; set; } = new List<Company>();
}
