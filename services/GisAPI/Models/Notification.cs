using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class Notification
{
    [Key]
    public long Id { get; set; }

    public int UserId { get; set; }

    [ForeignKey("UserId")]
    public User? User { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty; // alert, maintenance, geofence, system, report

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Priority { get; set; } = "normal"; // low, normal, high, urgent

    [MaxLength(50)]
    public string Channel { get; set; } = "push"; // push, email, sms, in_app

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public bool IsSent { get; set; }

    public DateTime? SentAt { get; set; }

    // Reference to related entity
    [MaxLength(50)]
    public string? ReferenceType { get; set; } // vehicle, alert, maintenance, geofence, trip

    public int? ReferenceId { get; set; }

    [MaxLength(500)]
    public string? ActionUrl { get; set; }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }
}
