using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class Contract
{
    [Key]
    public int Id { get; set; }

    public int VehicleId { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    [Required]
    [MaxLength(100)]
    public string ContractNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty; // lease, rental, purchase, insurance

    [MaxLength(200)]
    public string? Provider { get; set; }

    [MaxLength(100)]
    public string? ProviderContact { get; set; }

    [MaxLength(20)]
    public string? ProviderPhone { get; set; }

    [MaxLength(100)]
    public string? ProviderEmail { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    [Column(TypeName = "decimal(12,2)")]
    public decimal TotalValue { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? MonthlyPayment { get; set; }

    [MaxLength(20)]
    public string PaymentFrequency { get; set; } = "monthly"; // monthly, quarterly, yearly, one_time

    // For leasing
    public int? AllowedKmPerYear { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? ExcessKmRate { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? ResidualValue { get; set; }

    // For insurance
    [MaxLength(100)]
    public string? PolicyNumber { get; set; }

    [MaxLength(50)]
    public string? CoverageType { get; set; } // comprehensive, third_party, all_risk

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Deductible { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "active"; // draft, active, expired, cancelled, renewed

    [MaxLength(500)]
    public string? DocumentUrl { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    // Renewal reminder
    public int? ReminderDaysBefore { get; set; }

    public bool ReminderSent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Reservation
{
    [Key]
    public int Id { get; set; }

    public int VehicleId { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public int? RequestedByUserId { get; set; }

    [ForeignKey("RequestedByUserId")]
    public User? RequestedByUser { get; set; }

    public int? AssignedDriverId { get; set; }

    [ForeignKey("AssignedDriverId")]
    public Employee? AssignedDriver { get; set; }

    [MaxLength(200)]
    public string? Purpose { get; set; }

    [MaxLength(500)]
    public string? Destination { get; set; }

    public DateTime StartDateTime { get; set; }

    public DateTime EndDateTime { get; set; }

    public int? EstimatedKm { get; set; }

    public int? ActualKm { get; set; }

    public int? StartMileage { get; set; }

    public int? EndMileage { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "pending"; // pending, approved, rejected, in_progress, completed, cancelled

    public int? ApprovedByUserId { get; set; }

    [ForeignKey("ApprovedByUserId")]
    public User? ApprovedByUser { get; set; }

    public DateTime? ApprovedAt { get; set; }

    [MaxLength(500)]
    public string? RejectionReason { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
