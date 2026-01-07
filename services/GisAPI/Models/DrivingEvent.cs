using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class DrivingEvent
{
    [Key]
    public long Id { get; set; }

    public int VehicleId { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    public int? DriverId { get; set; }

    [ForeignKey("DriverId")]
    public Employee? Driver { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public long? TripId { get; set; }

    [ForeignKey("TripId")]
    public Trip? Trip { get; set; }

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty; // harsh_braking, harsh_acceleration, sharp_turn, speeding, excessive_idle, fatigue

    [MaxLength(20)]
    public string Severity { get; set; } = "medium"; // low, medium, high

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    public DateTime Timestamp { get; set; }

    // Event specific data
    public double? SpeedKph { get; set; }

    public double? SpeedLimitKph { get; set; }

    public double? GForce { get; set; }

    public int? DurationSeconds { get; set; }

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Metadata { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
