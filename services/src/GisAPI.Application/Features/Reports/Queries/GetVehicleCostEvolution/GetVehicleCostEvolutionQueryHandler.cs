using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;

namespace GisAPI.Application.Features.Reports.Queries.GetVehicleCostEvolution;

public class GetVehicleCostEvolutionQueryHandler : IRequestHandler<GetVehicleCostEvolutionQuery, VehicleCostEvolutionDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehicleCostEvolutionQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<VehicleCostEvolutionDto> Handle(GetVehicleCostEvolutionQuery request, CancellationToken ct)
    {
        var startUtc = OperatingCostAggregator.StartUtc(request.StartDate);
        var endExclusiveUtc = OperatingCostAggregator.EndExclusiveUtc(request.EndDate);

        var data = await OperatingCostAggregator.LoadAsync(
            _context, _tenantService, startUtc, endExclusiveUtc, request.VehicleId, departmentId: null, ct);

        // Hors société ou hors portée de l'appelant : même réponse qu'un véhicule
        // inexistant (404 via ExceptionHandlingMiddleware), pas de fuite.
        var vehicle = data.Vehicles.FirstOrDefault(v => v.VehicleId == request.VehicleId)
            ?? throw new NotFoundException("Vehicle", request.VehicleId);

        var months = new List<MonthlyVehicleCostDto>();
        decimal? previousTotal = null;
        foreach (var (year, month) in data.MonthsInRange())
        {
            var bucket = vehicle.Months.GetValueOrDefault((year, month), CostBucket.Zero);
            var total = bucket.Total;

            decimal? variation = previousTotal is > 0
                ? (total - previousTotal.Value) / previousTotal.Value * 100m
                : null;

            var km = vehicle.MonthlyKm.GetValueOrDefault((year, month));

            // Mois INCOMPLET : la période s’arrête avant son dernier jour. C’est
            // le cas du mois en cours sur « 12 derniers mois » et « Cette année ».
            // Présenté comme un mois normal, il affichait une chute de 33 à
            // 100 % sur les 12 véhicules du jeu de recette (10 jours sur 30), et
            // la carte « Mois le moins élevé » le désignait presque toujours :
            // un fait faux, affiché en gros (recette du 11/09/2026).
            var finDuMois = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            var incomplet = endExclusiveUtc < finDuMois;
            if (incomplet)
                variation = null;   // comparer 10 jours à un mois entier n’a pas de sens

            months.Add(new MonthlyVehicleCostDto(
                Year: year,
                Month: month,
                MonthName: OperatingCostAggregator.MonthLabel(year, month),
                FuelCost: Math.Round(bucket.Fuel, 2),
                MaintenanceCost: Math.Round(bucket.Maintenance, 2),
                RepairCost: Math.Round(bucket.Repair, 2),
                OtherCost: Math.Round(bucket.Other, 2),
                TotalCost: Math.Round(total, 2),
                DistanceKm: km > 0 ? Math.Round(km, 2) : null,
                VariationPct: variation.HasValue ? Math.Round(variation.Value, 1) : null,
                IsPartial: incomplet));

            previousTotal = total;
        }

        var totalCost = vehicle.Total.Total;
        // Mois le plus et le moins élevés : parmi les mois COMPLETS seulement.
        // Un mois incomplet est par construction « le moins élevé ».
        var withCost = months.Where(m => m.TotalCost > 0 && !m.IsPartial).ToList();

        return new VehicleCostEvolutionDto(
            VehicleId: vehicle.VehicleId,
            VehicleName: vehicle.VehicleName,
            Plate: vehicle.Plate,
            StartDate: startUtc,
            EndDate: endExclusiveUtc.AddDays(-1),
            GeneratedAt: DateTime.UtcNow,
            TotalCost: Math.Round(totalCost, 2),
            AverageMonthlyCost: months.Count > 0 ? Math.Round(totalCost / months.Count, 2) : 0m,
            HighestMonth: withCost.OrderByDescending(m => m.TotalCost).FirstOrDefault(),
            LowestMonth: withCost.OrderBy(m => m.TotalCost).FirstOrDefault(),
            TotalFuelCost: Math.Round(vehicle.Total.Fuel, 2),
            TotalMaintenanceCost: Math.Round(vehicle.Total.Maintenance, 2),
            TotalRepairCost: Math.Round(vehicle.Total.Repair, 2),
            TotalOtherCost: Math.Round(vehicle.Total.Other, 2),
            TotalDistanceKm: vehicle.DistanceKm.HasValue ? Math.Round(vehicle.DistanceKm.Value, 2) : null,
            DistanceSource: vehicle.DistanceSource,
            Months: months);
    }
}
