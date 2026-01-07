using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Supplier : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "general";
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? TaxId { get; set; }
    public string? BankAccount { get; set; }
    public string PaymentTerms { get; set; } = "net30";
    public decimal? DiscountPercent { get; set; }
    public int? Rating { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<PartInventory> Parts { get; set; } = new List<PartInventory>();
}

public class PartInventory : TenantEntity
{
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = "general";
    public string? Brand { get; set; }
    public string? Unit { get; set; }
    public int QuantityInStock { get; set; }
    public int MinimumStock { get; set; }
    public int? ReorderQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal? SellingPrice { get; set; }
    public string? Location { get; set; }
    public string[]? CompatibleVehicles { get; set; }
    public DateTime? LastRestockDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public bool IsActive { get; set; } = true;
}

public class PartTransaction : TenantEntity
{
    public int PartId { get; set; }
    public PartInventory? Part { get; set; }
    public string Type { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int QuantityBefore { get; set; }
    public int QuantityAfter { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
    public int? MaintenanceRecordId { get; set; }
    public MaintenanceRecord? MaintenanceRecord { get; set; }
    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}
