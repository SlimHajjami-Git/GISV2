using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class Supplier
{
    [Key]
    public int Id { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Type { get; set; } = "general"; // parts, fuel, tires, service, general

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(100)]
    public string? ContactName { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(100)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Website { get; set; }

    [MaxLength(100)]
    public string? TaxId { get; set; }

    [MaxLength(100)]
    public string? BankAccount { get; set; }

    [MaxLength(20)]
    public string PaymentTerms { get; set; } = "net30"; // immediate, net15, net30, net60

    [Column(TypeName = "decimal(5,2)")]
    public decimal? DiscountPercent { get; set; }

    public int? Rating { get; set; } // 1-5

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<PartInventory> Parts { get; set; } = new List<PartInventory>();
}

public class PartInventory
{
    [Key]
    public int Id { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public int? SupplierId { get; set; }

    [ForeignKey("SupplierId")]
    public Supplier? Supplier { get; set; }

    [Required]
    [MaxLength(100)]
    public string PartNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string Category { get; set; } = "general"; // oil, filter, brake, tire, battery, belt, fluid, general

    [MaxLength(100)]
    public string? Brand { get; set; }

    [MaxLength(50)]
    public string? Unit { get; set; } // piece, liter, kg, meter

    public int QuantityInStock { get; set; }

    public int MinimumStock { get; set; }

    public int? ReorderQuantity { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal UnitCost { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? SellingPrice { get; set; }

    [MaxLength(100)]
    public string? Location { get; set; } // Warehouse location

    // Compatible vehicles (JSON array of vehicle types/models)
    public string[]? CompatibleVehicles { get; set; }

    public DateTime? LastRestockDate { get; set; }

    public DateTime? ExpiryDate { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class PartTransaction
{
    [Key]
    public int Id { get; set; }

    public int PartId { get; set; }

    [ForeignKey("PartId")]
    public PartInventory? Part { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    [Required]
    [MaxLength(20)]
    public string Type { get; set; } = string.Empty; // in, out, adjustment, return

    public int Quantity { get; set; }

    public int QuantityBefore { get; set; }

    public int QuantityAfter { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? UnitCost { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? TotalCost { get; set; }

    // Reference to related entity
    public int? MaintenanceRecordId { get; set; }

    [ForeignKey("MaintenanceRecordId")]
    public MaintenanceRecord? MaintenanceRecord { get; set; }

    public int? VehicleId { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    public int? SupplierId { get; set; }

    [ForeignKey("SupplierId")]
    public Supplier? Supplier { get; set; }

    [MaxLength(100)]
    public string? ReferenceNumber { get; set; } // PO number, invoice number

    [MaxLength(500)]
    public string? Notes { get; set; }

    public int? CreatedByUserId { get; set; }

    [ForeignKey("CreatedByUserId")]
    public User? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
