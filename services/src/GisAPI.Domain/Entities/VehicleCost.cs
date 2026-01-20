using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class VehicleCost : TenantEntity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public int? Mileage { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? ReceiptUrl { get; set; }
    public string? FuelType { get; set; }
    public decimal? Liters { get; set; }
    public decimal? PricePerLiter { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public Societe? Societe { get; set; }
}
