using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class MaintenanceRecord
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
    [MaxLength(50)]
    public string Type { get; set; } = "scheduled"; // scheduled, repair, inspection, tire_change, oil_change, other

    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    public int MileageAtService { get; set; }

    public DateTime Date { get; set; }

    public DateTime? NextServiceDate { get; set; }

    public int? NextServiceMileage { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "scheduled"; // scheduled, in_progress, completed, cancelled

    [Column(TypeName = "decimal(10,2)")]
    public decimal LaborCost { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal PartsCost { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal TotalCost { get; set; }

    [MaxLength(200)]
    public string? ServiceProvider { get; set; }

    [MaxLength(100)]
    public string? ProviderContact { get; set; }

    [MaxLength(100)]
    public string? InvoiceNumber { get; set; }

    [MaxLength(500)]
    public string? InvoiceUrl { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<MaintenancePart> Parts { get; set; } = new List<MaintenancePart>();
}

public class MaintenancePart
{
    [Key]
    public int Id { get; set; }

    public int MaintenanceRecordId { get; set; }

    [ForeignKey("MaintenanceRecordId")]
    public MaintenanceRecord? MaintenanceRecord { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? PartNumber { get; set; }

    public int Quantity { get; set; } = 1;

    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitCost { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal TotalCost { get; set; }
}



