using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;

/// <summary>
/// R1 — Coût d'exploitation par véhicule sur une période
/// (GET /api/reports/costs/operating). Bornes inclusives jour entier.
/// </summary>
public record GetOperatingCostReportQuery(
    DateTime StartDate,
    DateTime EndDate,
    int? VehicleId = null,
    int? DepartmentId = null
) : IQuery<OperatingCostReportDto>;

/// <summary>Partagé par R1 (coût d'exploitation) et R3 (classement, <c>Vehicles</c> tronqué au top N).</summary>
public record OperatingCostReportDto(
    DateTime StartDate,
    DateTime EndDate,
    DateTime GeneratedAt,
    decimal TotalCost,
    decimal TotalKm,                       // Σ distances mesurables
    decimal? AverageCostPerKm,             // Σ coût véhicules mesurables / Σ km (moyenne pondérée)
    int VehicleCount,                      // véhicules analysés (dépense ou distance)
    int FleetSize,
    int VehiclesWithoutDistance,
    decimal TotalFuelCost,
    decimal TotalMaintenanceCost,
    decimal TotalRepairCost,
    decimal TotalOtherCost,
    string DistanceNote,
    List<VehicleOperatingCostDto> Vehicles);

public record VehicleOperatingCostDto(
    int Rank,
    int VehicleId,
    string VehicleName,
    string? Plate,
    string? DepartmentName,
    decimal? DistanceKm,
    string DistanceSource,                 // "odometer" | "gps" | "none"
    bool ReliableDistance,
    int IgnoredOdometerReadings,
    int OdometerBreaks,
    decimal FuelCost,
    decimal MaintenanceCost,
    decimal RepairCost,
    decimal OtherCost,
    decimal TotalCost,
    decimal? CostPerKm,                    // TotalCost / DistanceKm, 3 décimales
    decimal? DeviationFromAveragePct);     // (CostPerKm − moyenne) / moyenne × 100, 1 décimale
