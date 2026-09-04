using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reports.Queries.GetRepairFrequencyReport;

public class GetRepairFrequencyReportQueryHandler : IRequestHandler<GetRepairFrequencyReportQuery, RepairFrequencyReportDto>
{
    private const int MaxInterventionsDetail = 50;

    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetRepairFrequencyReportQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<RepairFrequencyReportDto> Handle(GetRepairFrequencyReportQuery request, CancellationToken ct)
    {
        var startUtc = OperatingCostAggregator.StartUtc(request.StartDate);
        var endExclusiveUtc = OperatingCostAggregator.EndExclusiveUtc(request.EndDate);

        var data = await OperatingCostAggregator.LoadAsync(
            _context, _tenantService, startUtc, endExclusiveUtc, request.VehicleId, request.DepartmentId, ct);

        var fleetSize = data.Vehicles.Count;
        var totalInterventions = data.Vehicles.Sum(v => v.Total.RepairCount);
        var totalRepairCost = data.Vehicles.Sum(v => v.Total.Repair);
        var average = fleetSize > 0 ? (decimal)totalInterventions / fleetSize : 0m;

        var measurableKm = data.Vehicles.Where(v => v.DistanceKm is > 0).Sum(v => v.DistanceKm!.Value);

        var vehicles = data.Vehicles
            .OrderByDescending(v => v.Total.RepairCount)
            .ThenByDescending(v => v.Total.Repair)
            .ThenBy(v => v.VehicleName)
            .Select((v, i) =>
            {
                var interventions = v.Total.RepairCount;
                var km = v.DistanceKm;
                return new VehicleRepairFrequencyDto(
                    Rank: i + 1,
                    VehicleId: v.VehicleId,
                    VehicleName: v.VehicleName,
                    Plate: v.Plate,
                    Interventions: interventions,
                    DistanceKm: km.HasValue ? Math.Round(km.Value, 2) : null,
                    DistanceSource: v.DistanceSource,
                    FrequencyPer1000Km: km is > 0 ? Math.Round(interventions / km.Value * 1000m, 2) : null,
                    TotalCost: Math.Round(v.Total.Repair, 2),
                    AverageCostPerIntervention: interventions > 0 ? Math.Round(v.Total.Repair / interventions, 2) : null,
                    DeviationFromAveragePct: average > 0 ? Math.Round((interventions - average) / average * 100m, 1) : null);
            })
            .ToList();

        var concerned = vehicles.Where(v => v.Interventions > 0).ToList();
        var mostFrequent = concerned.FirstOrDefault();
        var leastFrequent = concerned.LastOrDefault();

        // ── Répartition par type (colonne repair_type, sinon déduit de la description) ──
        var classified = data.Repairs
            .Select(r =>
            {
                var (type, inferred) = RepairTypeClassifier.Classify(r.RepairType, r.Description);
                return (Repair: r, Type: type, Inferred: inferred);
            })
            .ToList();

        var byType = classified
            .GroupBy(x => x.Type)
            .Select(g => new RepairTypeShareDto(
                Type: g.Key,
                Label: RepairTypeClassifier.Label(g.Key),
                Count: g.Count(),
                TotalCost: Math.Round(g.Sum(x => x.Repair.TotalCost), 2),
                Pct: totalInterventions > 0 ? Math.Round((decimal)g.Count() / totalInterventions * 100m, 1) : 0m))
            .OrderByDescending(t => t.Count)
            .ThenByDescending(t => t.TotalCost)
            .ThenBy(t => t.Type)
            .ToList();

        // ── Détail des interventions du véhicule le plus fréquent ──
        var detailSource = mostFrequent is null
            ? new List<(RepairRow Repair, string Type, bool Inferred)>()
            : classified
                .Where(x => x.Repair.VehicleId == mostFrequent.VehicleId)
                .OrderByDescending(x => x.Repair.Date)
                .ThenByDescending(x => x.Repair.Id)
                .Take(MaxInterventionsDetail)
                .ToList();

        var supplierNames = await LoadSupplierNamesAsync(
            detailSource.Where(x => x.Repair.SupplierId.HasValue).Select(x => x.Repair.SupplierId!.Value).Distinct().ToList(), ct);

        var detail = detailSource
            .Select(x => new RepairInterventionDto(
                RepairId: x.Repair.Id,
                Date: x.Repair.Date,
                Type: x.Type,
                TypeLabel: RepairTypeClassifier.Label(x.Type),
                TypeInferred: x.Inferred,
                Description: x.Repair.Description,
                SupplierName: x.Repair.SupplierId.HasValue ? supplierNames.GetValueOrDefault(x.Repair.SupplierId.Value) : null,
                MileageAtRepair: x.Repair.MileageAtRepair,
                TotalCost: Math.Round(x.Repair.TotalCost, 2),
                Reference: x.Repair.Reference,
                Status: x.Repair.Status))
            .ToList();

        return new RepairFrequencyReportDto(
            StartDate: startUtc,
            EndDate: endExclusiveUtc.AddDays(-1),
            GeneratedAt: DateTime.UtcNow,
            TotalInterventions: totalInterventions,
            VehiclesConcerned: concerned.Count,
            FleetSize: fleetSize,
            AverageInterventionsPerVehicle: Math.Round(average, 2),
            AverageFrequencyPer1000Km: measurableKm > 0 ? Math.Round(totalInterventions / measurableKm * 1000m, 2) : null,
            TotalRepairCost: Math.Round(totalRepairCost, 2),
            AverageCostPerIntervention: totalInterventions > 0 ? Math.Round(totalRepairCost / totalInterventions, 2) : null,
            MostFrequentVehicle: mostFrequent,
            LeastFrequentVehicle: leastFrequent,
            VehiclesAboveAverage: vehicles.Count(v => v.Interventions > average),
            VehiclesBelowAverage: vehicles.Count(v => v.Interventions < average),
            Vehicles: vehicles,
            ByType: byType,
            MostFrequentVehicleInterventions: detail);
    }

    /// <summary>
    /// Nom des fournisseurs référencés par le détail (Repair n'a pas de navigation
    /// Supplier). Filtré société : un fournisseur d'une autre société reste anonyme.
    /// </summary>
    private async Task<Dictionary<int, string>> LoadSupplierNamesAsync(List<int> supplierIds, CancellationToken ct)
    {
        if (supplierIds.Count == 0) return new Dictionary<int, string>();

        var companyId = _tenantService.CompanyId ?? 0;
        return await _context.Suppliers.AsNoTracking()
            .Where(s => s.CompanyId == companyId && supplierIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
    }
}
