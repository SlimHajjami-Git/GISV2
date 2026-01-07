using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class DriverAssignment
{
    [Key]
    public int Id { get; set; }

    public int VehicleId { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    public int DriverId { get; set; }

    [ForeignKey("DriverId")]
    public Employee? Driver { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public int? AssignedByUserId { get; set; }

    [ForeignKey("AssignedByUserId")]
    public User? AssignedByUser { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "active"; // active, ended, cancelled

    [MaxLength(50)]
    public string AssignmentType { get; set; } = "permanent"; // permanent, temporary, shift

    [MaxLength(500)]
    public string? Notes { get; set; }

    // Mileage at assignment
    public int? StartMileage { get; set; }

    public int? EndMileage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class DriverScore
{
    [Key]
    public int Id { get; set; }

    public int DriverId { get; set; }

    [ForeignKey("DriverId")]
    public Employee? Driver { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public DateOnly Date { get; set; }

    // Overall score (0-100)
    public int OverallScore { get; set; }

    // Component scores (0-100)
    public int SpeedingScore { get; set; }

    public int BrakingScore { get; set; }

    public int AccelerationScore { get; set; }

    public int IdlingScore { get; set; }

    public int FuelEfficiencyScore { get; set; }

    // Event counts
    public int SpeedingEvents { get; set; }

    public int HarshBrakingEvents { get; set; }

    public int HarshAccelerationEvents { get; set; }

    public int IdlingEvents { get; set; }

    // Distance driven
    [Column(TypeName = "decimal(10,2)")]
    public decimal DistanceKm { get; set; }

    public int DrivingTimeMinutes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
