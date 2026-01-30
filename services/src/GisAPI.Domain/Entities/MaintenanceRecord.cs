using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class MaintenanceRecord : TenantEntity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string Type { get; set; } = "scheduled";
    public string Description { get; set; } = string.Empty;
    public int MileageAtService { get; set; }
    public DateTime Date { get; set; }
    public DateTime? NextServiceDate { get; set; }
    public int? NextServiceMileage { get; set; }
    public string Status { get; set; } = "scheduled";
    public decimal LaborCost { get; set; }
    public decimal PartsCost { get; set; }
    public decimal TotalCost { get; set; }
    public string? ServiceProvider { get; set; }
    public string? ProviderContact { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? InvoiceUrl { get; set; }
    public string? Notes { get; set; }
    
    // Link to garage/supplier who performed the maintenance
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public Societe? Societe { get; set; }
    
    public ICollection<MaintenancePart> Parts { get; set; } = new List<MaintenancePart>();
}

public class MaintenancePart : Entity
{
    public int MaintenanceRecordId { get; set; }
    public MaintenanceRecord? MaintenanceRecord { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PartNumber { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
}


