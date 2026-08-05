namespace GisAPI.Application.Features.FuelExpenses;

/// <summary>
/// DTO for vehicle fuel expense statistics
/// </summary>
public record VehicleFuelExpenseDto(
    int VehicleId,
    string VehicleName,
    string? Plate,
    string? FuelType,
    int? FuelTankCapacity,
    decimal TotalFuelConsumedLiters,
    decimal TotalFuelCost,
    decimal AverageConsumptionPer100Km,
    decimal DeviationFromFleetAverage,
    int TotalDistanceKm,
    bool IsEstimated,
    List<DailyFuelConsumptionDto> DailyConsumption,
    List<FuelRefillEventDto> Refuels
);

/// <summary>
/// DTO for daily fuel consumption
/// </summary>
public record DailyFuelConsumptionDto(
    DateTime Date,
    decimal FuelConsumedLiters,
    decimal FuelCost,
    int DistanceKm,
    decimal ConsumptionPer100Km
);

/// <summary>
/// DTO for fleet-wide fuel statistics
/// </summary>
public record FleetFuelStatisticsDto(
    decimal TotalFleetFuelCost,
    decimal TotalFleetFuelConsumedLiters,
    decimal FleetAverageConsumptionPer100Km,
    decimal FleetStandardDeviation,
    int TotalFleetDistanceKm,
    int VehicleCount,
    List<FuelTypeDistributionDto> FuelTypeDistribution,
    List<MonthlyFuelTrendDto> MonthlyTrends,
    List<VehicleFuelExpenseDto> VehicleExpenses
);

/// <summary>
/// DTO for fuel type distribution in fleet
/// </summary>
public record FuelTypeDistributionDto(
    string FuelType,
    int VehicleCount,
    decimal TotalFuelConsumed,
    decimal TotalCost,
    decimal Percentage
);

/// <summary>
/// DTO for monthly fuel trends
/// </summary>
public record MonthlyFuelTrendDto(
    int Year,
    int Month,
    string MonthName,
    decimal TotalFuelConsumed,
    decimal TotalCost,
    decimal AverageConsumption
);

/// <summary>
/// DTO for fuel pricing information
/// </summary>
public record FuelPriceDto(
    int FuelTypeId,
    string FuelTypeCode,
    string FuelTypeName,
    decimal PricePerLiter,
    DateTime EffectiveFrom
);

/// <summary>
/// DTO for fuel refill event detected from GPS data
/// </summary>
public record FuelRefillEventDto(
    DateTime Timestamp,
    int VehicleId,
    decimal FuelAddedLiters,
    decimal? EstimatedCost,
    double? Latitude,
    double? Longitude
);

/// <summary>One sampled point of the fuel-level curve (from the boitier sensor).</summary>
public record FuelLevelPointDto(
    DateTime T,
    int Percent,
    decimal Liters
);

/// <summary>
/// A refill detected from the fuel sensor (a level jump >= 10%).
/// FromPercent/ToPercent portent la mesure BRUTE de la jauge : les litres ne
/// sont qu'une conversion (étalonnable par véhicule) — c'est la montée en
/// points qui fait foi, jamais le chiffre en litres.
/// </summary>
public record DetectedRefillDto(
    DateTime T,
    decimal Liters,
    int FromPercent = 0,
    int ToPercent = 0
)
{
    public int DeltaPoints => ToPercent - FromPercent;
}

/// <summary>GPS-side fuel audit for one vehicle: the level curve + the detected refills.</summary>
public record FuelLevelAuditDto(
    bool HasSensor,
    int TankCapacity,
    List<FuelLevelPointDto> LevelSeries,
    List<DetectedRefillDto> DetectedRefills
);
