using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class AiChatMessage : TenantEntity
{
    public int UserId { get; set; }
    public int VehicleId { get; set; }
    public string Role { get; set; } = "user"; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public int? TokensUsed { get; set; }

    // Navigation
    public User? User { get; set; }
    public Vehicle? Vehicle { get; set; }
}
