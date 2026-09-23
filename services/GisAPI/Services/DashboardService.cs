using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.AcquisitionPayments;
using GisAPI.Application.Features.Repairs;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Vehicles;

namespace GisAPI.Services;

/// <summary>
/// Calcul du dashboard « tout-en-un » (/api/dashboard/all), extrait du contrôleur
/// pour être réutilisable par le pré-chauffage en arrière-plan (DashboardPrewarmService).
/// La logique de calcul est INCHANGÉE : seules l'entrée (paramètres explicites au
/// lieu des claims) et la sortie (renvoie l'objet, le cache est géré par l'appelant)
/// diffèrent.
/// </summary>
public interface IDashboardService
{
    Task<object> ComputeDashboardAllAsync(int companyId, int userId, bool isAdmin, string period,
        bool isCustomRange, DateTime periodStart, DateTime periodEnd, DateTime prevStart, DateTime prevEnd,
        DateTime now, DateTime today, CancellationToken ct);
}

public class DashboardService : IDashboardService
{
    private readonly GisDbContext _context;
    private readonly IVehicleHealthScoreService _healthService;
    private readonly IFuelCalculationService _fuelCalcService;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(GisDbContext context, IVehicleHealthScoreService healthService,
        IFuelCalculationService fuelCalcService, ILogger<DashboardService> logger)
    {
        _context = context;
        _healthService = healthService;
        _fuelCalcService = fuelCalcService;
        _logger = logger;
    }

    public async Task<object> ComputeDashboardAllAsync(int companyId, int userId, bool isAdmin, string period,
        bool isCustomRange, DateTime periodStart, DateTime periodEnd, DateTime prevStart, DateTime prevEnd,
        DateTime now, DateTime today, CancellationToken ct)
    {
        // ── 1. Load vehicles once (shared across sections) ──
        var vehicleQuery = _context.Vehicles
            .AsNoTracking()
            .Include(v => v.GpsDevice)
            .Where(v => v.CompanyId == companyId)
            .AsQueryable();

        // Portée véhicules — MÊME sémantique que VehicleScope (la définition unique
        // utilisée par les écrans Carburant, Échéances et les rapports) :
        //   null       = l'appelant voit tout le parc (admin de société) ;
        //   liste vide = AUCUN véhicule visible, donc des résultats vides —
        //                surtout pas l'absence de filtre.
        // Le service ne peut pas appeler VehicleScope directement : il tourne aussi
        // hors requête (pré-chauffage), sans contexte tenant, d'où isAdmin/userId
        // passés explicitement par l'appelant (voir ScopeIdsAsync).
        // Avant le 09/09/2026 la portée n'était retenue que si l'utilisateur avait
        // au moins une affectation, et n'était appliquée qu'au poste « acquisition » :
        // le total du tableau de bord additionnait donc tout le parc alors que la
        // liste des véhicules, elle, était restreinte — incohérent avec lui-même et
        // avec les rapports.
        // La portée s'applique à TOUTE section rattachée à un véhicule, et pas aux
        // seules dépenses : distance de la période, trajets récents et conducteurs
        // affichent une PLAQUE, donc restreindre les coûts sans les restreindre
        // laissait fuiter le reste du parc juste au-dessus.
        var scopeIds = await ScopeIdsAsync(_context, isAdmin, userId, ct);
        if (scopeIds is not null)
        {
            List<int> ids = scopeIds;
            vehicleQuery = vehicleQuery.Where(v => ids.Contains(v.Id));
        }

        var vehicles = await vehicleQuery.ToListAsync();

        // Les cinq compteurs renvoyés (arrêt, contact mis, en circulation,
        // maintenance, sans GPS) sont additionnés par l'écran pour former le
        // total du parc ET le dénominateur des pourcentages. Ils doivent donc
        // partitionner la flotte, sans recouvrement : un véhicule à la fois sans
        // boîtier ET en maintenance était compté deux fois, ce qui gonflait le
        // parc — de façon maximale chez un client sans GPS, où TOUS les
        // véhicules tombent dans « sans GPS ».
        // La maintenance prime : c'est un état posé par l'exploitant.
        var maintenanceVehicles = vehicles.Count(v => v.Status == "maintenance");
        var noGpsVehicles = vehicles.Count(v => !v.GpsDeviceId.HasValue && v.Status != "maintenance");
        var deviceMap = vehicles.Where(v => v.GpsDeviceId.HasValue)
            .ToDictionary(v => v.GpsDeviceId!.Value, v => v);

        // ── 2. Vehicle status from last GPS position ──
        int movingCount = 0, ignitionOnCount = 0, stoppedCount = 0;
        var gpsVehicles = vehicles.Where(v => v.GpsDeviceId.HasValue && v.Status != "maintenance").ToList();
        var gpsDeviceIds = gpsVehicles.Select(v => v.GpsDeviceId!.Value).ToList();
        var latestPositions = new Dictionary<int, GpsPosition>();

        if (gpsDeviceIds.Any())
        {
            // BORNE TEMPORELLE (30 min) : la classification ne compte de toute façon
            // que les positions fraîches (voir le filtre plus bas). Sans cette borne,
            // le GROUP BY device_id + Max(id) scannait TOUT l'historique de chaque
            // boîtier sur gps_positions (13 Go) → 700-800 ms et l'un des principaux
            // contributeurs aux >80 s du dashboard. Avec la borne, l'index
            // (device_id, recorded_at) ne lit que les rares trames récentes.
            var freshSince = now.AddMinutes(-30);
            var latestPosIds = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => gpsDeviceIds.Contains(p.DeviceId) && p.RecordedAt >= freshSince)
                .GroupBy(p => p.DeviceId)
                .Select(g => g.Max(p => p.Id))
                .ToListAsync();

            latestPositions = await _context.GpsPositions
                .AsNoTracking()
                .Where(p => latestPosIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.DeviceId);

            // A vehicle only counts as "moving"/"ignition on" if its LAST fix is RECENT.
            // Without this freshness gate, a vehicle whose last-ever frame had speed>3 but
            // stopped reporting hours/days ago kept showing as "en circulation" (phantom movement).
            foreach (var v in gpsVehicles)
            {
                if (latestPositions.TryGetValue(v.GpsDeviceId!.Value, out var pos) && pos.RecordedAt >= freshSince)
                {
                    var speed = pos.SpeedKph ?? 0;
                    var ignition = pos.IgnitionOn ?? false;
                    if (speed > 3) movingCount++;
                    else if (ignition) ignitionOnCount++;
                    else stoppedCount++;
                }
                else stoppedCount++; // no fix OR stale fix → not actively moving
            }
        }

