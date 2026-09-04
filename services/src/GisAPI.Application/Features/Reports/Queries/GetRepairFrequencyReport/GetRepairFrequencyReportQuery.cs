using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetRepairFrequencyReport;

/// <summary>
/// R4 — Fréquence des réparations par véhicule
/// (GET /api/reports/costs/repair-frequency). Réparations au statut ≠ cancelled ;
/// <c>Vehicles</c> = TOUT le périmètre, véhicules à 0 intervention compris.
/// </summary>
public record GetRepairFrequencyReportQuery(
    DateTime StartDate,
    DateTime EndDate,
    int? VehicleId = null,
    int? DepartmentId = null
) : IQuery<RepairFrequencyReportDto>;

public record RepairFrequencyReportDto(
    DateTime StartDate,
    DateTime EndDate,
    DateTime GeneratedAt,
    int TotalInterventions,
    int VehiclesConcerned,
    int FleetSize,
    decimal AverageInterventionsPerVehicle,          // TotalInterventions / FleetSize
    decimal? AverageFrequencyPer1000Km,              // TotalInterventions / Σ km mesurables × 1000
    decimal TotalRepairCost,
    decimal? AverageCostPerIntervention,
    VehicleRepairFrequencyDto? MostFrequentVehicle,  // parmi les véhicules à ≥ 1 intervention
    VehicleRepairFrequencyDto? LeastFrequentVehicle,
    int VehiclesAboveAverage,                        // interventions > moyenne
    int VehiclesBelowAverage,                        // interventions < moyenne
    List<VehicleRepairFrequencyDto> Vehicles,        // tri interventions desc, totalCost desc
    List<RepairTypeShareDto> ByType,                 // donut, tri count desc
    List<RepairInterventionDto> MostFrequentVehicleInterventions); // tri date desc, max 50

public record VehicleRepairFrequencyDto(
    int Rank,
    int VehicleId,
    string VehicleName,
    string? Plate,
    int Interventions,
    decimal? DistanceKm,
    string DistanceSource,
    decimal? FrequencyPer1000Km,                     // interventions / km × 1000, 2 décimales
    decimal TotalCost,
    decimal? AverageCostPerIntervention,
    decimal? DeviationFromAveragePct);               // (interventions − moyenne) / moyenne × 100

public record RepairTypeShareDto(
    string Type,
    string Label,
    int Count,
    decimal TotalCost,
    decimal Pct);

public record RepairInterventionDto(
    int RepairId,
    DateTime Date,
    string Type,
    string TypeLabel,
    bool TypeInferred,
    string? Description,
    string? SupplierName,
    int? MileageAtRepair,
    decimal TotalCost,
    string Reference,
    string Status);
