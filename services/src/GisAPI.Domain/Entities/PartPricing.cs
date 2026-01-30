using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class PartPricing : TenantEntity
{
    public int PartId { get; set; }
    public VehiclePart Part { get; set; } = null!;
    public decimal Price { get; set; }
    public string? Supplier { get; set; }
    public string? Notes { get; set; }
    public Societe Societe { get; set; } = null!;
}


