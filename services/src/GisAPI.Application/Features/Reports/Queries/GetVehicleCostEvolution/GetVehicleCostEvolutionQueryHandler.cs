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
            ?? throw new NotFoundException(VehiculeIntrouvableException.Libelle);

        var months = new List<MonthlyVehicleCostDto>();
        decimal? previousTotal = null;
        var previousPartial = false;
        foreach (var (year, month) in data.MonthsInRange())
        {
            var bucket = vehicle.Months.GetValueOrDefault((year, month), CostBucket.Zero);
            var total = bucket.Total;

            var km = vehicle.MonthlyKm.GetValueOrDefault((year, month));

            // Mois INCOMPLET : la période ne couvre pas le mois entier, par l'une
            // OU l'autre borne. Elle s'arrête avant son dernier jour (mois en
            // cours de « 12 derniers mois » et « Cette année ») ou elle commence
            // après son premier jour (période personnalisée qui ne démarre pas un
            // 1er). Présenté comme un mois normal, il affichait une chute de 33 à
            // 100 % sur les 12 véhicules du jeu de recette (10 jours sur 30), et
            // la carte « Mois le moins élevé » le désignait presque toujours : un
            // fait faux, affiché en gros. Corrigé sur la borne de fin le
            // 11/09/2026, la borne de début était restée ouverte — octobre 2025
            // démarré le 15 sortait « mois le moins élevé » à 278,64 alors que le
            // mois entier vaut 390,10 (recette du 13/09/2026).
            var debutDuMois = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var finDuMois = debutDuMois.AddMonths(1);
            var incomplet = startUtc > debutDuMois || endExclusiveUtc < finDuMois;

            // Comparer 10 jours à un mois entier n'a pas de sens, dans un sens
            // comme dans l'autre : un mois complet qui suit un mois tronqué
            // afficherait une flambée aussi fausse que la chute du mois tronqué.
            decimal? variation = !incomplet && !previousPartial && previousTotal is > 0
                ? (total - previousTotal.Value) / previousTotal.Value * 100m
                : null;

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
                IsPartial: incomplet,
                CreditAmount: Math.Round(bucket.Credit, 2)));

            previousTotal = total;
            previousPartial = incomplet;
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
            Months: months,
            TotalCreditAmount: Math.Round(vehicle.Total.Credit, 2));
    }
}
