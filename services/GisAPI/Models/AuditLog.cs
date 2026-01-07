using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class AuditLog
{
    [Key]
    public long Id { get; set; }

    public int? UserId { get; set; }

    [ForeignKey("UserId")]
    public User? User { get; set; }

    public int? CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    [Required]
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty; // create, update, delete, login, logout, export, etc.

    [Required]
    [MaxLength(100)]
    public string EntityType { get; set; } = string.Empty; // Vehicle, User, GpsDevice, etc.

    public int? EntityId { get; set; }

    [MaxLength(200)]
    public string? EntityName { get; set; }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? OldValues { get; set; }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? NewValues { get; set; }

    [MaxLength(50)]
    public string? IpAddress { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
