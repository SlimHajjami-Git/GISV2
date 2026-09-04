using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetVehicleCostEvolution;

/// <summary>
/// R2 — Évolution mensuelle des coûts d'UN véhicule
/// (GET /api/reports/costs/evolution/{vehicleId}). Un élément par mois de la
/// plage, mois vides à 0, ordre chronologique.
/// </summary>
public record GetVehicleCostEvolutionQuery(
    int VehicleId,
    DateTime StartDate,
    DateTime EndDate
) : IQuery<VehicleCostEvolutionDto>;

public record VehicleCostEvolutionDto(
    int VehicleId,
    string VehicleName,
    string? Plate,
    DateTime StartDate,
    DateTime EndDate,
    DateTime GeneratedAt,
    decimal TotalCost,
    decimal AverageMonthlyCost,            // TotalCost / nombre de mois de la plage (mois vides inclus)
    MonthlyVehicleCostDto? HighestMonth,   // parmi les mois à TotalCost > 0
    MonthlyVehicleCostDto? LowestMonth,
    decimal TotalFuelCost,
    decimal TotalMaintenanceCost,
    decimal TotalRepairCost,
    decimal TotalOtherCost,
    decimal? TotalDistanceKm,
    string DistanceSource,
    List<MonthlyVehicleCostDto> Months);

public record MonthlyVehicleCostDto(
    int Year,
    int Month,
    string MonthName,                      // "Sept. 2025"
    decimal FuelCost,
    decimal MaintenanceCost,
    decimal RepairCost,
    decimal OtherCost,
    decimal TotalCost,
    decimal? DistanceKm,                   // km attribués au mois (relevé aval / trajets du mois)
    decimal? VariationPct);                // vs mois précédent ; null le 1er mois ou si précédent = 0
