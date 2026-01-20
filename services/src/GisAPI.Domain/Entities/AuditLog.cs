using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class AuditLog : Entity
{
    public new long Id { get; set; }
    public int? UserId { get; set; }
    public User? User { get; set; }
    public int? CompanyId { get; set; }
    public Societe? Societe { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? EntityName { get; set; }
    public Dictionary<string, object>? OldValues { get; set; }
    public Dictionary<string, object>? NewValues { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Description { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
