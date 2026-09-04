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
        var monthSegKm = new Dictionary<(int, int), decimal>();
        decimal fleetSegKm = 0;
        int fleetIgnored = 0;

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

            // ── Distance parcourue, d'après les relevés compteur ──────────────
            //
            // Recette client du 04/09/2026 : les quatre chiffres affichés (litres,
            // coût, L/100, €/km) n'étaient pas calculés sur le même périmètre —
            // litres et coût sur TOUS les pleins, L/100 et €/km sur les seuls
            // intervalles « plein-à-plein » valides, sans le premier plein ni les
            // pleins entourant un relevé aberrant. Le client divisait les litres
            // affichés par la distance affichée et ne retrouvait jamais le L/100.
            //
            // Désormais : L/100 = litres affichés / distance affichée, €/km = coût
            // affiché / distance affichée. La distance est la somme des écarts entre
            // relevés consécutifs cohérents (= dernier − premier relevé de chaque
            // série continue). Biais assumé et annoncé à l'écran : le premier plein
            // de la période est compté alors qu'il a été brûlé avant le premier
            // relevé — de l'ordre de 1/n sur n pleins.
            //
            // Un relevé ISOLÉ aberrant (145 200 km entre 46 845 et 47 455 — faute
            // de frappe à l'import) est ignoré comme relevé, PAS comme intervalle :
            // avant, il faisait sauter les deux intervalles qui l'encadrent, leurs
            // 610 km et leurs deux pleins. Critère : ses deux voisins sont cohérents
            // entre eux (0 < Δ ≤ MaxSegmentKm) alors qu'il s'écarte des deux.
            var odo = list.Where(e => e.OdometerKm is > 0).OrderBy(e => e.InvoiceDate).ToList();
            var kept = new List<(long Km, DateTime Date)>();
            int ignored = 0;
            for (int i = 0; i < odo.Count; i++)
            {
                var km = odo[i].OdometerKm!.Value;
                if (i > 0 && i < odo.Count - 1)
                {
                    var prev = odo[i - 1].OdometerKm!.Value;
                    var next = odo[i + 1].OdometerKm!.Value;
                    var neighboursCoherent = next - prev > 0 && next - prev <= MaxSegmentKm;
                    var offFromPrev = km - prev <= 0 || km - prev > MaxSegmentKm;
                    var offFromNext = next - km <= 0 || next - km > MaxSegmentKm;
                    if (neighboursCoherent && offFromPrev && offFromNext) { ignored++; continue; }
                }
                kept.Add((km, odo[i].InvoiceDate));
            }

            // Somme des écarts cohérents. Un écart ≤ 0 ou > MaxSegmentKm entre deux
            // relevés conservés est une rupture de série (changement de compteur,
            // deux imports incompatibles) : on ne l'additionne pas, sans rien
            // rejeter d'autre.
            decimal segKm = 0;
            int breaks = 0;
            for (int i = 1; i < kept.Count; i++)
            {
                var dKm = kept[i].Km - kept[i - 1].Km;
                if (dKm <= 0 || dKm > MaxSegmentKm) { breaks++; continue; }
                segKm += dKm;
                var k = (kept[i].Date.Year, kept[i].Date.Month);
                monthSegKm[k] = monthSegKm.GetValueOrDefault(k) + dKm;
            }

            fleetSegKm += segKm;
            fleetIgnored += ignored;

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
                ConsumptionPer100Km: segKm > 0 ? Math.Round(totalLiters / segKm * 100m, 2) : null,
                CostPerKm: segKm > 0 ? Math.Round(totalCost / segKm, 3) : null,
                EntriesWithoutOdometer: noOdo,
                FirstEntryDate: list.First().InvoiceDate,
                LastEntryDate: list.Last().InvoiceDate,
                ReliableOdometer: noOdo == 0 && ignored == 0 && breaks == 0 && kept.Count >= 2,
                IgnoredOdometerReadings: ignored
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
                // Même définition que par véhicule : litres du mois / km relevés dans le mois.
                ConsumptionPer100Km: segKm > 0 ? Math.Round(monthLiters.GetValueOrDefault(k) / segKm * 100m, 2) : null
            ));
            cur = cur.AddMonths(1);
        }

        return new RealFuelConsumptionReportDto(
            TotalFuelCost: Math.Round(vehicleDtos.Sum(v => v.TotalCost), 2),
            TotalLiters: Math.Round(vehicleDtos.Sum(v => v.TotalLiters), 2),
            TotalDistanceKm: fleetSegKm,
            // Parc : mêmes totaux que les cartes affichées, sur la même distance.
            FleetConsumptionPer100Km: fleetSegKm > 0 ? Math.Round(vehicleDtos.Sum(v => v.TotalLiters) / fleetSegKm * 100m, 2) : null,
            FleetCostPerKm: fleetSegKm > 0 ? Math.Round(vehicleDtos.Sum(v => v.TotalCost) / fleetSegKm, 3) : null,
            VehicleCount: vehicleDtos.Count,
            EntriesWithoutOdometer: vehicleDtos.Sum(v => v.EntriesWithoutOdometer),
            Vehicles: vehicleDtos.OrderByDescending(v => v.TotalCost).ToList(),
            MonthlyTrends: trends,
            IgnoredOdometerReadings: fleetIgnored
        );
    }
}