        // ── 3. Expenses (use nullable Sum to safely handle empty result sets) ──
        decimal fuelCost = 0, maintenanceCost = 0, repairCost = 0, otherCost = 0;
        // Achats véhicule : mensualités de crédit/leasing échues + apports/achats
        // datés dans la période — recette client du 04/09/2026 : le « Coût
        // total » ignorait tout ce qui n'était pas une ligne en base. Depuis le
        // 07/09/2026 la source est l'échéancier PERSISTÉ (acquisition_payments,
        // règle de comptage unique AcquisitionPaymentRules), avec repli sur le
        // calcul à la volée pour les véhicules jamais synchronisés.
        var acquisitionCost = await AcquisitionCostAsync(companyId, scopeIds, vehicles, periodStart, periodEnd, now, ct);
        try
        {
            (fuelCost, maintenanceCost, repairCost, otherCost) =
                await PeriodCostsAsync(_context, companyId, scopeIds, periodStart, periodEnd, ct);
        }
        // Une ANNULATION doit remonter, pas être absorbée : le résultat serait
        // partiel (coûts à 0) et le cache le servirait pendant dix minutes à tous
        // les utilisateurs de la société. Le filet ne couvre que les vraies pannes
        // de calcul, comme celui d'AcquisitionCostAsync.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Dashboard : calcul des dépenses impossible pour la société {CompanyId}", companyId);
        }

        // ── 4. Driving scores (alerts per vehicle in period) ──
        var alertsByVehicle = await _context.GpsAlerts.AsNoTracking()
            .Where(a => a.VehicleId.HasValue && a.Vehicle!.CompanyId == companyId &&
                        a.Timestamp >= periodStart && a.Timestamp <= periodEnd)
            .GroupBy(a => a.VehicleId!.Value)
            .Select(g => new { VehicleId = g.Key, AlertCount = g.Count() })
            .ToListAsync();

        // Distance actually driven per vehicle IN THE PERIOD (completed trips).
        // Reused below for the km ranking AND to know which vehicles were active.
        // Portée : la carte « Kilométrage », le classement des km et la ventilation
        // par type sortent tous d'ici — sans le filtre, un employé restreint lisait
        // les km de TOUT le parc au-dessus de coûts, eux, restreints.
        var periodTripsQuery = _context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.Status == "completed" &&
                        t.StartTime >= periodStart && t.StartTime <= periodEnd);
        if (scopeIds is not null)
        {
            List<int> ids = scopeIds;
            periodTripsQuery = periodTripsQuery.Where(t => ids.Contains(t.VehicleId));
        }

        var tripKmByVehicle = await periodTripsQuery
            .GroupBy(t => t.VehicleId)
            .Select(g => new { VehicleId = g.Key, Km = g.Sum(t => t.DistanceKm) })
            .ToListAsync(ct);

        // Only score vehicles that were ACTUALLY ACTIVE in the period (drove or raised alerts).
        // Otherwise parked / GPS-less vehicles get a perfect 100 (0 alerts) and dominate the top.
        var activeVehicleIds = new HashSet<int>(tripKmByVehicle.Where(x => x.Km > 0).Select(x => x.VehicleId));
        activeVehicleIds.UnionWith(alertsByVehicle.Select(a => a.VehicleId));

        var drivingScores = vehicles
            .Where(v => activeVehicleIds.Contains(v.Id))
            .Select(v =>
            {
                var alerts = alertsByVehicle.FirstOrDefault(a => a.VehicleId == v.Id)?.AlertCount ?? 0;
                var score = Math.Max(0, 100 - (alerts * 5));
                return new { plate = v.Plate ?? v.Name, score };
            })
            .OrderByDescending(x => x.score)
            .ToList();

        // ── 5. Vehicle health ──
        // CalculateAllScoresAsync note TOUT le parc de la société : sans portée, les
        // compteurs Sain/Attention/Critique d'un locataire HERTZ affecté à 2 véhicules
        // totalisaient les 307 du parc. Le filtre s'applique au RÉSULTAT, en mémoire,
        // plutôt qu'en changeant la signature partagée d'IVehicleHealthScoreService
        // (l'assistant IA l'appelle aussi) — et le service renvoie déjà VehicleId.
        var healthResults = ScopedHealthResults(await _healthService.CalculateAllScoresAsync(companyId), scopeIds);
        var healthy = healthResults.Count(h => h.Score >= 80);
        var attention = healthResults.Count(h => h.Score >= 40 && h.Score < 80);
        var unhealthy = healthResults.Count(h => h.Score < 40);

        // ── 6. Top km units — distance ACTUALLY DRIVEN in the selected period (completed trips),
        //       not the lifetime odometer (which never changed when you switched day/week/month). ──
        var topUnitsColors = new[] { "#3b82f6", "#22c55e", "#f97316", "#8b5cf6", "#06b6d4", "#ec4899", "#eab308", "#14b8a6" };
        var vehicleById = vehicles.ToDictionary(v => v.Id);
        var topUnits = tripKmByVehicle
            .Where(x => x.Km > 0 && vehicleById.ContainsKey(x.VehicleId))
            .OrderByDescending(x => x.Km)
            .Take(20)
            .Select((x, i) => new
            {
                name = vehicleById[x.VehicleId].Plate ?? vehicleById[x.VehicleId].Name,
                color = topUnitsColors[i % topUnitsColors.Length],
                // Distance PARCOURUE sur la periode demandee. Publie sous le nom
                // `mileage` depuis l'origine, alors que ce mot designe ailleurs le
                // compteur de vie du vehicule (Monitoring, fiche vehicule) : deux
                // grandeurs sous un seul mot, et c'est cette lecture-la que le
                // client a contestee. `periodKm` nomme la grandeur sans ambiguite.
                periodKm = Math.Round((double)x.Km),
                // Conserve pour compatibilite : l'application mobile lit encore
                // `mileage`. A retirer quand plus aucune version en service ne le lit.
                mileage = Math.Round((double)x.Km)
            })
            .ToList();

        // ── 7. Geofences — number of PASSAGES (entry/exit events) in the period,
        //       not the static count of assigned vehicles (which never reflected activity). ──
        var geoColors = new[] { "#22c55e", "#3b82f6", "#f97316", "#06b6d4", "#8b5cf6", "#ec4899", "#eab308", "#14b8a6" };
        // Portée de la LISTE des zones — règle du 23/09/2026, qui CORRIGE la consigne
        // de la veille (« garder la liste entière, ne cloisonner que les compteurs ») :
        // chez un loueur, un client « ne devrait pas voir les géofences, ni recevoir de
        // notifications de ces géofences ou d'autres véhicules que les siens ».
        // Voir VisibleGeofencesAsync pour la règle exacte et pour le fait de production
        // à ne pas prendre pour une régression (HERTZ n'a aucune liaison zone↔véhicule).
        var geofencesRaw = await VisibleGeofencesAsync(_context, companyId, userId, scopeIds, ct);
        // Portée des COMPTEURS de passages : inchangée — un passage est l'entrée ou la
        // sortie d'un VÉHICULE, il suit donc la portée véhicules de l'appelant.
        var geoCountMap = await GeofencePassageCountsAsync(_context, companyId, scopeIds, periodStart, periodEnd, ct);
        var geofencesList = geofencesRaw
            .Select((g, i) => new { name = g.Name, color = geoColors[i % geoColors.Length], count = geoCountMap.GetValueOrDefault(g.Id, 0) })
            .ToList();

        // ── 8. Alerts (GpsAlerts + Notifications merged) ──
        // C'est LE symptôme signalé par HERTZ (« il reçoit les notifications de tout
        // le parc ») : un locataire affecté à 2 véhicules sur 307 lisait ici les
        // alertes des 305 autres. Voir AlertFeedAsync pour la règle exacte.
        // La forme publiée reste { message, severity, time } : l'écran la lit telle quelle.
        var mergedAlerts = (await AlertFeedAsync(_context, companyId, userId, scopeIds, ct))
            .Select(a => new { message = a.Message, severity = a.Severity, time = a.Timestamp.ToString("dd/MM HH:mm") })
            .ToList();

        // ── 9. Recent trips ──
        // La liste affiche la PLAQUE : sans la portée, un employé restreint voyait
        // passer les 20 derniers trajets de toute la société.
        var recentTripsQuery = _context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId);
        if (scopeIds is not null)
        {
            List<int> ids = scopeIds;
            recentTripsQuery = recentTripsQuery.Where(t => ids.Contains(t.VehicleId));
        }

        var trips = await recentTripsQuery
            .OrderByDescending(t => t.StartTime)
            .Take(20)
            .Include(t => t.Vehicle)
            .ToListAsync(ct);

        var tripsList = trips.Select(t =>
        {
            var dist = (t.DistanceKm).ToString("F1");
            var mins = t.DurationMinutes;
            var dur = mins >= 60 ? $"{mins / 60}h{(mins % 60).ToString().PadLeft(2, '0')}" : $"{mins} min";
            var plate = t.Vehicle?.Plate ?? t.Vehicle?.Name ?? "Vehicule";
            return new { plate, distance = dist, duration = dur, date = t.StartTime.ToString("dd/MM HH:mm") };
        }).ToList();

        // ── 10. Drivers ──
        // Driver.AssignedVehicle is NOT a nav (EF would collide with Vehicle.AssignedDriver).
        // Manual join on AssignedVehicleId to pull the vehicle plate.
        // Portée : chaque ligne porte la plaque du véhicule affecté au conducteur ;
        // un employé restreint ne voit donc que les conducteurs de SES véhicules
        // (un conducteur sans véhicule affecté n'appartient à aucune portée, il ne
        // s'affiche que pour un admin — même règle que le plein sans véhicule).
        var driversQuery = _context.Drivers.AsNoTracking()
            .Where(d => d.CompanyId == companyId);
        if (scopeIds is not null)
        {
            List<int> ids = scopeIds;
            driversQuery = driversQuery.Where(d => d.AssignedVehicleId != null && ids.Contains(d.AssignedVehicleId.Value));
        }

        var driversRaw = await (from d in driversQuery
                                join v in _context.Vehicles.AsNoTracking()
                                    on d.AssignedVehicleId equals v.Id into vJoin
                                from vehicle in vJoin.DefaultIfEmpty()
                                select new
                                {
                                    d.FirstName,
                                    d.LastName,
                                    d.Status,
                                    VehiclePlate = vehicle != null ? vehicle.Plate : null
                                })
                                .Take(20)
                                .ToListAsync(ct);

        var driversList = driversRaw.Select(d =>
        {
            var fullName = $"{d.FirstName} {d.LastName}".Trim();
            var name = !string.IsNullOrWhiteSpace(fullName) ? fullName : "Conducteur";
            var initials = string.Join("", name.Split(' ').Where(w => w.Length > 0).Select(w => w[0].ToString().ToUpper()));
            if (initials.Length > 2) initials = initials[..2];
            return new
            {
                name,
                initials,
                vehicle = d.VehiclePlate ?? "",
                active = d.Status != "inactive"
            };
        }).ToList();

        // ── 11. Fuel consumption (FuelCalculationService) ──
        // En plage personnalisée, le graphique couvre la plage demandée (bornée au
        // présent et à 90 jours de calcul) au lieu des N derniers jours glissants.
        int fuelDays;
        DateTime fuelEndDate;
        if (isCustomRange)
        {
            fuelEndDate = periodEnd > now ? now : periodEnd;
            fuelDays = Math.Clamp((fuelEndDate.Date - periodStart.Date).Days + 1, 1, 90);
        }
        else
        {
            fuelEndDate = now;
            fuelDays = period switch { "today" => 1, "yesterday" => 1, "week" => 7, "quarter" => 90, _ => 30 };
        }
        var fuelStartDate = fuelEndDate.AddDays(-fuelDays);

        var fuelPrices = await _context.FuelPricings
            .Where(fp => fp.CompanyId == companyId && fp.IsActive &&
                         fp.EffectiveFrom <= now && (fp.EffectiveTo == null || fp.EffectiveTo > now))
            .Join(_context.FuelTypes, fp => fp.FuelTypeId, ft => ft.Id,
                  (fp, ft) => new { ft.Code, fp.PricePerLiter })
            .ToListAsync();
        var priceDict = fuelPrices.ToDictionary(p => p.Code.ToLower(), p => p.PricePerLiter);

        // Batch fuel calculation: 3 SQL queries instead of N*4 per-vehicle
        var batchFuelResults = await _fuelCalcService.CalculateFleetFuelBatchAsync(
            vehicles.Where(v => v.GpsDeviceId.HasValue).ToList(), fuelStartDate, fuelEndDate, priceDict);

        var vehicleFuelStats = batchFuelResults
            .Select(e => new
            {
                plate = e.Plate ?? e.VehicleName,
                consumption = e.AverageConsumptionPer100Km,
                totalLiters = e.TotalFuelConsumedLiters,
                totalKm = e.TotalDistanceKm
            })
            .OrderByDescending(v => v.consumption)
            .Cast<object>()
            .ToList();

        decimal fleetTotalLiters = batchFuelResults.Sum(e => e.TotalFuelConsumedLiters);
        int fleetTotalKm = batchFuelResults.Sum(e => e.TotalDistanceKm);
        var dailyFleetFuel = new Dictionary<string, decimal>();
        foreach (var expense in batchFuelResults)
        {
            foreach (var d in expense.DailyConsumption)
            {
                var dayKey = d.Date.ToString("yyyy-MM-dd");
                dailyFleetFuel[dayKey] = dailyFleetFuel.GetValueOrDefault(dayKey) + d.FuelConsumedLiters;
            }
        }

        // Vehicules SANS boitier : la courbe est alimentee par les pleins saisis
        // dans le menu Carburant (recette client du 26/08/2026 — l'abonnement
        // « gestion sans GPS » voyait un graphique vide alors que les pleins
        // etaient enregistres). Ce sont des litres factures, pas une estimation.
        var manualVehicleIds = vehicles.Where(v => !v.GpsDeviceId.HasValue).Select(v => v.Id).ToList();
        if (manualVehicleIds.Count > 0)
        {
            var manualDaily = await _context.FuelEntries.AsNoTracking()
                .Where(f => f.CompanyId == companyId && f.VehicleId != null
                            && manualVehicleIds.Contains(f.VehicleId.Value)
                            && f.InvoiceDate >= fuelStartDate && f.InvoiceDate <= fuelEndDate)
                .GroupBy(f => f.InvoiceDate.Date)
                .Select(g => new { Day = g.Key, Liters = g.Sum(x => (decimal?)x.Volume) ?? 0m })
                .ToListAsync();
            foreach (var m in manualDaily)
            {
                var dayKey = m.Day.ToString("yyyy-MM-dd");
                dailyFleetFuel[dayKey] = dailyFleetFuel.GetValueOrDefault(dayKey) + m.Liters;
            }
            fleetTotalLiters += manualDaily.Sum(m => m.Liters);
        }

        var chartDays = Enumerable.Range(0, Math.Min(fuelDays, 30))
            .Select(i => fuelEndDate.AddDays(-((Math.Min(fuelDays, 30) - 1) - i)).ToString("yyyy-MM-dd"))
            .ToList();
        var chartValues = chartDays.Select(d => Math.Round(dailyFleetFuel.GetValueOrDefault(d), 2)).ToList();

        // ── 12. Trends vs previous period (cheap comparisons for the KPI ▲▼ deltas) ──
        decimal prevTotalCost = 0;
        try
        {
            // MÊME définition et MÊME portée que la période courante : la période
            // précédente lisait en plus les MaintenanceLogs alors que la période
            // courante ne compte que les VehicleCosts « maintenance » — l'entretien
            // était donc compté DEUX FOIS dans le passé (chaque entretien saisi crée
            // une dépense ET un journal qui la référence), ce qui écrasait
            // artificiellement la flèche de tendance vers le bas.
            var (prevFuel, prevMaintenance, prevRepair, prevOther) =
                await PeriodCostsAsync(_context, companyId, scopeIds, prevStart, prevEnd, ct);
            prevTotalCost = prevFuel + prevMaintenance + prevRepair + prevOther
                + await AcquisitionCostAsync(companyId, scopeIds, vehicles, prevStart, prevEnd, now, ct);
        }
        // Best-effort, MAIS jamais au prix d'une annulation avalée (cf. § 3).
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Dashboard : tendance de coût indisponible pour la société {CompanyId}", companyId);
        }
        var currentTotalCost = fuelCost + maintenanceCost + repairCost + otherCost + acquisitionCost;

        var currentDistance = tripKmByVehicle.Sum(x => x.Km);
        // MÊME portée que la distance de la période courante : sinon la flèche de
        // tendance comparait les km de l'employé à ceux de toute la société.
        var prevTripsQuery = _context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.Status == "completed" &&
                        t.StartTime >= prevStart && t.StartTime <= prevEnd);
        if (scopeIds is not null)
        {
            List<int> ids = scopeIds;
            prevTripsQuery = prevTripsQuery.Where(t => ids.Contains(t.VehicleId));
        }
        var prevDistance = await prevTripsQuery
            .Select(t => (decimal?)t.DistanceKm).SumAsync(ct) ?? 0m;

        static double Pct(decimal cur, decimal prev) => prev > 0 ? Math.Round((double)(cur - prev) / (double)prev * 100, 1) : 0.0;
        var costTrend = Pct(currentTotalCost, prevTotalCost);
        var distanceTrend = Pct(currentDistance, prevDistance);

        // ── 13. Fleet breakdown by vehicle type (count + km driven in the period) ──
        var typeColors = new[] { "#6366f1", "#10b981", "#f59e0b", "#0ea5e9", "#ec4899", "#8b5cf6", "#14b8a6", "#ef4444" };
        var typeBreakdown = vehicles
            .GroupBy(v => string.IsNullOrWhiteSpace(v.Type) ? "Autre" : v.Type!)
            .Select(g => new
            {
                type = g.Key,
                count = g.Count(),
                km = Math.Round(g.Sum(v => (double)(tripKmByVehicle.FirstOrDefault(t => t.VehicleId == v.Id)?.Km ?? 0m)))
            })
            .OrderByDescending(x => x.count)
            .Take(8)
            .Select((x, i) => new { x.type, x.count, x.km, color = typeColors[i % typeColors.Length] })
            .ToList();

        // ── Build response ──
        var result = new
        {
            vehicleStatus = new
            {
                stopped = stoppedCount,
                ignitionOn = ignitionOnCount,
                moving = movingCount,
                maintenance = maintenanceVehicles,
                noGps = noGpsVehicles
            },
            expenses = new
            {
                fuelCost,
                maintenanceCost,
                repairCost,
                otherCost,
                acquisitionCost,
                totalCost = fuelCost + maintenanceCost + repairCost + otherCost + acquisitionCost
            },
            fuelConsumption = new
            {
                vehicleStats = vehicleFuelStats,
                fleetTotalLiters = Math.Round(fleetTotalLiters, 2),
                fleetTotalKm,
                chartDays,
                chartValues,
                estimated = batchFuelResults.Count > 0 // capteur GPS -> estime ; pleins saisis seuls -> chiffres factures
            },
            drivingScores,
            healthData = new { healthy, attention, unhealthy },
            topUnits,
            geofences = geofencesList,
            alerts = mergedAlerts,
            recentTrips = tripsList,
            drivers = driversList,
            trends = new { cost = costTrend, distance = distanceTrend },
            // Meme chiffre sous deux noms : `periodKm` dit la grandeur (distance
            // parcourue SUR LA PERIODE demandee), `periodDistance` reste publie
            // pour ne pas casser les clients deja livres.
            periodKm = Math.Round((double)currentDistance),
            periodDistance = Math.Round((double)currentDistance),
            typeBreakdown
        };

        return result;
    }

    /// <summary>
    /// Portée véhicules du tableau de bord, MÊME sémantique que
    /// <c>VehicleScope.AccessibleVehicleIdsAsync</c> — la définition unique des
    /// écrans Carburant, Échéances et des rapports :
    ///   • <c>null</c> = l'appelant voit tout le parc (admin de société) ;
    ///   • liste VIDE = aucun véhicule visible, donc des résultats vides ;
    ///   • sinon les véhicules affectés (table user_vehicles).
    /// Méthode à part (et publique) parce que le service tourne AUSSI hors requête
    /// HTTP — pré-chauffage en arrière-plan, sans <c>ICurrentTenantService</c> —,
    /// d'où isAdmin/userId passés explicitement plutôt que lus dans les claims.
    /// C'est cette méthode que les tests appellent : la portée n'est plus recopiée.
    /// </summary>
    public static async Task<List<int>?> ScopeIdsAsync(IGisDbContext context, bool isAdmin, int userId, CancellationToken ct)
    {
        if (isAdmin) return null;
        // Non identifié : rien de visible (fail-closed). Avant le 09/09/2026,
        // userId = 0 sans le drapeau admin valait « tout le parc ».
        if (userId <= 0) return new List<int>();

        return await context.UserVehicles.AsNoTracking()
            .Where(uv => uv.UserId == userId)
            .Select(uv => uv.VehicleId)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Une ligne du bloc « Alertes » du tableau de bord, avant mise en forme :
    /// libellé, gravité déjà traduite (danger / warning / info) et horodatage brut
    /// (c'est lui qui ordonne la fusion des deux sources).
    /// </summary>
    public record DashboardAlert(string Message, string Severity, DateTime Timestamp);

    /// <summary>
    /// Les 20 dernières lignes du bloc « Alertes », fusion de DEUX sources qui ne se
    /// cloisonnent PAS de la même façon (incident de confidentialité HERTZ du
    /// 23/09/2026 — un locataire affecté à 2 véhicules sur 307 lisait ici tout le parc) :
    ///   • <c>gps_alerts</c> porte un VÉHICULE : elle suit la portée véhicules
    ///     (<paramref name="scopeIds"/>), exactement comme les trajets récents ;
    ///   • <c>notifications</c> porte un DESTINATAIRE : une notification appartient à
    ///     son utilisateur, et la cloche le sait déjà
    ///     (<c>GetNotificationsQueryHandler</c> filtre sur <c>UserId</c>) — le tableau
    ///     de bord, lui, ne filtrait que sur la société.
    /// Les deux filtres ne s'appliquent QUE si <paramref name="scopeIds"/> n'est pas
    /// <c>null</c>, et pour deux raisons distinctes :
    ///   • décision produit du 23/09/2026 : pour un ADMINISTRATEUR on ne change RIEN,
    ///     il continue de voir les alertes ET les notifications de toute la société ;
    ///   • le pré-chauffage appelle le service avec <c>userId: 0, isAdmin: true</c> :
    ///     un filtre inconditionnel sur <c>UserId == 0</c> viderait l'entrée admin
    ///     mise en cache, celle que reçoivent tous les admins de la société.
    /// Statique et prenant le contexte en paramètre pour être appelable telle quelle
    /// par les tests — aucune copie de la règle ailleurs.
    /// </summary>
    public static async Task<List<DashboardAlert>> AlertFeedAsync(
        IGisDbContext context, int companyId, int userId, List<int>? scopeIds, CancellationToken ct)
    {
        var alertsQuery = context.GpsAlerts.AsNoTracking()
            .Where(a => a.VehicleId.HasValue && a.Vehicle!.CompanyId == companyId);
        var notificationsQuery = context.Notifications.AsNoTracking()
            .Where(n => n.CompanyId == companyId);

        if (scopeIds is not null)
        {
            List<int> ids = scopeIds;
            alertsQuery = alertsQuery.Where(a => ids.Contains(a.VehicleId!.Value));
            notificationsQuery = notificationsQuery.Where(n => n.UserId == userId);
        }

        var gpsAlerts = await alertsQuery
            .OrderByDescending(a => a.Timestamp)
            .Take(20)
            .Select(a => new { message = a.Message ?? "Alerte", severity = a.Severity ?? "info", ts = a.Timestamp })
            .ToListAsync(ct);

        var notifications = await notificationsQuery
            .OrderByDescending(n => n.CreatedAt)
            .Take(20)
            .Select(n => new { message = n.Title ?? "Notification", severity = n.Priority, ts = n.CreatedAt })
            .ToListAsync(ct);

        return gpsAlerts
            .Select(a => new DashboardAlert(
                a.message,
                a.severity == "critical" ? "danger" : a.severity == "warning" ? "warning" : "info",
                a.ts))
            .Concat(notifications.Select(n => new DashboardAlert(
                n.message,
                n.severity == "high" || n.severity == "critical" ? "danger" : n.severity == "medium" ? "warning" : "info",
                n.ts)))
            .OrderByDescending(a => a.Timestamp)
            .Take(20)
            .ToList();
    }

    /// <summary>
    /// Restreint les scores de santé (calculés pour TOUTE la société) à la portée
    /// véhicules de l'appelant : <c>null</c> = tout le parc, liste vide = rien.
    /// Filtrage en mémoire à dessein — la signature d'<c>IVehicleHealthScoreService</c>
    /// est partagée (assistant IA, fiche véhicule) et ne doit pas bouger pour ça.
    /// </summary>
    public static List<VehicleHealthResult> ScopedHealthResults(List<VehicleHealthResult> results, List<int>? scopeIds)
    {
        if (scopeIds is null) return results;
        var ids = new HashSet<int>(scopeIds);
        return results.Where(h => ids.Contains(h.VehicleId)).ToList();
    }

    /// <summary>
    /// Nombre de PASSAGES (entrées/sorties) par géofence sur [from, to], dans la
    /// portée véhicules de l'appelant. Un passage est le franchissement d'un
    /// VÉHICULE : il se cloisonne sur <paramref name="scopeIds"/> (null = tout le
    /// parc, liste vide = rien). La LISTE des zones, elle, obéit à une autre règle —
    /// voir <see cref="VisibleGeofenceIdsAsync"/>.
    /// </summary>
    public static async Task<Dictionary<int, int>> GeofencePassageCountsAsync(
        IGisDbContext context, int companyId, List<int>? scopeIds, DateTime from, DateTime to, CancellationToken ct)
    {
        var eventsQuery = context.GeofenceEvents.AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Timestamp >= from && e.Timestamp <= to);

        if (scopeIds is not null)
        {
            List<int> ids = scopeIds;
            eventsQuery = eventsQuery.Where(e => ids.Contains(e.VehicleId));
        }

        var counts = await eventsQuery
            .GroupBy(e => e.GeofenceId)
            .Select(g => new { GeofenceId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return counts.ToDictionary(x => x.GeofenceId, x => x.Count);
    }

    /// <summary>Une géozone telle que le tableau de bord la publie : identifiant et libellé.</summary>
    public record DashboardGeofence(int Id, string Name);

    /// <summary>
    /// Identifiants des géozones VISIBLES par l'appelant, ou <c>null</c> lorsqu'il
    /// les voit toutes (le filtre société suffit alors). Une liste VIDE signifie
    /// « aucune zone visible » et doit produire un résultat vide.
    ///
    /// RÈGLE DU 23/09/2026 (décidée par Slim, capture d'écran à l'appui). Elle
    /// REMPLACE la consigne de la veille, qui laissait la liste des zones entière et
    /// ne cloisonnait que les compteurs de passages : au sens métier c'était faux,
    /// chez un loueur un client « ne devrait pas voir les géofences, ni recevoir de
    /// notifications de ces géofences ou d'autres véhicules que les siens ».
    ///   • administrateur (portée <c>null</c>) : RIEN ne change, toutes les zones
    ///     de sa société ;
    ///   • utilisateur restreint AVEC la case Géofences (<c>users.can_geofences</c>) :
    ///       zones rattachées à au moins un de SES véhicules (<c>geofence_vehicles</c>)
    ///       UNION zones rattachées à AUCUN véhicule ;
    ///   • utilisateur restreint SANS la case Géofences : AUCUNE zone, même rattachée
    ///     (règle de Slim pour Kap Pharma, inchangée) ;
    ///   • utilisateur restreint sans aucun véhicule affecté (portée VIDE) : AUCUNE
    ///     zone — la liste vide veut dire « il ne voit rien », jamais « tout ».
    /// Ce qui reste INTERDIT : voir une zone rattachée UNIQUEMENT aux véhicules
    /// d'autres utilisateurs.
    ///
    /// AJUSTEMENT DU 23/09/2026 (même jour) — les zones SANS liaison. La première
    /// version ne montrait à un restreint que les zones rattachées à ses véhicules.
    /// Or une zone NEUVE n'est rattachée à rien : dès que l'écriture a été rendue aux
    /// gestionnaires de zones (voir <c>GeofencesController</c>), un Opérateur de
    /// SICOAC qui crée une zone l'aurait vue disparaître sous ses yeux. Les faits de
    /// production relevés ce jour-là : 17 comptes NON administrateurs ont la case
    /// Géofences et/ou Entretien et/ou Paramètres — SICOAC en a 7 (rôle « Operateur »,
    /// 11 véhicules affectés sur 11 pour six d'entre eux, 5 sur 11 pour le
    /// septième), PARENIN 2, EXALTIS 1, BELIVE plusieurs : ce sont eux qui gèrent les
    /// zones de leur société au quotidien. Une zone sans liaison est une zone DE
    /// SOCIÉTÉ — la surveillance l'applique d'ailleurs à TOUT le parc
    /// (<c>BroadcastPositionCommandHandler.ComputeInsideZones</c>) — et elle se gère
    /// par ceux qui ont la case. Les compteurs de passages, eux, restent cloisonnés
    /// aux véhicules de la portée (voir les appelants) : voir la zone ne donne pas les
    /// passages des véhicules des autres.
    ///
    /// La permission est lue EN BASE, comme le fait <c>PermissionMiddleware</c> : le
    /// jeton ne porte que les rôles (voir <c>JwtService</c>), jamais les cases par
    /// module. Utilisateur introuvable => aucune zone (fail-closed).
    ///
    /// FAIT DE PRODUCTION À CONNAÎTRE : HERTZ n'a JAMAIS rattaché ses zones à des
    /// véhicules (3 zones, 0 liaison). Ses zones sont donc des zones DE SOCIÉTÉ : un
    /// compte restreint de HERTZ qui recevrait un jour la case Géofences les verrait
    /// toutes les trois — c'est le sens de la case (« gère les zones »). Kap Pharma
    /// (users.id = 58) a can_geofences = FALSE : il ne voit toujours AUCUNE zone, et le
    /// relevé du 23/09 ne cite aucun compte restreint de HERTZ parmi ceux qui ont la
    /// case. Ne donner la case Géofences à un LOCATAIRE qu'en connaissance de cause.
    /// </summary>
    public static async Task<List<int>?> VisibleGeofenceIdsAsync(
        IGisDbContext context, int companyId, int userId, List<int>? scopeIds, CancellationToken ct)
    {
        // Administrateur : aucun filtre, exactement comme avant.
        if (scopeIds is null) return null;

        if (!await CanManageGeofencesAsync(context, companyId, userId, ct)) return new List<int>();

        // Portée véhicules VIDE : il ne voit rien — pas même les zones de société.
        if (scopeIds.Count == 0) return new List<int>();

        return await VisibleGeofenceIdsQuery(context, companyId, scopeIds).ToListAsync(ct);
    }

    /// <summary>
    /// La case Géofences de l'appelant (<c>users.can_geofences</c>), lue EN BASE et
    /// bornée à sa société. Utilisateur introuvable => false (fail-closed). Partagée
    /// par la règle de lecture ci-dessus et par la règle d'écriture de
    /// <c>GeofencesController</c> : une seule lecture de la case, pas deux copies.
    /// </summary>
    public static Task<bool> CanManageGeofencesAsync(
        IGisDbContext context, int companyId, int userId, CancellationToken ct) =>
        context.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.CompanyId == companyId)
            .Select(u => u.CanGeofences)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Requête SQL de la règle restreinte de <see cref="VisibleGeofenceIdsAsync"/> :
    /// zones de la société rattachées à l'un des véhicules de <paramref name="scopeIds"/>,
    /// OU rattachées à aucun véhicule. Deux sous-requêtes EXISTS / NOT EXISTS sur
    /// <c>geofence_vehicles</c>, la liste des véhicules passant en UN paramètre tableau.
    /// </summary>
    internal static IQueryable<int> VisibleGeofenceIdsQuery(IGisDbContext context, int companyId, List<int> scopeIds)
    {
        List<int> ids = scopeIds;
        return context.Geofences.AsNoTracking()
            .Where(g => g.CompanyId == companyId
                && (g.AssignedVehicles.Any(gv => ids.Contains(gv.VehicleId))
                    || !g.AssignedVehicles.Any()))
            .Select(g => g.Id);
    }

    /// <summary>
    /// Géozones actives de la société visibles par l'appelant, règle de
    /// <see cref="VisibleGeofenceIdsAsync"/>. Ordonnées par identifiant pour que la
    /// couleur attribuée à chaque zone par l'écran soit stable d'un appel à l'autre.
    /// </summary>
    public static async Task<List<DashboardGeofence>> VisibleGeofencesAsync(
        IGisDbContext context, int companyId, int userId, List<int>? scopeIds, CancellationToken ct)
    {
        var visibleIds = await VisibleGeofenceIdsAsync(context, companyId, userId, scopeIds, ct);
        if (visibleIds is { Count: 0 }) return new List<DashboardGeofence>();

        var query = context.Geofences.AsNoTracking()
            .Where(g => g.CompanyId == companyId && g.IsActive);

        if (visibleIds is not null)
        {
            List<int> gids = visibleIds;
            query = query.Where(g => gids.Contains(g.Id));
        }

        return await query
            .OrderBy(g => g.Id)
            .Select(g => new DashboardGeofence(g.Id, g.Name))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Véhicules retenus par <c>GET /api/dashboard/stats</c> : ceux de la société,
    /// bornés à la portée de l'appelant (null = tout le parc, liste VIDE = aucun).
    /// Cette route comptait la flotte ENTIÈRE de la société — mêmes compteurs que
    /// /dashboard/all, mais par une autre URL, donc le même symptôme HERTZ.
    /// </summary>
    public static async Task<List<Vehicle>> StatsVehiclesAsync(
        IGisDbContext context, int companyId, List<int>? scopeIds, CancellationToken ct)
    {
        var query = context.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == companyId);

        if (scopeIds is not null)
        {
            List<int> ids = scopeIds;
            query = query.Where(v => ids.Contains(v.Id));
        }

        return await query.ToListAsync(ct);
    }

    /// <summary>Compteurs de <c>GET /api/dashboard/stats</c> rattachés à un véhicule.</summary>
    public record DashboardStatsCounts(
        int UnresolvedAlerts,
        int AlertsToday,
        int UpcomingMaintenance,
        int OverdueMaintenance,
        decimal CostsThisMonth,
        decimal FuelCostsThisMonth,
        int TripsToday,
        decimal DistanceToday,
        int ActiveGeofences,
        int GeofenceEventsToday);

    /// <summary>
    /// Les compteurs de <c>/api/dashboard/stats</c> qui portent sur un VÉHICULE
    /// (alertes, entretiens, coûts, trajets, géozones), dans la portée de l'appelant.
    /// Ils ne filtraient que par société : un locataire de HERTZ affecté à 2 véhicules
    /// y lisait les alertes, les coûts et les kilomètres des 305 autres.
    /// Les géozones suivent en plus la règle du 23/09/2026
    /// (<see cref="VisibleGeofenceIdsAsync"/>) : un passage n'est compté que s'il
    /// concerne un véhicule de la portée ET une zone que l'appelant a le droit de voir.
    /// Les compteurs Conducteurs restent volontairement à l'échelle de la société —
    /// un conducteur n'est pas rattaché à un véhicule de façon exclusive.
    /// </summary>
    public static async Task<DashboardStatsCounts> StatsCountsAsync(
        IGisDbContext context, int companyId, int userId, List<int>? scopeIds,
        DateTime today, DateTime thisMonth, CancellationToken ct)
    {
        var unresolvedAlerts = context.GpsAlerts.AsNoTracking()
            .Where(a => a.VehicleId.HasValue && a.Vehicle!.CompanyId == companyId && !a.Resolved);
        var alertsToday = context.GpsAlerts.AsNoTracking()
            .Where(a => a.VehicleId.HasValue && a.Vehicle!.CompanyId == companyId && a.Timestamp >= today);

        // Même périmètre qu'ailleurs : ni en pause, ni modèle désactivé.
        var upcoming = context.VehicleMaintenanceSchedules.AsNoTracking()
            .Where(s => s.CompanyId == companyId && !s.IsPaused && s.Template!.IsActive &&
                        (s.Status == "upcoming" || s.Status == "due"));
        var overdue = context.VehicleMaintenanceSchedules.AsNoTracking()
            .Where(s => s.CompanyId == companyId && !s.IsPaused && s.Template!.IsActive &&
                        (s.Status == "overdue" || s.Status == "critical"));

        var costs = context.VehicleCosts.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Date >= thisMonth);
        var fuelCosts = context.VehicleCosts.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Type == "fuel" && c.Date >= thisMonth);

        var tripsToday = context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.StartTime >= today);
        var distanceToday = context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.StartTime >= today && t.Status == "completed");

        var geofences = context.Geofences.AsNoTracking()
            .Where(g => g.CompanyId == companyId && g.IsActive);
        var geofenceEventsToday = context.GeofenceEvents.AsNoTracking()
            .Where(e => e.Geofence!.CompanyId == companyId && e.Timestamp >= today);

        if (scopeIds is not null)
        {
            List<int> ids = scopeIds;
            unresolvedAlerts = unresolvedAlerts.Where(a => ids.Contains(a.VehicleId!.Value));
            alertsToday = alertsToday.Where(a => ids.Contains(a.VehicleId!.Value));
            upcoming = upcoming.Where(s => ids.Contains(s.VehicleId));
            overdue = overdue.Where(s => ids.Contains(s.VehicleId));
            costs = costs.Where(c => ids.Contains(c.VehicleId));
            fuelCosts = fuelCosts.Where(c => ids.Contains(c.VehicleId));
            tripsToday = tripsToday.Where(t => ids.Contains(t.VehicleId));
            distanceToday = distanceToday.Where(t => ids.Contains(t.VehicleId));
            geofenceEventsToday = geofenceEventsToday.Where(e => ids.Contains(e.VehicleId));
        }

        var visibleGeofenceIds = await VisibleGeofenceIdsAsync(context, companyId, userId, scopeIds, ct);
        if (visibleGeofenceIds is not null)
        {
            List<int> gids = visibleGeofenceIds;
            geofences = geofences.Where(g => gids.Contains(g.Id));
            geofenceEventsToday = geofenceEventsToday.Where(e => gids.Contains(e.GeofenceId));
        }

        return new DashboardStatsCounts(
            UnresolvedAlerts: await unresolvedAlerts.CountAsync(ct),
            AlertsToday: await alertsToday.CountAsync(ct),
            UpcomingMaintenance: await upcoming.CountAsync(ct),
            OverdueMaintenance: await overdue.CountAsync(ct),
            // Net des crédits (avoir, remboursement d'assurance), comme partout ailleurs.
            CostsThisMonth: await VehicleCostCategory.SignedTotalAsync(costs),
            FuelCostsThisMonth: await fuelCosts.Select(c => (decimal?)c.Amount).SumAsync(ct) ?? 0m,
            TripsToday: await tripsToday.CountAsync(ct),
            DistanceToday: await distanceToday.Select(t => (decimal?)t.DistanceKm).SumAsync(ct) ?? 0m,
            ActiveGeofences: await geofences.CountAsync(ct),
            GeofenceEventsToday: await geofenceEventsToday.CountAsync(ct));
    }

    /// <summary>Une ligne du flux <c>GET /api/dashboard/activity</c>.</summary>
    public record DashboardActivity(string Type, int Id, string? Message, DateTime Timestamp, string? VehicleName);

    /// <summary>
    /// Plafond du flux « activité récente ». Au-delà, ce n'est plus un flux : l'écran
    /// n'affiche qu'une poignée de lignes, et rien ne justifie de matérialiser plus.
    /// </summary>
    public const int LimiteActiviteMax = 200;

    /// <summary>
    /// Borne la limite reçue de la query string. Elle arrivait BRUTE jusqu'aux trois
    /// <c>Take()</c> de <see cref="RecentActivityAsync"/> :
    ///   • <c>?limit=-1</c> produisait un <c>LIMIT -1</c>, refusé par PostgreSQL
    ///     (« LIMIT must not be negative ») — une 500 à la portée de n'importe qui ;
    ///   • <c>?limit=1000000</c> matérialisait deux fois un million de lignes de
    ///     <c>gps_alerts</c> (446 k lignes par jour sur TN) pour n'en rendre que le
    ///     dessus.
    /// </summary>
    public static int BornerLimiteActivite(int limit) => Math.Clamp(limit, 1, LimiteActiviteMax);

    /// <summary>
    /// Le flux « activité récente » : dernières alertes GPS et derniers passages de
    /// géofence, fusionnés par horodatage.
    ///
    /// Cette route refaisait, à une URL près, la requête du bloc « Alertes » du
    /// tableau de bord — et sans aucune portée : elle publie le NOM DU VÉHICULE, donc
    /// un locataire de HERTZ y retrouvait tout le parc alors que /dashboard/all venait
    /// d'être cloisonné. Elle vit sous un commentaire « LEGACY ENDPOINTS » mais le
    /// front l'appelle toujours (api.service.ts).
    ///
    /// Trois états, comme partout : <paramref name="scopeIds"/> null = administrateur,
    /// aucun filtre ; liste non vide = ses véhicules ; liste VIDE = rien.
    /// Les passages de géofence suivent EN PLUS la règle du 23/09/2026
    /// (<see cref="VisibleGeofenceIdsAsync"/>) : la ligne publie le NOM DE LA ZONE,
    /// que l'appelant n'a pas forcément le droit de connaître.
    /// </summary>
    public static async Task<List<DashboardActivity>> RecentActivityAsync(
        IGisDbContext context, int companyId, int userId, List<int>? scopeIds, int limit, CancellationToken ct)
    {
        // Borne appliquée ICI aussi, et pas seulement chez l'appelant : les trois
        // Take() ci-dessous reçoivent une valeur qui vient de la query string.
        limit = BornerLimiteActivite(limit);

        var alertsQuery = context.GpsAlerts.AsNoTracking()
            .Where(a => a.VehicleId.HasValue && a.Vehicle!.CompanyId == companyId);
        var eventsQuery = context.GeofenceEvents.AsNoTracking()
            .Where(e => e.Vehicle!.CompanyId == companyId);

        if (scopeIds is not null)
        {
            List<int> ids = scopeIds;
            alertsQuery = alertsQuery.Where(a => ids.Contains(a.VehicleId!.Value));
            eventsQuery = eventsQuery.Where(e => ids.Contains(e.VehicleId));
        }

        var visibleGeofenceIds = await VisibleGeofenceIdsAsync(context, companyId, userId, scopeIds, ct);
        if (visibleGeofenceIds is not null)
        {
            List<int> gids = visibleGeofenceIds;
            eventsQuery = eventsQuery.Where(e => gids.Contains(e.GeofenceId));
        }

        var recentAlerts = await alertsQuery
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => new DashboardActivity("alert", a.Id, a.Message, a.Timestamp,
                a.Vehicle != null ? a.Vehicle.Name : null))
            .ToListAsync(ct);

        var recentEvents = await eventsQuery
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .Select(e => new DashboardActivity("geofence", e.Id, e.Type + " - " + e.Geofence!.Name, e.Timestamp,
                e.Vehicle != null ? e.Vehicle.Name : null))
            .ToListAsync(ct);

        return recentAlerts
            .Concat(recentEvents)
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Les quatre postes de dépenses d'une fenêtre [from, to], dans la portée
    /// véhicules de l'appelant (<paramref name="scopeIds"/> : null = tout le parc,
    /// liste vide = rien) :
    ///   • carburant  = pleins saisis (fuel_entries) + dépenses de type « fuel » ;
    ///   • entretien  = dépenses de type « maintenance » UNIQUEMENT — un entretien
    ///     saisi crée une VehicleCost ET un MaintenanceLog qui la référence, donc
    ///     compter les journaux doublerait (recette client du 25/08/2026) ;
    ///   • réparations = repairs, statut « cancelled » EXCLU — une réparation
    ///     annulée n'est pas une dépense, et les rapports de coûts l'excluent
    ///     déjà : la compter ici faisait diverger le total des deux écrans —,
    ///     plus les dépenses de type « repair », MOINS les avoirs et remboursements
    ///     (règle du 18/09/2026 : ce bloc à quatre postes n'a pas la place d'une
    ///     ligne de crédit, que les rapports détaillés portent à part). Le poste
    ///     peut donc être négatif ; le borner à zéro ferait mentir le total ;
    ///   • autres      = le reste des dépenses, BRUT
    ///     (ventilation <see cref="VehicleCostCategory"/>).
    /// Une seule définition, partagée par la période courante et la période
    /// précédente : sans cela le total et la flèche de tendance ne comparaient pas
    /// les mêmes choses. Les mensualités d'acquisition sont comptées à part
    /// (<see cref="AcquisitionCostAsync"/>).
    /// Statique et prenant le contexte en paramètre pour être appelable telle
    /// quelle par les tests (aucune copie de la règle ailleurs).
    /// </summary>
    public static async Task<(decimal Fuel, decimal Maintenance, decimal Repair, decimal Other)> PeriodCostsAsync(
        IGisDbContext context, int companyId, List<int>? scopeIds, DateTime from, DateTime to, CancellationToken ct)
    {
        // Rattachement par véhicule, comme OperatingCostAggregator et les rapports
        // de coûts : un plein SANS véhicule n'appartient à personne et ne compte
        // nulle part. Il était jusqu'ici additionné pour un admin (et lui seul),
        // si bien que le même plein entrait dans le tableau de bord mais jamais
        // dans le rapport mensuel.
        var fuelEntries = context.FuelEntries.AsNoTracking()
            .Where(f => f.CompanyId == companyId && f.VehicleId != null
                        && f.InvoiceDate >= from && f.InvoiceDate <= to);
        var costs = context.VehicleCosts.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Date >= from && c.Date <= to);
        var repairs = context.Repairs.AsNoTracking()
            .Where(r => r.SocieteId == companyId && r.RepairDate >= from && r.RepairDate <= to);

        if (scopeIds is not null)
        {
            List<int> ids = scopeIds;
            fuelEntries = fuelEntries.Where(f => ids.Contains(f.VehicleId!.Value));
            costs = costs.Where(c => ids.Contains(c.VehicleId));
            repairs = repairs.Where(r => ids.Contains(r.VehicleId));
        }

        var fuel = await fuelEntries.Select(f => (decimal?)f.TotalAmount).SumAsync(ct) ?? 0m;

        // Dépenses : une somme par type EN SQL (quelques lignes, jamais le détail),
        // puis la ventilation partagée en C# (VehicleCostCategory) — la même que
        // les rapports de coûts et le tableau de bord GPA. Constat du 14/09/2026 :
        // « repair » tombait ici en « Autres » et « insurance_refund » s'y
        // ajoutait en positif, alors que les rapports les rangent en Réparations
        // et en crédit ; la comparaison exacte « maintenance » laissait aussi
        // « Entretien » (casse) et « entretien » en « Autres ».
        // Parts positive et négative séparées, comme VehicleCostCategory.SignedTotalAsync :
        // un crédit se déduit ligne à ligne en valeur absolue, ce que la somme brute d'un
        // type mélangé (un avoir ancien saisi à −120 et un autre à +30) ne permet pas.
        // CASE plutôt que Math.Abs, que le fournisseur SQLite des tests ne traduit pas.
        var byType = await costs
            .GroupBy(c => c.Type)
            .Select(g => new
            {
                Type = g.Key,
                Positive = g.Sum(c => c.Amount > 0 ? c.Amount : 0m),
                Negative = g.Sum(c => c.Amount < 0 ? c.Amount : 0m)
            })
            .ToListAsync(ct);

        decimal maintenance = 0m, repair = 0m, other = 0m;
        foreach (var t in byType)
        {
            // Crédit (avoir, remboursement) : déduit des RÉPARATIONS, pas d'« Autres »
            // (règle du 18/09/2026) — ce bloc n'a que quatre postes.
            if (VehicleCostCategory.IsCredit(t.Type))
            {
                repair -= t.Positive - t.Negative;
                continue;
            }
            var amount = t.Positive + t.Negative;
            switch (VehicleCostCategory.Classify(t.Type).Category)
            {
                case CostCategory.Fuel: fuel += amount; break;
                case CostCategory.Maintenance: maintenance += amount; break;
                case CostCategory.Repair: repair += amount; break;
                default: other += amount; break;
            }
        }

        // Réparations annulées exclues, casse ET espaces ignorés (« Cancelled », « CANCELLED »
        // des données anciennes) — même règle que RepairInputRules.HasStatus et
        // OperatingCostAggregator. Somme calculée en base : le détail n'est jamais chargé.
        repair += await repairs
            .Where(r => r.Status == null || r.Status.Trim().ToLower() != RepairInputRules.Cancelled)
            .Select(r => (decimal?)r.TotalCost)
            .SumAsync(ct) ?? 0m;

        return (fuel, maintenance, repair, other);
    }

    /// <summary>
    /// Coût d'acquisition de la période : Σ (paid_amount ?? amount) des lignes
    /// d'acquisition_payments de la société (et des véhicules visibles) dont la
    /// date d'échéance tombe dans [from, to] et qui COMPTENT (payées, ou
    /// planifiées dont la date est atteinte — jamais les ignorées), en SQL ;
    /// PLUS, pour les véhicules à contrat/prix qui n'ont ENCORE aucune ligne
    /// (transition sans backfill : l'échéancier n'est généré qu'à la première
    /// ouverture de l'écran Dépenses), le calcul à la volée d'AcquisitionSchedule
    /// — comportement identique à l'ancien par défaut, modifiable dès qu'une
    /// ligne existe. JAMAIS de synchronisation ici : le dashboard est mis en
    /// cache et pré-chauffé hors requête, sans contexte tenant.
    /// Le calcul lui-même vit dans la couche Application
    /// (<see cref="AcquisitionCostCalculator.PeriodCostAsync"/>), partagé avec le
    /// tableau de bord GPA ; seul le repli en cas de panne reste propre à ce service.
    /// </summary>
    private async Task<decimal> AcquisitionCostAsync(int companyId, List<int>? scopeIds, List<Vehicle> vehicles,
        DateTime from, DateTime to, DateTime now, CancellationToken ct)
    {
        try
        {
            return await AcquisitionCostCalculator.PeriodCostAsync(_context, companyId, scopeIds, vehicles, from, to, now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Table absente (migration 044 pas encore jouée) ou base indisponible :
            // le dashboard ne doit pas tomber pour ce poste, on revient au calcul
            // à la volée — et on le dit dans les logs.
            _logger.LogWarning(ex, "Dashboard : somme acquisition_payments impossible pour la société {CompanyId}, repli sur AcquisitionSchedule", companyId);
            return AcquisitionSchedule.Cost(vehicles, from, to, now);
        }
    }

    /// <summary>Fenêtres [début,fin] de la période et de la période précédente. Partagé contrôleur + pré-chauffage.</summary>
    public static (DateTime start, DateTime end, DateTime prevStart, DateTime prevEnd) GetPeriodRange(DateTime now, string period)
    {
        DateTime start, end, prevStart, prevEnd;
        switch (period)
        {
            case "today":
                start = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
                end = DateTime.SpecifyKind(now.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Utc);
                prevStart = DateTime.SpecifyKind(now.Date.AddDays(-1), DateTimeKind.Utc);
                prevEnd = DateTime.SpecifyKind(now.Date.AddSeconds(-1), DateTimeKind.Utc);
                break;
            case "yesterday":
                start = DateTime.SpecifyKind(now.Date.AddDays(-1), DateTimeKind.Utc);
                end = DateTime.SpecifyKind(now.Date.AddSeconds(-1), DateTimeKind.Utc);
                prevStart = DateTime.SpecifyKind(now.Date.AddDays(-2), DateTimeKind.Utc);
                prevEnd = DateTime.SpecifyKind(start.AddSeconds(-1), DateTimeKind.Utc);
                break;
            case "week":
                var dayOfWeek = (int)now.DayOfWeek;
                start = DateTime.SpecifyKind(now.Date.AddDays(-dayOfWeek), DateTimeKind.Utc);
                end = DateTime.SpecifyKind(start.AddDays(7).AddSeconds(-1), DateTimeKind.Utc);
                prevStart = DateTime.SpecifyKind(start.AddDays(-7), DateTimeKind.Utc);
                prevEnd = DateTime.SpecifyKind(start.AddSeconds(-1), DateTimeKind.Utc);
                break;
            case "quarter":
                var quarterMonth = ((now.Month - 1) / 3) * 3 + 1;
                start = DateTime.SpecifyKind(new DateTime(now.Year, quarterMonth, 1), DateTimeKind.Utc);
                end = DateTime.SpecifyKind(start.AddMonths(3).AddSeconds(-1), DateTimeKind.Utc);
                prevStart = DateTime.SpecifyKind(start.AddMonths(-3), DateTimeKind.Utc);
                prevEnd = DateTime.SpecifyKind(start.AddSeconds(-1), DateTimeKind.Utc);
                break;
            case "year":
                start = DateTime.SpecifyKind(new DateTime(now.Year, 1, 1), DateTimeKind.Utc);
                end = DateTime.SpecifyKind(start.AddYears(1).AddSeconds(-1), DateTimeKind.Utc);
                prevStart = DateTime.SpecifyKind(start.AddYears(-1), DateTimeKind.Utc);
                prevEnd = DateTime.SpecifyKind(start.AddSeconds(-1), DateTimeKind.Utc);
                break;
            default: // month
                start = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, 1), DateTimeKind.Utc);
                end = DateTime.SpecifyKind(start.AddMonths(1).AddSeconds(-1), DateTimeKind.Utc);
                prevStart = DateTime.SpecifyKind(start.AddMonths(-1), DateTimeKind.Utc);
                prevEnd = DateTime.SpecifyKind(start.AddSeconds(-1), DateTimeKind.Utc);
                break;
        }
        return (start, end, prevStart, prevEnd);
    }
}
