using MediatR;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

/// <summary>
/// GPS-INDEPENDENT fuel consumption ("Carburant réel"). For clients WITHOUT GPS
/// boxes: distance, L/100km and cost/km are derived purely from the manually
/// entered fuel fill-ups (FuelEntry: Volume, TotalAmount, OdometerKm, InvoiceDate)
/// using the full-to-full method. Unlike GetFuelExpenseStatistics this does NOT
/// require GpsDeviceId and never touches GPS data.
/// </summary>
public record GetRealFuelConsumptionQuery(
    DateTime? StartDate,
    DateTime? EndDate,
    int? VehicleId
) : IRequest<RealFuelConsumptionReportDto>;

public record RealFuelConsumptionReportDto(
    decimal TotalFuelCost,
    decimal TotalLiters,
    decimal TotalDistanceKm,
    decimal? FleetConsumptionPer100Km,
    decimal? FleetCostPerKm,
    int VehicleCount,
    int EntriesWithoutOdometer,
    List<VehicleFuelConsumptionDto> Vehicles,
    List<MonthlyFuelConsumptionDto> MonthlyTrends,
    // Relevés compteur écartés comme fautes de frappe (voir le handler).
    int IgnoredOdometerReadings = 0
);

public record VehicleFuelConsumptionDto(
    int VehicleId,
    string VehicleName,
    string? Plate,
    string? FuelType,
    int EntryCount,
    decimal TotalLiters,
    decimal TotalCost,
    decimal? DistanceKm,            // null = not enough odometer data to measure
    decimal? ConsumptionPer100Km,   // = TotalLiters / DistanceKm × 100 — les quatre chiffres se recoupent
    decimal? CostPerKm,             // = TotalCost / DistanceKm
    int EntriesWithoutOdometer,
    DateTime? FirstEntryDate,
    DateTime? LastEntryDate,
    bool ReliableOdometer,          // false if any fill lacks km, a reading was ignored or the series broke
    int IgnoredOdometerReadings = 0 // relevés écartés (faute de frappe isolée)
);

public record MonthlyFuelConsumptionDto(
    int Year,
    int Month,
    string MonthName,
    decimal TotalLiters,
    decimal TotalCost,
    decimal? ConsumptionPer100Km
);
