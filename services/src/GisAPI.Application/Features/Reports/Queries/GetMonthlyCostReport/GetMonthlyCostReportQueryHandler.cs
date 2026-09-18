using GisAPI.Application.Common;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GisAPI.Application.Features.Reports.Queries.GetMonthlyCostReport;

public class GetMonthlyCostReportQueryHandler : IRequestHandler<GetMonthlyCostReportQuery, MonthlyCostReportDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private static readonly CultureInfo FrenchCulture = new("fr-FR");

    public GetMonthlyCostReportQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<MonthlyCostReportDto> Handle(GetMonthlyCostReportQuery request, CancellationToken ct)
    {
        // 400 et non 500 sur un mois hors bornes : new DateTime lèverait plus bas.
        ReportRequestRules.EnsureValidMonth(request.Year, request.Month);

        var companyId = _tenantService.CompanyId ?? 0;
        var startDate = DateTime.SpecifyKind(new DateTime(request.Year, request.Month, 1), DateTimeKind.Utc);
        var endDate = DateTime.SpecifyKind(startDate.AddMonths(1), DateTimeKind.Utc);
        // Mois PRECEDENT : les colonnes « PR » du rapport servent a comparer le
        // mois affiche au precedent. Elles existaient dans le contrat de donnees
        // mais recopiaient litteralement leurs voisines (« Same as GPS KM for
        // now ») : le client croyait disposer d’un point de comparaison alors
        // qu’il relisait deux fois le meme chiffre, et l’ecart — le seul signal
        // utile — ne pouvait par construction jamais apparaitre.
        var startPrevDate = DateTime.SpecifyKind(startDate.AddMonths(-1), DateTimeKind.Utc);

        // 1. Fetch vehicles scoped to current company
        // Explicit CompanyId filter (defense in depth; system admins also get scoped to their
        // active company for this report instead of seeing all fleets)
        // Restriction aux véhicules affectés à l'appelant : sans elle, un
        // employé restreint obtenait les coûts (carburant, entretien,
        // réparations) de tout le parc. Le filtre s'applique ICI, sur la liste
        // de véhicules dont dérivent tous les totaux et regroupements plus bas,
        // donc avant toute agrégation. scope == null => admin société, vue complète.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);

        var vehiclesQuery = _context.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Include(v => v.Department)
            .Include(v => v.AssignedDriver)
            .AsQueryable();

        if (scope is not null)
            vehiclesQuery = vehiclesQuery.Where(v => scope.Contains(v.Id));

        if (request.DepartmentId.HasValue)
            vehiclesQuery = vehiclesQuery.Where(v => v.DepartmentId == request.DepartmentId.Value);

        var vehicles = await vehiclesQuery.ToListAsync(ct);
        var vehicleIds = vehicles.Select(v => v.Id).ToList();

        if (!vehicleIds.Any())
        {
            return new MonthlyCostReportDto
            {
                Year = request.Year,
                Month = request.Month,
                MonthName = startDate.ToString("MMMM yyyy", FrenchCulture),
                ReportPeriod = $"{startDate:dd/MM/yyyy} - {endDate.AddDays(-1):dd/MM/yyyy}",
                GeneratedAt = DateTime.UtcNow
            };
        }

        // 2. Fetch fuel entries from /carburant page's table (fuel_entries)
        var fuelEntries = await _context.FuelEntries.AsNoTracking()
            .Where(f => f.CompanyId == companyId
                     && f.VehicleId.HasValue
                     && vehicleIds.Contains(f.VehicleId.Value)
                     && f.InvoiceDate >= startDate
                     && f.InvoiceDate < endDate)
            .Select(f => new { f.VehicleId, f.TotalAmount, f.Volume })
            .ToListAsync(ct);

        // 3. Coûts d'entretien = dépenses (VehicleCost type maintenance/entretien),
        //    MÊME source que le tableau de bord et la page Dépenses. Un entretien
        //    saisi depuis l'écran crée toujours une VehicleCost ; on ne lit donc
        //    QUE les dépenses (pas les MaintenanceLogs, dont ceux sans dépense
        //    liée seraient des résidus). Corrige « Entretiens à 0 » ET l'écart de
        //    montant entre écrans (recette client du 25/08/2026).
        //    On charge TOUTES les dépenses du mois en une fois puis on les ventile
        //    en C#, avec la même règle que le tableau de bord et que les rapports
        //    de coûts : carburant / entretien / AUTRES. Auparavant seul l'entretien
        //    était lu, si bien qu'assurance, vignette, visite technique, carte
        //    grise, péage et réparation-accident n'existaient dans AUCUNE colonne
        //    de ce rapport — mesuré sur la production : 1 161 916 à l'écran
        //    Dépenses contre 9 735 ici pour la même société et la même année
        //    (recette du 08/09/2026).
        var costRows = await _context.VehicleCosts.AsNoTracking()
            .Where(c => c.CompanyId == companyId
                     && vehicleIds.Contains(c.VehicleId)
                     && c.Date >= startDate && c.Date < endDate)
            .Select(c => new { c.VehicleId, c.Type, c.Amount })
            .ToListAsync(ct);

        //    Ventilation partagée (VehicleCostCategory) avec le tableau de bord et
        //    les rapports de coûts : une dépense « repair » va en Réparations et
        //    un remboursement d'assurance est un CRÉDIT. Constat du 14/09/2026 :
        //    ce rapport les comptait en « Autres », en positif, quand les rapports
        //    de coûts les rangeaient en Réparations et en déduction.
        //    Depuis le 18/09/2026 le crédit ne diminue plus « Autres » : il porte
        //    sa propre colonne, comme dans « Coût d'exploitation réel ».
        var classified = costRows
            .Select(c => new
            {
                c.VehicleId,
                Category = VehicleCostCategory.Classify(c.Type).Category,
                IsCredit = VehicleCostCategory.IsCredit(c.Type),
                Amount = VehicleCostCategory.SignedAmount(c.Type, c.Amount)
            })
            .ToList();

        // Les quatre postes sont bâtis sur les seules DÉPENSES : un crédit a sa colonne et
        // ne doit jamais tomber dans un poste, sous peine d'être compté deux fois. Le filtre
        // est posé une fois ici plutôt que répété (et oublié) sur chaque poste.
        var depenses = classified.Where(c => !c.IsCredit).ToList();

        var maintenanceLogs = depenses
            .Where(c => c.Category == CostCategory.Maintenance)
            .Select(c => new { c.VehicleId, ActualCost = c.Amount })
            .ToList();

        var fuelCosts = depenses
            .Where(c => c.Category == CostCategory.Fuel)
            .GroupBy(c => c.VehicleId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        var otherByVehicle = depenses
            .Where(c => c.Category == CostCategory.Other)
            .GroupBy(c => c.VehicleId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        // Avoirs et remboursements : colonne à part, en négatif (18/09/2026).
        var creditByVehicle = classified
            .Where(c => c.IsCredit)
            .GroupBy(c => c.VehicleId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        var repairExpensesByVehicle = depenses
            .Where(c => c.Category == CostCategory.Repair)
            .GroupBy(c => c.VehicleId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        // 4. Fetch repairs from /reparation page's table (repairs)
        var repairs = (await _context.Repairs.AsNoTracking()
            .Where(r => r.SocieteId == companyId
                     && vehicleIds.Contains(r.VehicleId)
                     && r.RepairDate >= startDate
                     && r.RepairDate < endDate)
            .Select(r => new { r.VehicleId, r.TotalCost, r.Status })
            .ToListAsync(ct))
            // Réparation ANNULÉE : aucun atelier n'est passé, aucun euro n'est dû.
            // Le tableau de bord, « Coût d'exploitation réel » et le rapport mensuel
            // flotte l'excluent déjà ; ce rapport la comptait encore et annonçait 325
            // là où les autres écrans affichaient 245 (recette du 13/09/2026). Son
            // relevé compteur est écarté de la même façon par OdometerReadings.
            .Where(r => !string.Equals(r.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 5. Kilometrage du mois affiche, puis du mois PRECEDENT.
        //
        // La regle depend de l’EQUIPEMENT, pas de l’offre commerciale : compteur
        // du boitier quand il y en a un branche au bus CAN (Calypso GPS), sinon
        // releves saisis par le client — pleins, entretiens, reparations,
        // depenses (Calypso GPA, ou vehicule sans boitier). Un vehicule equipe
        // qui n’a pas roule du mois doit afficher 0 km depuis son boitier, et
        // surtout pas basculer sur les saisies.
        var deviceVehicleMap = vehicles
            .Where(v => v.GpsDeviceId.HasValue)
            .ToDictionary(v => v.GpsDeviceId!.Value, v => v.Id);
        var deviceIds = deviceVehicleMap.Keys.ToList();

        const string firstSql = @"
SELECT DISTINCT ON (device_id)
    device_id   AS ""DeviceId"",
    odometer_km AS ""Odo""
FROM gps_positions
WHERE device_id = ANY({0})
  AND recorded_at >= {1}
  AND recorded_at <  {2}
  AND odometer_km IS NOT NULL
  AND odometer_km > 0
  AND odometer_km <> 1048574
ORDER BY device_id, recorded_at ASC;
";
        const string lastSql = @"
SELECT DISTINCT ON (device_id)
    device_id   AS ""DeviceId"",
    odometer_km AS ""Odo""
FROM gps_positions
WHERE device_id = ANY({0})
  AND recorded_at >= {1}
  AND recorded_at <  {2}
  AND odometer_km IS NOT NULL
  AND odometer_km > 0
  AND odometer_km <> 1048574
ORDER BY device_id, recorded_at DESC;
";
        // Une seule implementation, appelee pour le mois courant et pour le
        // precedent : deux copies auraient fini par diverger.
        //
        // ABSENT du dictionnaire = distance NON MESUREE, ce qui n’est pas la meme
        // chose que zero kilometre : un vehicule sans boitier dont les releves du
        // mois n’additionnent aucun kilometre n’a rien a mesurer. Rendu « 0 km »,
        // il annoncait une consommation et un cout au km nuls alors qu’il avait
        // roule 700 km (recette du 13/09/2026).
        async Task<Dictionary<int, (decimal Km, string Source)>> KilometrageAsync(DateTime debut, DateTime finExclue)
        {
            var parVehicule = new Dictionary<int, (decimal Km, string Source)>();
            var depuisBoitier = new HashSet<int>();

            if (deviceIds.Any())
            {
                var deviceIdsArr = deviceIds.ToArray();
                var premiers = await _context.Database
                    .SqlQueryRaw<OdometerEndpoint>(firstSql, deviceIdsArr, debut, finExclue)
                    .ToListAsync(ct);
                var derniers = await _context.Database
                    .SqlQueryRaw<OdometerEndpoint>(lastSql, deviceIdsArr, debut, finExclue)
                    .ToListAsync(ct);

                var mapPremier = premiers.ToDictionary(x => x.DeviceId, x => x.Odo);
                var mapDernier = derniers.ToDictionary(x => x.DeviceId, x => x.Odo);

                foreach (var deviceId in deviceIds)
                {
                    if (mapPremier.TryGetValue(deviceId, out var premier)
                        && mapDernier.TryGetValue(deviceId, out var dernier)
                        && deviceVehicleMap.TryGetValue(deviceId, out var vehicleId))
                    {
                        // Ramene a zero si le compteur du boitier a ete remis a
                        // zero en cours de periode : un nombre negatif serait pire
                        // qu’un kilometrage sous-estime.
                        parVehicule[vehicleId] = (Math.Max(0, dernier - premier), OperatingCostAggregator.SourceGps);
                        depuisBoitier.Add(vehicleId);
                    }
                }
            }

            var sansCompteurBoitier = vehicles
                .Where(v => !depuisBoitier.Contains(v.Id))
                .Select(v => v.Id)
                .ToList();

            if (sansCompteurBoitier.Count > 0)
            {
                var releves = await OdometerReadings.LoadAsync(
                    _context, companyId, sansCompteurBoitier, debut, finExclue, ct);

                foreach (var vehicleId in sansCompteurBoitier)
                {
                    var distance = OdometerDistance.Compute(releves[vehicleId]);
                    // Meme regle que OperatingCostAggregator, la definition de
                    // reference : sans kilometre additionne, pas de distance. Un
                    // releve isole, des releves identiques ou des ecarts tous
                    // rompus rendent « non mesure », et non 0 km — sans quoi ce
                    // rapport et « Cout d'exploitation reel » divergeraient pour
                    // le meme vehicule sur le meme mois.
                    if (distance.Measurable)
                        parVehicule[vehicleId] = (distance.DistanceKm, OperatingCostAggregator.SourceOdometer);
                }
            }

            return parVehicule;
        }

        var mileagePerVehicle = await KilometrageAsync(startDate, endDate);
        var mileagePrevPerVehicle = await KilometrageAsync(startPrevDate, startDate);

        // Litres du mois precedent, pour la colonne « C. PR (L) ».
        var fuelEntriesPrev = await _context.FuelEntries.AsNoTracking()
            .Where(f => f.CompanyId == companyId
                     && f.VehicleId.HasValue
                     && vehicleIds.Contains(f.VehicleId.Value)
                     && f.InvoiceDate >= startPrevDate
                     && f.InvoiceDate < startDate)
            .Select(f => new { f.VehicleId, f.Volume })
            .ToListAsync(ct);

        var litersPrevByVehicle = fuelEntriesPrev
            .Where(f => f.VehicleId.HasValue)
            .GroupBy(f => f.VehicleId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(f => f.Volume));

        // 6. Group fuel entries by vehicle
        var fuelByVehicle = fuelEntries
            .Where(f => f.VehicleId.HasValue)
            .GroupBy(f => f.VehicleId!.Value)
            .ToDictionary(g => g.Key, g => new
            {
                TotalCost = g.Sum(f => f.TotalAmount),
                TotalLiters = g.Sum(f => f.Volume)
            });

        // 7. Group maintenance by vehicle
        var maintByVehicle = maintenanceLogs
            .GroupBy(m => m.VehicleId)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.ActualCost));

        // 8. Group repairs by vehicle
        var repairByVehicle = repairs
            .GroupBy(r => r.VehicleId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.TotalCost));

        // 9. Build per-vehicle rows
        var vehicleRows = new List<VehicleMonthlyCostDto>();

        foreach (var vehicle in vehicles)
        {
            // Distance MESUREE (valeur, eventuellement nulle) ou NON MESUREE (null) :
            // les deux se rendaient « 0 km » avant, et le rapport presentait une
            // absence de mesure comme un vehicule immobile.
            var kmMesure = mileagePerVehicle.TryGetValue(vehicle.Id, out var mesure);
            decimal? km = kmMesure ? mesure.Km : null;
            var kmSource = kmMesure ? mesure.Source : OperatingCostAggregator.SourceNone;
            // Mois precedent, pour les colonnes de comparaison « PR ».
            decimal? kmPrev = mileagePrevPerVehicle.TryGetValue(vehicle.Id, out var mesurePrec) ? mesurePrec.Km : null;
            var litersPrev = litersPrevByVehicle.GetValueOrDefault(vehicle.Id, 0);
            var hasFuel = fuelByVehicle.TryGetValue(vehicle.Id, out var fuel);
            var fuelCost = (hasFuel ? fuel!.TotalCost : 0) + fuelCosts.GetValueOrDefault(vehicle.Id, 0);
            var fuelLiters = hasFuel ? fuel!.TotalLiters : 0;
            var maintCost = maintByVehicle.GetValueOrDefault(vehicle.Id, 0);
            var repairCost = repairByVehicle.GetValueOrDefault(vehicle.Id, 0)
                           + repairExpensesByVehicle.GetValueOrDefault(vehicle.Id, 0);
            var otherCost = otherByVehicle.GetValueOrDefault(vehicle.Id, 0);
            var creditAmount = creditByVehicle.GetValueOrDefault(vehicle.Id, 0);
            var totalCost = fuelCost + maintCost + repairCost + otherCost + creditAmount;

            // Only include vehicles with some activity — sans distance mesuree,
            // GetValueOrDefault vaut 0 : le vehicule reste hors du rapport tant
            // qu'aucune depense ne l'y fait entrer, comme avant. Un avoir SEUL
            // fait entrer le vehicule : sinon la ligne de credit manquerait au
            // tableau alors que le total de la societe la porte.
            if (km.GetValueOrDefault() == 0 && fuelCost == 0 && maintCost == 0 && repairCost == 0
                && otherCost == 0 && creditAmount == 0)
                continue;

            var row = new VehicleMonthlyCostDto
            {
                VehicleId = vehicle.Id,
                VehicleName = vehicle.Name ?? $"{vehicle.Brand} {vehicle.Model}".Trim(),
                Plate = vehicle.Plate,
                DriverName = vehicle.AssignedDriver?.FullName,
                DepartmentId = vehicle.DepartmentId,
                DepartmentName = vehicle.Department?.Name ?? "Non assigné",
                Km = km,
                KmSource = kmSource,
                KmPr = kmPrev,
                FuelCostDzd = fuelCost,
                FuelLiters = fuelLiters,
                FuelLitersPr = litersPrev,
                MaintenanceCostDzd = maintCost,
                RepairCostDzd = repairCost,
                OtherCostDzd = otherCost,
                CreditAmountDzd = creditAmount,
                TotalCostDzd = totalCost,
                // Pas de distance mesuree, pas de ratio : « 0 » se lirait comme
                // une consommation ou un cout au km reellement constates.
                CostPerKm = km > 0 ? Math.Round(totalCost / km.Value, 2) : null,
                FuelPer100Km = km > 0 ? Math.Round((fuelCost / km.Value) * 100, 2) : null,
                MaintenanceRepairPer100Km = km > 0 ? Math.Round(((maintCost + repairCost) / km.Value) * 100, 2) : null,
                ConsumptionPer100Km = km > 0 ? Math.Round((fuelLiters / km.Value) * 100, 2) : null,
                ConsumptionPrPer100Km = kmPrev > 0 ? Math.Round((litersPrev / kmPrev.Value) * 100, 2) : null
            };
            vehicleRows.Add(row);
        }

        // 10. Group by department
        var departmentGroups = vehicleRows
            .GroupBy(v => new { v.DepartmentId, v.DepartmentName })
            .OrderBy(g => g.Key.DepartmentName)
            .Select(g => new DepartmentCostGroupDto
            {
                DepartmentId = g.Key.DepartmentId,
                DepartmentName = g.Key.DepartmentName,
                // Les distances NON mesurees ne pesent rien dans le total : on ne
                // peut pas les inventer. Les ratios du groupe (ComputeRatios)
                // ecartent pour la meme raison les depenses de ces vehicules.
                TotalKm = g.Sum(v => v.Km ?? 0),
                TotalKmPr = g.Sum(v => v.KmPr ?? 0),
                TotalFuelCostDzd = g.Sum(v => v.FuelCostDzd),
                TotalFuelLiters = g.Sum(v => v.FuelLiters),
                TotalFuelLitersPr = g.Sum(v => v.FuelLitersPr),
                TotalMaintenanceCostDzd = g.Sum(v => v.MaintenanceCostDzd),
                TotalRepairCostDzd = g.Sum(v => v.RepairCostDzd),
                TotalOtherCostDzd = g.Sum(v => v.OtherCostDzd),
                TotalCreditAmountDzd = g.Sum(v => v.CreditAmountDzd),
                TotalCostDzd = g.Sum(v => v.TotalCostDzd),
                Vehicles = g.OrderBy(v => v.VehicleName).ToList()
            })
            .ToList();
        foreach (var d in departmentGroups)
            d.ComputeRatios(d.Vehicles);

        // 11. Build final report
        var rapport = new MonthlyCostReportDto
        {
            Year = request.Year,
            Month = request.Month,
            MonthName = startDate.ToString("MMMM yyyy", FrenchCulture),
            ReportPeriod = $"{startDate:dd/MM/yyyy} - {endDate.AddDays(-1):dd/MM/yyyy}",
            GeneratedAt = DateTime.UtcNow,
            TotalKm = vehicleRows.Sum(v => v.Km ?? 0),
            TotalKmPr = vehicleRows.Sum(v => v.KmPr ?? 0),
            TotalFuelCostDzd = vehicleRows.Sum(v => v.FuelCostDzd),
            TotalFuelLiters = vehicleRows.Sum(v => v.FuelLiters),
            TotalFuelLitersPr = vehicleRows.Sum(v => v.FuelLitersPr),
            TotalMaintenanceCostDzd = vehicleRows.Sum(v => v.MaintenanceCostDzd),
            TotalRepairCostDzd = vehicleRows.Sum(v => v.RepairCostDzd),
            TotalOtherCostDzd = vehicleRows.Sum(v => v.OtherCostDzd),
            TotalCreditAmountDzd = vehicleRows.Sum(v => v.CreditAmountDzd),
            TotalCostDzd = vehicleRows.Sum(v => v.TotalCostDzd),
            Departments = departmentGroups,
            Vehicles = vehicleRows.OrderBy(v => v.DepartmentName).ThenBy(v => v.VehicleName).ToList()
        };
        rapport.ComputeRatios(rapport.Vehicles);
        return rapport;
    }

    /// <summary>
    /// Carrier for the chronological-first / chronological-last odometer
    /// raw-SQL queries. Public because Npgsql's <c>SqlQueryRaw&lt;T&gt;</c>
    /// requires a concrete public type.
    /// </summary>
    public class OdometerEndpoint
    {
        public int DeviceId { get; set; }
        public long Odo { get; set; }
    }
}
