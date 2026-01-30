using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class FuelType : Entity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; } = true;

    // Navigation
    public ICollection<FuelPricing> Pricings { get; set; } = new List<FuelPricing>();
}

public class FuelPricing : TenantEntity
{
    public int FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }
    public decimal PricePerLiter { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Societe? Societe { get; set; }
}


