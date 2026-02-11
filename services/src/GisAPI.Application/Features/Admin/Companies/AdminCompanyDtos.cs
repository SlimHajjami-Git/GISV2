namespace GisAPI.Application.Features.Admin.Companies;

public class AdminCompanyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Type { get; set; } = "transport";
    public int? SubscriptionId { get; set; }
    public string? SubscriptionName { get; set; }
    public int MaxVehicles { get; set; }
    public int CurrentVehicles { get; set; }
    public int CurrentUsers { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastActivity { get; set; }
    public string SubscriptionStatus { get; set; } = "active";
    public DateTime SubscriptionStartedAt { get; set; }
    public DateTime? SubscriptionExpiresAt { get; set; }
    public string BillingCycle { get; set; } = "yearly";
    public decimal? NextPaymentAmount { get; set; }
    public DateTime? LastPaymentAt { get; set; }
    public int? DaysUntilExpiration { get; set; }
}

public class AdminRoleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string RoleType { get; set; } = "employee";
    public Dictionary<string, object>? Permissions { get; set; }
    public bool IsSystem { get; set; }
    public bool IsDefault { get; set; }
    public int UserCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
