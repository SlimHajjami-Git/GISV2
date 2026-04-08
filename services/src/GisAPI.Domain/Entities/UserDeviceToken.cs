using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class UserDeviceToken : Entity
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public bool IsActive { get; set; } = true;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}
