using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

/// <summary>
/// Computes "Carburant réel" — fuel consumption from manually entered fill-ups,
/// with NO dependency on a GPS device. Method: full-to-full. For each pair of
/// consecutive fills that both carry a valid odometer reading, the volume added
/// at the later fill is the fuel burned over the distance between the two
/// readings. Summing reliable segments gives distance, litres, L/100km and
/// cost/km. Fills with no odometer still count toward total cost/litres but not
/// toward consumption.
/// </summary>
public class GetRealFuelConsumptionQueryHandler
    : IRequestHandler<GetRealFuelConsumptionQuery, RealFuelConsumptionReportDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private static readonly CultureInfo Fr = new("fr-FR");

    // Δkm beyond this between two consecutive fills is treated as a missed fill or
    // a bad odometer reading and excluded from the consumption math.
    private const long MaxSegmentKm = 3000;

    public GetRealFuelConsumptionQueryHandler(IGisDbContext context, ICurrentTenantService tenant)
    {
        _context = context;
        _tenant = tenant;
    }

    public async Task<RealFuelConsumptionReportDto> Handle(GetRealFuelConsumptionQuery request, CancellationToken ct)
    {
        var companyId = _tenant.CompanyId ?? throw new InvalidOperationException("Company ID not set");
        var start = request.StartDate ?? DateTime.UtcNow.AddMonths(-12);
        var end = request.EndDate ?? DateTime.UtcNow;

        // Portee vehicules : ce rapport agrege litres, couts et L/100km par vehicule puis
        // pour le parc. Le filtre est applique AVANT l'agregation, sinon les totaux
        // resteraient ceux du parc entier. Fuite constatee : un employe restreint a ses
        // vehicules voyait la consommation reelle de tout le parc.
        // scope == null => admin societe, aucun filtre supplementaire.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenant, ct);

        var entriesQuery = _context.FuelEntries
            .AsNoTracking()
            .Where(f => f.CompanyId == companyId
                        && f.VehicleId != null
                        && f.InvoiceDate >= start && f.InvoiceDate <= end
                        && (request.VehicleId == null || f.VehicleId == request.VehicleId));

        if (scope is not null)
            entriesQuery = entriesQuery.Where(f => scope.Contains(f.VehicleId!.Value));

        var entries = await entriesQuery
            .Select(f => new { VehicleId = f.VehicleId!.Value, f.Volume, f.TotalAmount, f.OdometerKm, f.InvoiceDate })
            .ToListAsync(ct);

        var vehicleIds = entries.Select(e => e.VehicleId).Distinct().ToList();
        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId && vehicleIds.Contains(v.Id))
            .Select(v => new { v.Id, v.Name, v.Plate, v.FuelType })
            .ToDictionaryAsync(v => v.Id, ct);

        var vehicleDtos = new List<VehicleFuelConsumptionDto>();
        var monthLiters = new Dictionary<(int, int), decimal>();
        var monthCost = new Dictionary<(int, int), decimal>();
        var monthSegLiters = new Dictionary<(int, int), decimal>();
        var monthSegKm = new Dictionary<(int, int), decimal>();
        decimal fleetSegKm = 0, fleetSegLiters = 0, fleetSegCost = 0;

        foreach (var grp in entries.GroupBy(e => e.VehicleId))
        {
            var list = grp.OrderBy(e => e.InvoiceDate).ToList();
            var totalLiters = list.Sum(e => e.Volume);
            var totalCost = list.Sum(e => e.TotalAmount);
            var noOdo = list.Count(e => e.OdometerKm is not > 0);

            foreach (var e in list)
            {
                var k = (e.InvoiceDate.Year, e.InvoiceDate.Month);
                monthLiters[k] = monthLiters.GetValueOrDefault(k) + e.Volume;
                monthCost[k] = monthCost.GetValueOrDefault(k) + e.TotalAmount;
            }

            // Full-to-full over consecutive fills that BOTH have a valid odometer.
            var odo = list.Where(e => e.OdometerKm is > 0).OrderBy(e => e.InvoiceDate).ToList();
            decimal segKm = 0, segLiters = 0, segCost = 0;
            int skipped = 0;
            for (int i = 1; i < odo.Count; i++)
            {
                var dKm = odo[i].OdometerKm!.Value - odo[i - 1].OdometerKm!.Value;
                if (dKm <= 0 || dKm > MaxSegmentKm) { skipped++; continue; }
                segKm += dKm;
                segLiters += odo[i].Volume;     // volume added now = fuel burned over dKm
                segCost += odo[i].TotalAmount;
                var k = (odo[i].InvoiceDate.Year, odo[i].InvoiceDate.Month);
                monthSegLiters[k] = monthSegLiters.GetValueOrDefault(k) + odo[i].Volume;
                monthSegKm[k] = monthSegKm.GetValueOrDefault(k) + dKm;
            }

            fleetSegKm += segKm; fleetSegLiters += segLiters; fleetSegCost += segCost;

            vehicles.TryGetValue(grp.Key, out var vi);
            vehicleDtos.Add(new VehicleFuelConsumptionDto(
                VehicleId: grp.Key,
                VehicleName: vi?.Name ?? $"#{grp.Key}",
                Plate: vi?.Plate,
                FuelType: vi?.FuelType,
                EntryCount: list.Count,
                TotalLiters: Math.Round(totalLiters, 2),
                TotalCost: Math.Round(totalCost, 2),
                DistanceKm: segKm > 0 ? segKm : null,
                ConsumptionPer100Km: segKm > 0 ? Math.Round(segLiters / segKm * 100m, 2) : null,
                CostPerKm: segKm > 0 ? Math.Round(segCost / segKm, 3) : null,
                EntriesWithoutOdometer: noOdo,
                FirstEntryDate: list.First().InvoiceDate,
                LastEntryDate: list.Last().InvoiceDate,
                ReliableOdometer: noOdo == 0 && skipped == 0 && odo.Count >= 2
            ));
        }

        // Monthly trend over the requested range (real values, not a placeholder).
        var trends = new List<MonthlyFuelConsumptionDto>();
        var cur = new DateTime(start.Year, start.Month, 1);
        var lastMonth = new DateTime(end.Year, end.Month, 1);
        while (cur <= lastMonth)
        {
            var k = (cur.Year, cur.Month);
            var segKm = monthSegKm.GetValueOrDefault(k);
            trends.Add(new MonthlyFuelConsumptionDto(
                Year: cur.Year,
                Month: cur.Month,
                MonthName: Fr.DateTimeFormat.GetMonthName(cur.Month),
                TotalLiters: Math.Round(monthLiters.GetValueOrDefault(k), 2),
                TotalCost: Math.Round(monthCost.GetValueOrDefault(k), 2),
                ConsumptionPer100Km: segKm > 0 ? Math.Round(monthSegLiters.GetValueOrDefault(k) / segKm * 100m, 2) : null
            ));
            cur = cur.AddMonths(1);
        }

        return new RealFuelConsumptionReportDto(
            TotalFuelCost: Math.Round(vehicleDtos.Sum(v => v.TotalCost), 2),
            TotalLiters: Math.Round(vehicleDtos.Sum(v => v.TotalLiters), 2),
            TotalDistanceKm: fleetSegKm,
            FleetConsumptionPer100Km: fleetSegKm > 0 ? Math.Round(fleetSegLiters / fleetSegKm * 100m, 2) : null,
            FleetCostPerKm: fleetSegKm > 0 ? Math.Round(fleetSegCost / fleetSegKm, 3) : null,
            VehicleCount: vehicleDtos.Count,
            EntriesWithoutOdometer: vehicleDtos.Sum(v => v.EntriesWithoutOdometer),
            Vehicles: vehicleDtos.OrderByDescending(v => v.TotalCost).ToList(),
            MonthlyTrends: trends
        );
    }
}
