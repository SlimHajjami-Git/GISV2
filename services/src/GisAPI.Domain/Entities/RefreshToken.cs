using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class RefreshToken : Entity
{
    public string Token { get; set; } = string.Empty;
    public int UserId { get; set; }
    public User? User { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }
    public string? CreatedByIp { get; set; }
    public string? ReplacedByToken { get; set; }
    public string? DeviceInfo { get; set; }
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt != null;
    public bool IsActive => !IsRevoked && !IsExpired;
}


