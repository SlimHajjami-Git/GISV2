using MediatR;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

/// <summary>
/// Anti-fraud fuel report: compares the REAL fuel billed (FuelEntry, entered via /carburant,
/// paid by fuel card) against the fuel the vehicle actually consumed according to its GPS device
/// (boitier sensor when available — e.g. NEMS L — otherwise distance-based estimation).
/// A large positive gap (billed >> consumed) suggests the card was used for another vehicle.
/// </summary>
public record GetFuelComparisonReportQuery(
    DateTime? StartDate,
    DateTime? EndDate,
    int? VehicleId
) : IRequest<FuelComparisonReportDto>;

/// <summary>One vehicle row of the real-vs-GPS fuel comparison.</summary>
public record FuelComparisonRowDto(
    int VehicleId,
    string VehicleName,
    string? Plate,
    decimal RealLiters,     // sum of FuelEntry.Volume in the period (billed / card)
    decimal RealCost,       // sum of FuelEntry.TotalAmount in the period
    decimal GpsLiters,      // litres consumed per the boitier (sensor or estimate)
    string GpsSource,       // "capteur" (real sensor, e.g. NEMS L) | "estime" | "aucun" (no GPS device)
    int DistanceKm,
    decimal DiffLiters,     // RealLiters - GpsLiters (positive = billed more than consumed)
    decimal DiffPercent     // DiffLiters / GpsLiters * 100
);

/// <summary>Fleet-level real-vs-GPS fuel comparison. The suspicion verdict (threshold on
/// DiffPercent + DiffLiters) is computed client-side so it can be tuned live in the UI.</summary>
public record FuelComparisonReportDto(
    DateTime StartDate,
    DateTime EndDate,
    decimal TotalRealLiters,
    decimal TotalRealCost,
    decimal TotalGpsLiters,
    int VehicleCount,
    int SensorCount,        // how many rows have a real fuel sensor ("capteur")
    List<FuelComparisonRowDto> Rows
);
