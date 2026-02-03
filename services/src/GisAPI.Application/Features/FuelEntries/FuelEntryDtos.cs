namespace GisAPI.Application.Features.FuelEntries;

public record FuelEntryDto(
    int Id,
    int? VehicleId,
    string VehiclePlate,
    int FuelTypeId,
    string FuelTypeCode,
    string FuelTypeName,
    decimal Volume,
    decimal PricePerLiter,
    decimal TotalAmount,
    DateTime InvoiceDate,
    string? StationName,
    string? InvoiceNumber,
    string? Notes,
    int? DriverId,
    string? DriverName,
    long? OdometerKm,
    DateTime CreatedAt
);

public record CreateFuelEntryRequest(
    string VehiclePlate,
    int FuelTypeId,
    decimal Volume,
    decimal PricePerLiter,
    DateTime InvoiceDate,
    string? StationName = null,
    string? InvoiceNumber = null,
    string? Notes = null,
    int? DriverId = null,
    long? OdometerKm = null
);

public record UpdateFuelEntryRequest(
    string VehiclePlate,
    int FuelTypeId,
    decimal Volume,
    decimal PricePerLiter,
    DateTime InvoiceDate,
    string? StationName = null,
    string? InvoiceNumber = null,
    string? Notes = null,
    int? DriverId = null,
    long? OdometerKm = null
);

public record FuelEntryStatsDto(
    int TotalEntries,
    decimal TotalVolume,
    decimal TotalAmount,
    decimal AveragePricePerLiter,
    Dictionary<string, decimal> VolumeByFuelType,
    Dictionary<string, decimal> AmountByVehicle
);
