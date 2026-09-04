using GisAPI.Application.Features.Reports.Common;

namespace GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;

/// <summary>
/// Construit le DTO commun à R1 et R3 à partir des données agrégées. Les KPI
/// sont toujours calculés sur TOUT le périmètre ; seul <c>Vehicles</c> est
/// tronqué quand <paramref name="top"/> est fourni (classement).
/// </summary>
public static class OperatingCostReportBuilder
{
    public const string DistanceNote =
        "Kilométrage issu des relevés compteur saisis aux pleins (véhicules sans boîtier) et des trajets GPS (véhicules équipés).";

    public static OperatingCostReportDto Build(OperatingCostData data, DateTime startDate, DateTime endDate, int? top = null)
    {
        var analysed = data.Vehicles.Where(v => v.HasActivity).ToList();
        var measurable = analysed.Where(v => v.DistanceKm is > 0).ToList();

        var totalKm = measurable.Sum(v => v.DistanceKm!.Value);
        // Moyenne du parc = moyenne PONDÉRÉE : Σ coût des véhicules mesurables / Σ km.
        decimal? average = totalKm > 0 ? measurable.Sum(v => v.Total.Total) / totalKm : null;

        var ranked = analysed
            .Select(v =>
            {
                decimal? costPerKm = v.DistanceKm is > 0 ? v.Total.Total / v.DistanceKm.Value : null;
                decimal? deviation = costPerKm.HasValue && average is > 0
                    ? (costPerKm.Value - average.Value) / average.Value * 100m
                    : null;
                return (Vehicle: v, CostPerKm: costPerKm, Deviation: deviation);
            })
            // costPerKm desc, null en fin, puis totalCost desc
            .OrderByDescending(x => x.CostPerKm.HasValue)
            .ThenByDescending(x => x.CostPerKm ?? 0m)
            .ThenByDescending(x => x.Vehicle.Total.Total)
            .ThenBy(x => x.Vehicle.VehicleName)
            .Select((x, i) => new VehicleOperatingCostDto(
                Rank: i + 1,
                VehicleId: x.Vehicle.VehicleId,
                VehicleName: x.Vehicle.VehicleName,
                Plate: x.Vehicle.Plate,
                DepartmentName: x.Vehicle.DepartmentName,
                DistanceKm: x.Vehicle.DistanceKm.HasValue ? Math.Round(x.Vehicle.DistanceKm.Value, 2) : null,
                DistanceSource: x.Vehicle.DistanceSource,
                ReliableDistance: x.Vehicle.ReliableDistance,
                IgnoredOdometerReadings: x.Vehicle.IgnoredOdometerReadings,
                OdometerBreaks: x.Vehicle.OdometerBreaks,
                FuelCost: Math.Round(x.Vehicle.Total.Fuel, 2),
                MaintenanceCost: Math.Round(x.Vehicle.Total.Maintenance, 2),
                RepairCost: Math.Round(x.Vehicle.Total.Repair, 2),
                OtherCost: Math.Round(x.Vehicle.Total.Other, 2),
                TotalCost: Math.Round(x.Vehicle.Total.Total, 2),
                CostPerKm: x.CostPerKm.HasValue ? Math.Round(x.CostPerKm.Value, 3) : null,
                DeviationFromAveragePct: x.Deviation.HasValue ? Math.Round(x.Deviation.Value, 1) : null))
            .ToList();

        if (top.HasValue)
            ranked = ranked.Take(Math.Max(0, top.Value)).ToList();

        return new OperatingCostReportDto(
            StartDate: startDate,
            EndDate: endDate,
            GeneratedAt: DateTime.UtcNow,
            TotalCost: Math.Round(analysed.Sum(v => v.Total.Total), 2),
            TotalKm: Math.Round(totalKm, 2),
            AverageCostPerKm: average.HasValue ? Math.Round(average.Value, 3) : null,
            VehicleCount: analysed.Count,
            FleetSize: data.Vehicles.Count,
            VehiclesWithoutDistance: analysed.Count(v => v.DistanceKm is not > 0),
            TotalFuelCost: Math.Round(analysed.Sum(v => v.Total.Fuel), 2),
            TotalMaintenanceCost: Math.Round(analysed.Sum(v => v.Total.Maintenance), 2),
            TotalRepairCost: Math.Round(analysed.Sum(v => v.Total.Repair), 2),
            TotalOtherCost: Math.Round(analysed.Sum(v => v.Total.Other), 2),
            DistanceNote: DistanceNote,
            Vehicles: ranked);
    }
}
