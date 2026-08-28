using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using GisAPI.Application.Common.Interfaces;

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

    public DashboardService(GisDbContext context, IVehicleHealthScoreService healthService, IFuelCalculationService fuelCalcService)
    {
        _context = context;
        _healthService = healthService;
        _fuelCalcService = fuelCalcService;
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

        // Non-admin users only see their assigned vehicles
        if (!isAdmin && userId > 0)
        {
            var assignedVehicleIds = await _context.UserVehicles
                .Where(uv => uv.UserId == userId)
                .Select(uv => uv.VehicleId)
                .ToListAsync();

            if (assignedVehicleIds.Any())
                vehicleQuery = vehicleQuery.Where(v => assignedVehicleIds.Contains(v.Id));
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
        try
        {
            fuelCost = await _context.FuelEntries.AsNoTracking()
                .Where(f => f.CompanyId == companyId && f.InvoiceDate >= periodStart && f.InvoiceDate <= periodEnd)
                .Select(f => (decimal?)f.TotalAmount).SumAsync() ?? 0m;
            fuelCost += await _context.VehicleCosts.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.Type == "fuel" && c.Date >= periodStart && c.Date <= periodEnd)
                .Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;

            // Un entretien enregistré depuis l'écran écrit DEUX lignes pour une
            // seule dépense : une VehicleCost (type « maintenance ») et un
            // MaintenanceLog qui la référence par CostId. Additionner les deux
            // tables doublait donc chaque entretien dans le coût affiché.
            // On prend la dépense (VehicleCost) comme source, et on n'ajoute que
            // les journaux SANS coût rattaché — les entretiens plus anciens ou
            // importés, qui eux n'ont pas de ligne de dépense correspondante.
            maintenanceCost = await _context.VehicleCosts.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.Type == "maintenance" && c.Date >= periodStart && c.Date <= periodEnd)
                .Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;
            maintenanceCost += await _context.MaintenanceLogs.AsNoTracking()
                .Where(m => m.Vehicle!.CompanyId == companyId && m.DoneDate >= periodStart && m.DoneDate <= periodEnd
                            && m.ActualCost > 0 && m.CostId == null)
                .Select(m => (decimal?)m.ActualCost).SumAsync() ?? 0m;

            repairCost = await _context.Repairs.AsNoTracking()
                .Where(r => r.SocieteId == companyId && r.RepairDate >= periodStart && r.RepairDate <= periodEnd)
                .Select(r => (decimal?)r.TotalCost).SumAsync() ?? 0m;

            otherCost = await _context.VehicleCosts.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.Date >= periodStart && c.Date <= periodEnd
                    && c.Type != "fuel" && c.Type != "maintenance")
                .Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Dashboard] Error calculating expenses for company {companyId}: {ex.Message}");
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
        var tripKmByVehicle = await _context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.Status == "completed" &&
                        t.StartTime >= periodStart && t.StartTime <= periodEnd)
            .GroupBy(t => t.VehicleId)
            .Select(g => new { VehicleId = g.Key, Km = g.Sum(t => t.DistanceKm) })
            .ToListAsync();

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
        var healthResults = await _healthService.CalculateAllScoresAsync(companyId);
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
                mileage = Math.Round((double)x.Km)
            })
            .ToList();

        // ── 7. Geofences — number of PASSAGES (entry/exit events) in the period,
        //       not the static count of assigned vehicles (which never reflected activity). ──
        var geoColors = new[] { "#22c55e", "#3b82f6", "#f97316", "#06b6d4", "#8b5cf6", "#ec4899", "#eab308", "#14b8a6" };
        var geofencesRaw = await _context.Geofences.AsNoTracking()
            .Where(g => g.CompanyId == companyId && g.IsActive)
            .Select(g => new { g.Id, g.Name })
            .ToListAsync();
        var geoEventCounts = await _context.GeofenceEvents.AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Timestamp >= periodStart && e.Timestamp <= periodEnd)
            .GroupBy(e => e.GeofenceId)
            .Select(g => new { GeofenceId = g.Key, Count = g.Count() })
            .ToListAsync();
        var geoCountMap = geoEventCounts.ToDictionary(x => x.GeofenceId, x => x.Count);
        var geofencesList = geofencesRaw
            .Select((g, i) => new { name = g.Name, color = geoColors[i % geoColors.Length], count = geoCountMap.GetValueOrDefault(g.Id, 0) })
            .ToList();

        // ── 8. Alerts (GpsAlerts + Notifications merged) ──
        var gpsAlerts = await _context.GpsAlerts.AsNoTracking()
            .Where(a => a.VehicleId.HasValue && a.Vehicle!.CompanyId == companyId)
            .OrderByDescending(a => a.Timestamp)
            .Take(20)
            .Select(a => new { message = a.Message ?? "Alerte", severity = a.Severity ?? "info", ts = a.Timestamp })
            .ToListAsync();

        var notifications = await _context.Notifications.AsNoTracking()
            .Where(n => n.CompanyId == companyId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(20)
            .Select(n => new { message = n.Title ?? "Notification", severity = n.Priority, ts = n.CreatedAt })
            .ToListAsync();

        var mergedAlerts = gpsAlerts
            .Select(a => new
            {
                a.message,
                severity = a.severity == "critical" ? "danger" : a.severity == "warning" ? "warning" : "info",
                time = a.ts.ToString("dd/MM HH:mm"),
                a.ts
            })
            .Concat(notifications.Select(n => new
            {
                message = n.message,
                severity = n.severity == "high" || n.severity == "critical" ? "danger" : n.severity == "medium" ? "warning" : "info",
                time = n.ts.ToString("dd/MM HH:mm"),
                ts = n.ts
            }))
            .OrderByDescending(a => a.ts)
            .Take(20)
            .Select(a => new { a.message, a.severity, a.time })
            .ToList();

        // ── 9. Recent trips ──
        var trips = await _context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId)
            .OrderByDescending(t => t.StartTime)
            .Take(20)
            .Include(t => t.Vehicle)
            .ToListAsync();

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
        var driversRaw = await (from d in _context.Drivers.AsNoTracking()
                                where d.CompanyId == companyId
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
                                .ToListAsync();

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
            prevTotalCost += await _context.FuelEntries.AsNoTracking()
                .Where(f => f.CompanyId == companyId && f.InvoiceDate >= prevStart && f.InvoiceDate <= prevEnd)
                .Select(f => (decimal?)f.TotalAmount).SumAsync() ?? 0m;
            prevTotalCost += await _context.MaintenanceLogs.AsNoTracking()
                .Where(m => m.Vehicle!.CompanyId == companyId && m.DoneDate >= prevStart && m.DoneDate <= prevEnd && m.ActualCost > 0)
                .Select(m => (decimal?)m.ActualCost).SumAsync() ?? 0m;
            prevTotalCost += await _context.Repairs.AsNoTracking()
                .Where(r => r.SocieteId == companyId && r.RepairDate >= prevStart && r.RepairDate <= prevEnd)
                .Select(r => (decimal?)r.TotalCost).SumAsync() ?? 0m;
            prevTotalCost += await _context.VehicleCosts.AsNoTracking()
                .Where(c => c.CompanyId == companyId && c.Date >= prevStart && c.Date <= prevEnd)
                .Select(c => (decimal?)c.Amount).SumAsync() ?? 0m;
        }
        catch { /* trend is best-effort */ }
        var currentTotalCost = fuelCost + maintenanceCost + repairCost + otherCost;

        var currentDistance = tripKmByVehicle.Sum(x => x.Km);
        var prevDistance = await _context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.Status == "completed" &&
                        t.StartTime >= prevStart && t.StartTime <= prevEnd)
            .Select(t => (decimal?)t.DistanceKm).SumAsync() ?? 0m;

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
                totalCost = fuelCost + maintenanceCost + repairCost + otherCost
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
            periodDistance = Math.Round((double)currentDistance),
            typeBreakdown
        };

        return result;
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
