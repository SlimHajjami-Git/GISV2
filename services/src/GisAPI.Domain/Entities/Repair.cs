using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Domain.Entities;

[Table("repairs")]
public class Repair
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("societe_id")]
    public int SocieteId { get; set; }

    [Column("vehicle_id")]
    public int VehicleId { get; set; }

    [Column("supplier_id")]
    public int? SupplierId { get; set; }

    [Required]
    [MaxLength(100)]
    [Column("reference")]
    public string Reference { get; set; } = string.Empty;

    [MaxLength(500)]
    [Column("description")]
    public string? Description { get; set; }

    [Column("repair_date")]
    public DateTime RepairDate { get; set; }

    [Column("mileage_at_repair")]
    public int? MileageAtRepair { get; set; }

    [Column("labor_cost", TypeName = "decimal(10,2)")]
    public decimal LaborCost { get; set; }

    [Column("parts_cost", TypeName = "decimal(10,2)")]
    public decimal PartsCost { get; set; }

    [Column("total_cost", TypeName = "decimal(10,2)")]
    public decimal TotalCost { get; set; }

    [MaxLength(50)]
    [Column("status")]
    public string Status { get; set; } = "completed"; // pending, in_progress, completed, cancelled

    [MaxLength(100)]
    [Column("invoice_number")]
    public string? InvoiceNumber { get; set; }

    [MaxLength(1000)]
    [Column("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("created_by")]
    public int? CreatedBy { get; set; }

    // Navigation properties
    [ForeignKey("SocieteId")]
    public virtual Societe? Societe { get; set; }

    [ForeignKey("VehicleId")]
    public virtual Vehicle? Vehicle { get; set; }

    public virtual ICollection<RepairPart> Parts { get; set; } = new List<RepairPart>();
}

[Table("repair_parts")]
public class RepairPart
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("repair_id")]
    public int RepairId { get; set; }

    [Required]
    [MaxLength(200)]
    [Column("part_name")]
    public string PartName { get; set; } = string.Empty;

    [MaxLength(100)]
    [Column("part_reference")]
    public string? PartReference { get; set; }

    [Column("quantity")]
    public int Quantity { get; set; } = 1;

    [Column("unit_price", TypeName = "decimal(10,2)")]
    public decimal UnitPrice { get; set; }

    [Column("subtotal", TypeName = "decimal(10,2)")]
    public decimal Subtotal { get; set; }

    [MaxLength(500)]
    [Column("notes")]
    public string? Notes { get; set; }

    // Navigation property
    [ForeignKey("RepairId")]
    public virtual Repair? Repair { get; set; }
}
