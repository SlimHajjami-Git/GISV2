using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// Entrée manuelle de carburant (factures, pleins)
/// </summary>
public class FuelEntry : TenantEntity
{
    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    
    public string VehiclePlate { get; set; } = string.Empty;
    
    public int FuelTypeId { get; set; }
    public FuelType? FuelType { get; set; }
    
    public decimal Volume { get; set; }
    public decimal PricePerLiter { get; set; }
    public decimal TotalAmount { get; set; }
    
    public DateTime InvoiceDate { get; set; }
    
    public string? StationName { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Notes { get; set; }
    
    public int? DriverId { get; set; }
    public Driver? Driver { get; set; }
    
    public long? OdometerKm { get; set; }
}
