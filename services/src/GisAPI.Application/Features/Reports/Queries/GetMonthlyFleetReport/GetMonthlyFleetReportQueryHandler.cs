using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GisAPI.Application.Features.Reports.Queries.GetMonthlyFleetReport;

/// <summary>
/// Rapport mensuel de flotte.
///
/// Jusqu'au 09/09/2026 la partie financière de ce rapport était ENTIÈREMENT
/// fabriquée : aucune table de dépense n'était ouverte. Le carburant valait
/// « litres estimés × 2,1 », l'assurance « nombre de véhicules × 150 », les
/// autres coûts « nombre de véhicules × 50 » et la maintenance quatre lignes
/// Vidange/Pneus/Freins/Révision calculées sur le nombre de véhicules. Une
/// société ayant saisi 10 430 d'assurance lisait donc « Assurance 1 800 »,
/// identique tous les mois, et l'écran affichait un total inconciliable avec
/// l'écran Dépenses.
///
/// Les coûts viennent désormais de <see cref="OperatingCostAggregator"/> — la
/// même définition que les rapports de coûts (carburant = pleins saisis +
/// dépenses de type <c>fuel</c>, entretiens = dépenses <c>maintenance</c>,
/// réparations = table <c>repairs</c> hors annulées, autres = le reste des
/// dépenses ; mensualités d'acquisition exclues).
///
/// La portée de l'appelant (<see cref="VehicleScope"/>) est appliquée UNE fois,
/// dans <c>GetVehiclesAsync</c> : tout le reste du rapport en dérive. L'agrégateur
/// l'applique de son côté ; sans le filtre ici, un employé affecté à un véhicule
/// sur douze lisait des coûts limités à son véhicule divisés par la distance des
/// douze — et voyait au passage les plaques et l'utilisation des onze autres.
/// </summary>
public class GetMonthlyFleetReportQueryHandler : IRequestHandler<GetMonthlyFleetReportQuery, MonthlyFleetReportDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private static readonly CultureInfo FrenchCulture = new("fr-FR");

    public GetMonthlyFleetReportQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<MonthlyFleetReportDto> Handle(GetMonthlyFleetReportQuery request, CancellationToken ct)
    {
        var startDate = DateTime.SpecifyKind(new DateTime(request.Year, request.Month, 1), DateTimeKind.Utc);
        var endDate = DateTime.SpecifyKind(startDate.AddMonths(1), DateTimeKind.Utc);
        var daysInMonth = DateTime.DaysInMonth(request.Year, request.Month);

        // Previous month for MoM comparison
        var prevMonthStart = DateTime.SpecifyKind(startDate.AddMonths(-1), DateTimeKind.Utc);
        var prevMonthEnd = startDate;

        // Previous year same month for YoY comparison
        var prevYearStart = DateTime.SpecifyKind(startDate.AddYears(-1), DateTimeKind.Utc);
        var prevYearEnd = DateTime.SpecifyKind(endDate.AddYears(-1), DateTimeKind.Utc);

        // Fetch all required data
        var vehicles = await GetVehiclesAsync(request, ct);
        var vehicleIds = vehicles.Select(v => v.Id).ToList();
        var deviceIds = vehicles.Where(v => v.GpsDeviceId.HasValue).Select(v => v.GpsDeviceId!.Value).ToList();

        var positions = await GetPositionsAsync(deviceIds, startDate, endDate, ct);
        var prevMonthPositions = await GetPositionsAsync(deviceIds, prevMonthStart, prevMonthEnd, ct);
        var prevYearPositions = await GetPositionsAsync(deviceIds, prevYearStart, prevYearEnd, ct);

        var driverIds = vehicles.Where(v => v.AssignedDriverId.HasValue)
            .Select(v => v.AssignedDriverId!.Value)
            .Distinct()
            .ToList();
        
        var drivers = driverIds.Any()
            ? await _context.Drivers.AsNoTracking()
                .Where(d => driverIds.Contains(d.Id))
                .ToListAsync(ct)
            : new List<Driver>();

        // Build the report
        var report = new MonthlyFleetReportDto
        {
            Year = request.Year,
            Month = request.Month,
            MonthName = startDate.ToString("MMMM yyyy", FrenchCulture),
            GeneratedAt = DateTime.UtcNow,
            ReportPeriod = $"{startDate:dd/MM/yyyy} - {endDate.AddDays(-1):dd/MM/yyyy}"
        };

        // Dépenses RÉELLEMENT saisies sur le mois (aucune constante) : c'est la
        // source unique des sections carburant, maintenance et coûts.
        var realCosts = await LoadRealCostsAsync(request, vehicleIds, startDate, endDate, ct);

        // Process all sections
        report.FleetOverview = BuildFleetOverview(vehicles, positions);
        report.Utilization = BuildUtilization(vehicles, positions, startDate, daysInMonth);
        report.FuelAnalytics = BuildFuelAnalytics(vehicles, positions, realCosts);
        report.Maintenance = BuildMaintenance(vehicles, realCosts);
        report.DriverPerformance = BuildDriverPerformance(drivers, vehicles, positions);
        report.Efficiency = BuildEfficiency(positions, daysInMonth);
        report.CostAnalysis = BuildCostAnalysis(vehicles, positions, realCosts);

        // Comparisons — le coût comparé est lui aussi le coût réel des deux périodes.
        var prevMonthCost = await LoadPeriodCostTotalAsync(vehicleIds, prevMonthStart, prevMonthEnd, ct);
        report.MonthOverMonth = BuildComparison("Mois précédent", positions, prevMonthPositions,
            realCosts.Total.Total, prevMonthCost);
        if (prevYearPositions.Any())
        {
            var prevYearCost = await LoadPeriodCostTotalAsync(vehicleIds, prevYearStart, prevYearEnd, ct);
            report.YearOverYear = BuildComparison("Même mois année précédente", positions, prevYearPositions,
                realCosts.Total.Total, prevYearCost);
        }

        // Executive Summary — les heures de conduite viennent des trajets terminés.
        var drivingMinutes = await LoadDrivingMinutesAsync(vehicleIds, startDate, endDate, ct);
        report.ExecutiveSummary = BuildExecutiveSummary(report, drivingMinutes);
        
        // Alerts
        report.Alerts = BuildAlerts(report, vehicles);
        
        // KPIs
        report.KeyPerformanceIndicators = BuildKpis(report);
        
        // Charts
        report.Charts = BuildChartData(report, vehicles, positions, startDate, daysInMonth);

        return report;
    }

    /// <summary>
    /// Périmètre du rapport, et lui seul : positions, conducteurs, coûts, ratios
    /// et graphiques en dérivent tous.
    ///
    /// Deux bornes, pas une. Le filtre société est indispensable — sans lui le
    /// rapport agrégeait les véhicules de TOUTES les sociétés pour un
    /// administrateur système, dont le filtre global de multi-tenance est
    /// contourné. Mais il ne suffit PAS à cloisonner : la portée utilisateur
    /// (<see cref="VehicleScope"/>) doit être appliquée ici aussi, sinon un
    /// employé affecté à un seul véhicule obtient un rapport dont les coûts sont
    /// bornés à son véhicule (l'agrégateur, lui, applique la portée) tandis que
    /// la distance, la consommation et la liste des véhicules couvrent tout le
    /// parc : ratios faux, et fuite des plaques et conducteurs des autres.
    /// Portée nulle = tout le parc de la société ; portée VIDE = rapport vide.
    /// </summary>
    private async Task<List<Vehicle>> GetVehiclesAsync(GetMonthlyFleetReportQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;

        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);
        if (scope is { Count: 0 }) return new List<Vehicle>();

        var query = _context.Vehicles.AsNoTracking()
            .Include(v => v.AssignedDriver)
            .Where(v => v.CompanyId == companyId);

        if (scope is not null)
            query = query.Where(v => scope.Contains(v.Id));

        if (request.VehicleIds?.Length > 0)
            query = query.Where(v => request.VehicleIds.Contains(v.Id));

        return await query.ToListAsync(ct);
    }

    private async Task<List<GpsPosition>> GetPositionsAsync(List<int> deviceIds, DateTime start, DateTime end, CancellationToken ct)
    {
        if (!deviceIds.Any()) return new List<GpsPosition>();

        // Ensure UTC kind for PostgreSQL compatibility
        var adjustedStart = DateTime.SpecifyKind(start.AddHours(1), DateTimeKind.Utc); // Tunisia UTC+1
        var adjustedEnd = DateTime.SpecifyKind(end.AddHours(1), DateTimeKind.Utc);

        // Sample positions: 1 per ~3 minute window instead of loading ALL positions
        // (a month of 10-second data for 20 devices = 5M+ rows → OOM)
        // This takes positions in the first 10 seconds of every 3rd minute = ~18x reduction
        return await _context.GpsPositions.AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId) &&
                        p.RecordedAt >= adjustedStart &&
                        p.RecordedAt < adjustedEnd &&
                        p.RecordedAt.Minute % 3 == 0 &&
                        p.RecordedAt.Second < 10)
            .ToListAsync(ct);
    }

    // ==================== DÉPENSES RÉELLES ====================

    /// <summary>Une ligne de <c>vehicle_costs</c> de la période, tous types confondus.</summary>
    private sealed record ExpenseRow(int VehicleId, string Type, decimal Amount, decimal Liters, DateTime Date, string? Description);

    /// <summary>
    /// Dépenses réellement saisies sur la période, prêtes pour les sections
    /// carburant / maintenance / coûts. Les quatre seaux viennent de
    /// <see cref="OperatingCostAggregator"/> ; le détail par type de dépense et
    /// les litres achetés sont relus à part car l'agrégateur ne les expose pas.
    /// </summary>
    private sealed class RealCostData
    {
        public required IReadOnlyDictionary<int, VehicleCostData> ByVehicle { get; init; }
        public required CostBucket Total { get; init; }
        public required IReadOnlyList<ExpenseRow> Expenses { get; init; }
        public required IReadOnlyList<RepairRow> Repairs { get; init; }
        /// <summary>Litres RÉELLEMENT achetés (pleins + dépenses carburant renseignées).</summary>
        public required IReadOnlyDictionary<int, decimal> LitersByVehicle { get; init; }

        public static RealCostData Empty { get; } = new()
        {
            ByVehicle = new Dictionary<int, VehicleCostData>(),
            Total = CostBucket.Zero,
            Expenses = Array.Empty<ExpenseRow>(),
            Repairs = Array.Empty<RepairRow>(),
            LitersByVehicle = new Dictionary<int, decimal>()
        };
    }

    private static string NormalizeType(string? type) => (type ?? string.Empty).Trim().ToLowerInvariant();

    // Mêmes règles de ventilation que l'agrégateur, au mot près : un type
    // « carburant » (non reconnu là-bas) resterait dans « autres » ici aussi,
    // sinon les catégories ne retomberaient plus sur le total.
    private static bool IsFuelType(string? type) => NormalizeType(type) == "fuel";
    private static bool IsMaintenanceType(string? type) => NormalizeType(type) is "maintenance" or "entretien";
    private static bool IsInsuranceType(string? type) => NormalizeType(type) is "insurance" or "assurance";

    private Task<OperatingCostData> LoadAggregateAsync(
        GetMonthlyFleetReportQuery request, DateTime startUtc, DateTime endExclusiveUtc, CancellationToken ct)
    {
        // Un seul véhicule demandé : autant borner la requête. Sinon on charge le
        // parc accessible (VehicleScope) et on recoupe en mémoire avec la liste du
        // rapport — c'est cette intersection qui fait foi. Elle reste nécessaire
        // même depuis que GetVehiclesAsync applique la portée : l'agrégateur ne
        // sait pas filtrer sur une LISTE de véhicules demandés (paramètre unitaire),
        // donc un filtre « véhicules 3 et 7 » ne se transmet que par ce recoupement.
        var single = request.VehicleIds is { Length: 1 } ? request.VehicleIds[0] : (int?)null;
        return OperatingCostAggregator.LoadAsync(_context, _tenantService, startUtc, endExclusiveUtc, single, null, ct);
    }

    private async Task<RealCostData> LoadRealCostsAsync(
        GetMonthlyFleetReportQuery request, List<int> vehicleIds, DateTime startUtc, DateTime endExclusiveUtc, CancellationToken ct)
    {
        if (vehicleIds.Count == 0) return RealCostData.Empty;

        var aggregate = await LoadAggregateAsync(request, startUtc, endExclusiveUtc, ct);
        var wanted = vehicleIds.ToHashSet();
        var scoped = aggregate.Vehicles.Where(v => wanted.Contains(v.VehicleId)).ToList();
        if (scoped.Count == 0) return RealCostData.Empty;

        var scopedIds = scoped.Select(v => v.VehicleId).ToList();
        var companyId = _tenantService.CompanyId ?? 0;

        // Détail par type : l'agrégateur ne rend que quatre seaux alors que
        // l'écran affiche une ligne « Assurance » et une répartition par catégorie.
        var expenses = (await _context.VehicleCosts.AsNoTracking()
            .Where(c => c.CompanyId == companyId
                     && scopedIds.Contains(c.VehicleId)
                     && c.Date >= startUtc
                     && c.Date < endExclusiveUtc)
            .Select(c => new { c.VehicleId, c.Type, c.Amount, c.Liters, c.Date, c.Description })
            .ToListAsync(ct))
            .Select(c => new ExpenseRow(c.VehicleId, c.Type, c.Amount, c.Liters ?? 0m, c.Date, c.Description))
            .ToList();

        // Litres achetés : les pleins saisis priment sur l'estimation capteur.
        var fills = await _context.FuelEntries.AsNoTracking()
            .Where(f => f.CompanyId == companyId
                     && f.VehicleId.HasValue
                     && scopedIds.Contains(f.VehicleId.Value)
                     && f.InvoiceDate >= startUtc
                     && f.InvoiceDate < endExclusiveUtc)
            .Select(f => new { VehicleId = f.VehicleId!.Value, f.Volume })
            .ToListAsync(ct);

        var liters = new Dictionary<int, decimal>();
        foreach (var fill in fills)
            liters[fill.VehicleId] = liters.GetValueOrDefault(fill.VehicleId) + fill.Volume;
        foreach (var expense in expenses.Where(e => IsFuelType(e.Type) && e.Liters > 0))
            liters[expense.VehicleId] = liters.GetValueOrDefault(expense.VehicleId) + expense.Liters;

        return new RealCostData
        {
            ByVehicle = scoped.ToDictionary(v => v.VehicleId),
            Total = scoped.Aggregate(CostBucket.Zero, (acc, v) => acc.Plus(v.Total)),
            Expenses = expenses,
            Repairs = aggregate.Repairs.Where(r => wanted.Contains(r.VehicleId)).ToList(),
            LitersByVehicle = liters
        };
    }

    /// <summary>
    /// Total des dépenses d'une période de COMPARAISON (mois précédent, année
    /// précédente). Seul ce scalaire est consommé : passer par l'agrégateur
    /// matérialisait pour rien les véhicules, les pleins, les dépenses, les
    /// réparations ET tous les trajets de la période — deux fois par rapport.
    /// Trois sommes suffisent, bornées par la MÊME liste de véhicules que le
    /// mois courant (elle porte déjà société + portée + filtre de l'écran), avec
    /// la définition de l'agrégateur au mot près : pleins + dépenses (tous types,
    /// les mensualités d'acquisition vivent dans une autre table) + réparations
    /// non annulées.
    /// </summary>
    private async Task<decimal> LoadPeriodCostTotalAsync(
        List<int> vehicleIds, DateTime startUtc, DateTime endExclusiveUtc, CancellationToken ct)
    {
        if (vehicleIds.Count == 0) return 0m;

        var companyId = _tenantService.CompanyId ?? 0;

        var fuel = await _context.FuelEntries.AsNoTracking()
            .Where(f => f.CompanyId == companyId
                     && f.VehicleId.HasValue
                     && vehicleIds.Contains(f.VehicleId.Value)
                     && f.InvoiceDate >= startUtc
                     && f.InvoiceDate < endExclusiveUtc)
            .Select(f => (decimal?)f.TotalAmount)
            .SumAsync(ct) ?? 0m;

        var expenses = await _context.VehicleCosts.AsNoTracking()
            .Where(c => c.CompanyId == companyId
                     && vehicleIds.Contains(c.VehicleId)
                     && c.Date >= startUtc
                     && c.Date < endExclusiveUtc)
            .Select(c => (decimal?)c.Amount)
            .SumAsync(ct) ?? 0m;

        // Le statut est comparé sans tenir compte de la casse, comme l'agrégateur :
        // la comparaison ne se traduit pas en SQL, d'où la projection minimale.
        var repairs = (await _context.Repairs.AsNoTracking()
            .Where(r => r.SocieteId == companyId
                     && vehicleIds.Contains(r.VehicleId)
                     && r.RepairDate >= startUtc
                     && r.RepairDate < endExclusiveUtc)
            .Select(r => new { r.TotalCost, r.Status })
            .ToListAsync(ct))
            .Where(r => !string.Equals(r.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.TotalCost);

        return fuel + expenses + repairs;
    }

    /// <summary>
    /// Minutes de conduite RÉELLES de la période : durée des trajets terminés
    /// (<c>trips.duration_minutes</c>) des véhicules du rapport. La carte
    /// « heures de conduite » affichait jusqu'ici « nombre de jours actifs × 8 » :
    /// un véhicule vu cinq minutes un seul jour pesait huit heures, et la valeur
    /// était rendue en grand sans la moindre mention d'estimation. Sans trajet
    /// enregistré, la carte affiche 0 — pas une conjecture.
    /// </summary>
    private async Task<int> LoadDrivingMinutesAsync(
        List<int> vehicleIds, DateTime startUtc, DateTime endExclusiveUtc, CancellationToken ct)
    {
        if (vehicleIds.Count == 0) return 0;

        var companyId = _tenantService.CompanyId ?? 0;

        return await _context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId
                     && vehicleIds.Contains(t.VehicleId)
                     && t.Status == "completed"
                     && t.StartTime >= startUtc
                     && t.StartTime < endExclusiveUtc)
            .Select(t => (int?)t.DurationMinutes)
            .SumAsync(ct) ?? 0;
    }

    private FleetOverviewDto BuildFleetOverview(List<Vehicle> vehicles, List<GpsPosition> positions)
    {
        var overview = new FleetOverviewDto
        {
            TotalVehicles = vehicles.Count,
            ActiveVehicles = vehicles.Count(v => v.Status == "Active"),
            InactiveVehicles = vehicles.Count(v => v.Status == "Inactive"),
            InMaintenanceVehicles = vehicles.Count(v => v.Status == "Maintenance")
        };

        // By Type
        overview.ByType = vehicles.GroupBy(v => v.Type ?? "Autre")
            .Select(g => new VehicleTypeSummaryDto
            {
                Type = g.Key,
                Count = g.Count(),
                Percentage = Math.Round((double)g.Count() / vehicles.Count * 100, 1),
                TotalDistanceKm = CalculateDistanceForVehicles(g.ToList(), positions),
                AvgDistanceKm = g.Count() > 0 ? CalculateDistanceForVehicles(g.ToList(), positions) / g.Count() : 0
            }).ToList();

        // By Status
        overview.ByStatus = vehicles.GroupBy(v => v.Status ?? "Unknown")
            .Select(g => new VehicleStatusSummaryDto
            {
                Status = g.Key,
                Count = g.Count(),
                Percentage = Math.Round((double)g.Count() / vehicles.Count * 100, 1)
            }).ToList();

        return overview;
    }

    private VehicleUtilizationDto BuildUtilization(List<Vehicle> vehicles, List<GpsPosition> positions, DateTime startDate, int daysInMonth)
    {
        var utilization = new VehicleUtilizationDto();
        var vehicleUtilizations = new List<double>();

        // Daily trend
        for (int day = 0; day < daysInMonth; day++)
        {
            var date = startDate.AddDays(day);
            var dayStart = date.AddHours(1);
            var dayEnd = dayStart.AddDays(1);
            
            var dayPositions = positions.Where(p => p.RecordedAt >= dayStart && p.RecordedAt < dayEnd).ToList();
            var activeVehicleIds = dayPositions.Select(p => p.DeviceId).Distinct().Count();
            
            utilization.DailyTrend.Add(new DailyUtilizationDto
            {
                Date = date,
                UtilizationRate = vehicles.Count > 0 ? Math.Round((double)activeVehicleIds / vehicles.Count * 100, 1) : 0,
                ActiveVehicles = activeVehicleIds,
                TotalDistanceKm = Math.Round(CalculateDistance(dayPositions), 2),
                TotalTrips = CountTrips(dayPositions)
            });
        }

        // By vehicle
        foreach (var vehicle in vehicles.Where(v => v.GpsDeviceId.HasValue))
        {
            var vehiclePositions = positions.Where(p => p.DeviceId == vehicle.GpsDeviceId).ToList();
            var operatingDays = vehiclePositions.Select(p => p.RecordedAt.Date).Distinct().Count();
            var distance = CalculateDistance(vehiclePositions);
            var utilizationRate = Math.Round((double)operatingDays / daysInMonth * 100, 1);
            
            vehicleUtilizations.Add(utilizationRate);

            utilization.ByVehicle.Add(new VehicleUtilizationDetailDto
            {
                VehicleId = vehicle.Id,
                VehicleName = vehicle.Name,
                Plate = vehicle.Plate,
                UtilizationRate = utilizationRate,
                TotalDistanceKm = Math.Round(distance, 2),
                TotalTrips = CountTrips(vehiclePositions),
                OperatingDays = operatingDays,
                AvgDailyKm = operatingDays > 0 ? Math.Round(distance / operatingDays, 2) : 0
            });
        }

        utilization.OverallUtilizationRate = utilization.DailyTrend.Any() 
            ? Math.Round(utilization.DailyTrend.Average(d => d.UtilizationRate), 1) : 0;
        utilization.AverageDailyDistanceKm = utilization.DailyTrend.Any()
            ? Math.Round(utilization.DailyTrend.Average(d => d.TotalDistanceKm), 2) : 0;
        utilization.TotalOperatingDays = utilization.DailyTrend.Count(d => d.ActiveVehicles > 0);
        utilization.TotalIdleDays = daysInMonth - utilization.TotalOperatingDays;

        // Statistics
        if (vehicleUtilizations.Any())
        {
            utilization.Statistics = CalculateStatistics(vehicleUtilizations);
        }

        return utilization;
    }

    // Default consumption rates (L/100km) by vehicle type for estimation fallback
    private static readonly Dictionary<string, double> DefaultConsumptionRates = new(StringComparer.OrdinalIgnoreCase)
    {
        { "citadine", 6.5 }, { "berline", 7.5 }, { "suv", 9.0 }, { "camion", 25.0 },
        { "camionnette", 10.0 }, { "fourgon", 11.0 }, { "utilitaire", 10.0 }, { "bus", 30.0 },
        { "moto", 4.0 }, { "pickup", 11.0 }, { "van", 9.5 }, { "minibus", 15.0 }
    };

    private FuelAnalyticsDto BuildFuelAnalytics(List<Vehicle> vehicles, List<GpsPosition> positions, RealCostData real)
    {
        var analytics = new FuelAnalyticsDto();
        var fuelEfficiencies = new List<double>();
        var byDevice = positions.ToLookup(p => p.DeviceId);
        double coveredDistance = 0;

        foreach (var vehicle in vehicles)
        {
            var realLiters = (double)real.LitersByVehicle.GetValueOrDefault(vehicle.Id);
            var hasDevice = vehicle.GpsDeviceId.HasValue;

            // Ni boîtier ni plein saisi : aucune donnée à afficher pour ce véhicule.
            if (!hasDevice && realLiters <= 0) continue;

            var vehiclePositions = hasDevice
                ? byDevice[vehicle.GpsDeviceId!.Value].OrderBy(p => p.RecordedAt).ToList()
                : new List<GpsPosition>();

            var distance = CalculateDistance(vehiclePositions);
            if (distance <= 0
                && real.ByVehicle.TryGetValue(vehicle.Id, out var costData)
                && costData.DistanceKm is > 0)
            {
                // Véhicule sans boîtier : distance des relevés compteur des pleins.
                distance = (double)costData.DistanceKm.Value;
            }

            double consumptionLiters;
            double consumptionPer100Km;
            bool isEstimated;

            if (realLiters > 0)
            {
                // Des litres ACHETÉS priment toujours sur l'estimation capteur /
                // type de véhicule : c'est une saisie, pas un modèle.
                consumptionLiters = realLiters;
                consumptionPer100Km = distance > 0 ? consumptionLiters / distance * 100 : 0;
                isEstimated = false;
            }
            else
            {
                (consumptionLiters, consumptionPer100Km, isEstimated) =
                    CalculateVehicleFuelConsumption(vehicle, vehiclePositions, distance);
            }

            var efficiency = consumptionLiters > 0 ? distance / consumptionLiters : 0;
            if (efficiency > 0) fuelEfficiencies.Add(efficiency);

            analytics.ByVehicle.Add(new VehicleFuelConsumptionDto
            {
                VehicleId = vehicle.Id,
                VehicleName = vehicle.Name,
                TotalDistanceKm = Math.Round(distance, 2),
                TotalConsumedLiters = Math.Round(consumptionLiters, 2),
                EfficiencyKmPerLiter = Math.Round(efficiency, 2),
                ConsumptionPer100Km = Math.Round(consumptionPer100Km, 2),
                EfficiencyRating = GetEfficiencyRating(efficiency),
                IsEstimated = isEstimated
            });

            analytics.TotalFuelConsumedLiters += consumptionLiters;
            coveredDistance += distance;
        }

        analytics.TotalFuelConsumedLiters = Math.Round(analytics.TotalFuelConsumedLiters, 2);

        // Coût = dépenses carburant RÉELLES (pleins saisis + dépenses de type
        // « fuel »). Avant, c'était « litres estimés × 2,1 » : un prix en dur qui
        // ne correspondait à aucune facture. Sans saisie, le coût vaut 0.
        analytics.TotalFuelCost = Round2(real.Total.Fuel);
        analytics.IsEstimated = analytics.ByVehicle.Any(v => v.IsEstimated);

        analytics.AverageConsumptionPer100Km = coveredDistance > 0
            ? Math.Round(analytics.TotalFuelConsumedLiters / coveredDistance * 100, 2) : 0;
        analytics.AverageFuelEfficiencyKmPerLiter = analytics.TotalFuelConsumedLiters > 0
            ? Math.Round(coveredDistance / analytics.TotalFuelConsumedLiters, 2) : 0;

        if (fuelEfficiencies.Any())
        {
            analytics.Statistics = CalculateStatistics(fuelEfficiencies);
        }

        return analytics;
    }

    /// <summary>
    /// Calculate fuel consumption for a single vehicle using real sensor data.
    /// Priority: 1) FMS FuelRateLPer100Km (CAN bus)  2) FuelRaw % drops  3) Vehicle-type default
    /// </summary>
    private (double consumptionLiters, double consumptionPer100Km, bool isEstimated)
        CalculateVehicleFuelConsumption(Vehicle vehicle, List<GpsPosition> sortedPositions, double distanceKm)
    {
        if (distanceKm <= 0 || sortedPositions.Count < 2)
            return (0, 0, true);

        // === SOURCE 1: FMS CAN bus FuelRateLPer100Km (most accurate) ===
        var fmsRates = sortedPositions
            .Where(p => p.FuelRateLPer100Km.HasValue && p.FuelRateLPer100Km > 0 && p.FuelRateLPer100Km < 80)
            .Select(p => (double)p.FuelRateLPer100Km!.Value)
            .ToList();

        if (fmsRates.Count >= 5)
        {
            // Use weighted average: exclude top/bottom 10% outliers (trimmed mean)
            var sorted = fmsRates.OrderBy(r => r).ToList();
            var trimCount = Math.Max(1, sorted.Count / 10);
            var trimmed = sorted.Skip(trimCount).Take(sorted.Count - 2 * trimCount).ToList();
            var avgRate = trimmed.Any() ? trimmed.Average() : sorted.Average();
            var consumed = (avgRate / 100.0) * distanceKm;
            return (consumed, avgRate, false);
        }

        // === SOURCE 2: FuelRaw sensor % drops ===
        var fuelPositions = sortedPositions
            .Where(p => p.FuelRaw.HasValue && p.FuelRaw >= 0 && p.FuelRaw <= 100)
            .ToList();

        if (fuelPositions.Count >= 3)
        {
            // Check for binary oscillation (broken sensor: only 0 and 100)
            var extremeCount = fuelPositions.Count(p => p.FuelRaw <= 2 || p.FuelRaw >= 98);
            var extremeRatio = (double)extremeCount / fuelPositions.Count;
            var largeSwings = 0;
            for (int i = 1; i < fuelPositions.Count; i++)
            {
                if (Math.Abs(fuelPositions[i].FuelRaw!.Value - fuelPositions[i - 1].FuelRaw!.Value) > 50)
                    largeSwings++;
            }
            var swingRatio = (double)largeSwings / (fuelPositions.Count - 1);

            if (extremeRatio <= 0.6 || swingRatio <= 0.1)
            {
                // Sensor data looks valid — calculate consumption from fuel % drops
                var tankCapacity = vehicle.FuelTankCapacity ?? 60;
                double totalDropPercent = 0;
                int lastFuel = fuelPositions[0].FuelRaw!.Value;

                for (int i = 1; i < fuelPositions.Count; i++)
                {
                    var currFuel = fuelPositions[i].FuelRaw!.Value;
                    var delta = currFuel - lastFuel;

                    if (delta >= 10)
                    {
                        // Refuel event — skip, don't count as consumption
                        lastFuel = currFuel;
                        continue;
                    }
                    if (delta > 0)
                    {
                        // Small positive change — sensor noise, skip
                        continue;
                    }
                    var drop = -delta;
                    if (drop > 0 && drop < 50)
                    {
                        totalDropPercent += drop;
                    }
                    lastFuel = currFuel;
                }

                if (totalDropPercent > 0)
                {
                    var consumedLiters = (totalDropPercent / 100.0) * tankCapacity;
                    var per100Km = (consumedLiters / distanceKm) * 100.0;
                    // Sanity check: reject if result is clearly wrong (< 2 or > 60 L/100km)
                    if (per100Km >= 2.0 && per100Km <= 60.0)
                        return (consumedLiters, per100Km, false);
                }
            }
        }

        // === SOURCE 3: Fallback — vehicle type default rate ===
        var vehicleType = vehicle.Type?.ToLower() ?? "berline";
        var defaultRate = DefaultConsumptionRates.GetValueOrDefault(vehicleType, 8.0);
        var estimatedLiters = (defaultRate / 100.0) * distanceKm;
        return (estimatedLiters, defaultRate, true);
    }

    /// <summary>
    /// Interventions réelles de la période : dépenses d'entretien saisies
    /// (<c>vehicle_costs</c> type <c>maintenance</c>/<c>entretien</c>) et
    /// réparations (table <c>repairs</c>, annulées exclues). Le type de
    /// réparation vient de la colonne <c>repair_type</c>, déduit de la
    /// description quand elle est vide (<see cref="RepairTypeClassifier"/>).
    /// </summary>
    private MaintenanceAnalyticsDto BuildMaintenance(List<Vehicle> vehicles, RealCostData real)
    {
        var entretiens = real.Expenses.Where(e => IsMaintenanceType(e.Type)).ToList();
        var repairs = real.Repairs;
        var total = real.Total.Maintenance + real.Total.Repair;
        var covered = real.ByVehicle.Count > 0 ? real.ByVehicle.Count : vehicles.Count;

        var maintenance = new MaintenanceAnalyticsDto
        {
            TotalMaintenanceEvents = entretiens.Count + repairs.Count,
            TotalMaintenanceCost = Round2(total),
            // Rien en base ne distingue une intervention planifiée d'une
            // intervention subie : ces deux compteurs restent à zéro plutôt que
            // de reconduire la répartition 70/30 inventée par l'ancien code.
            ScheduledMaintenances = 0,
            UnscheduledMaintenances = 0,
            AvgMaintenanceCostPerVehicle = covered > 0
                ? Math.Round((double)total / covered, 2) : 0
        };

        var byType = new List<MaintenanceTypeBreakdownDto>();
        if (entretiens.Count > 0)
        {
            byType.Add(new MaintenanceTypeBreakdownDto
            {
                Type = "Entretien",
                Count = entretiens.Count,
                TotalCost = Round2(entretiens.Sum(e => e.Amount))
            });
        }

        byType.AddRange(repairs
            .GroupBy(r => RepairTypeClassifier.Classify(r.RepairType, r.Description).Type)
            .Select(g => new MaintenanceTypeBreakdownDto
            {
                Type = RepairTypeClassifier.Label(g.Key),
                Count = g.Count(),
                TotalCost = Round2(g.Sum(r => r.TotalCost))
            }));

        foreach (var line in byType)
            line.Percentage = total != 0 ? Math.Round((double)(line.TotalCost / total) * 100, 1) : 0;

        maintenance.ByType = byType.OrderByDescending(t => t.TotalCost).ToList();

        DateTime? LastInterventionDate(int vehicleId)
        {
            var dates = entretiens.Where(e => e.VehicleId == vehicleId).Select(e => e.Date)
                .Concat(repairs.Where(r => r.VehicleId == vehicleId).Select(r => r.Date))
                .ToList();
            return dates.Count > 0 ? dates.Max() : null;
        }

        maintenance.ByVehicle = real.ByVehicle.Values
            .Where(v => v.Total.Maintenance != 0 || v.Total.Repair != 0)
            .Select(v => new VehicleMaintenanceDto
            {
                VehicleId = v.VehicleId,
                VehicleName = v.VehicleName,
                MaintenanceCount = entretiens.Count(e => e.VehicleId == v.VehicleId)
                                 + repairs.Count(r => r.VehicleId == v.VehicleId),
                TotalCost = Round2(v.Total.Maintenance + v.Total.Repair),
                LastMaintenanceDate = LastInterventionDate(v.VehicleId)
            })
            .OrderByDescending(v => v.TotalCost)
            .ToList();

        maintenance.RecentEvents = entretiens
            .Select(e => new MaintenanceEventDto
            {
                VehicleId = e.VehicleId,
                VehicleName = VehicleLabel(real, e.VehicleId),
                Type = "Entretien",
                Date = e.Date,
                Cost = Round2(e.Amount),
                Description = e.Description ?? string.Empty
            })
            .Concat(repairs.Select(r => new MaintenanceEventDto
            {
                Id = r.Id,
                VehicleId = r.VehicleId,
                VehicleName = VehicleLabel(real, r.VehicleId),
                Type = RepairTypeClassifier.Label(RepairTypeClassifier.Classify(r.RepairType, r.Description).Type),
                Date = r.Date,
                Cost = Round2(r.TotalCost),
                Description = r.Description ?? string.Empty
            }))
            .OrderByDescending(e => e.Date)
            .Take(20)
            .ToList();

        // Upcoming : aucune échéance d'entretien planifiée n'est exploitable ici
        // (maintenance_logs a company_id = 0 sur toute la base) — liste vide
        // plutôt qu'une projection inventée.
        return maintenance;
    }

    private static string VehicleLabel(RealCostData real, int vehicleId) =>
        real.ByVehicle.TryGetValue(vehicleId, out var v) ? v.VehicleName : $"#{vehicleId}";

    private DriverPerformanceDto BuildDriverPerformance(List<Driver> drivers, List<Vehicle> vehicles, List<GpsPosition> positions)
    {
        var performance = new DriverPerformanceDto
        {
            TotalDrivers = drivers.Count,
            ActiveDrivers = drivers.Count(d => vehicles.Any(v => v.AssignedDriverId == d.Id))
        };

        var scores = new List<double>();

        foreach (var driver in drivers)
        {
            var assignedVehicles = vehicles.Where(v => v.AssignedDriverId == driver.Id).ToList();
            var deviceIds = assignedVehicles.Where(v => v.GpsDeviceId.HasValue).Select(v => v.GpsDeviceId!.Value).ToList();
            var driverPositions = positions.Where(p => deviceIds.Contains(p.DeviceId)).ToList();

            if (!driverPositions.Any()) continue;

            var distance = CalculateDistance(driverPositions);
            var speeds = driverPositions.Where(p => p.SpeedKph > 0).Select(p => p.SpeedKph ?? 0).ToList();
            var avgSpeed = speeds.Any() ? speeds.Average() : 0;
            
            // Simple scoring based on average speed (penalize extremes)
            var speedScore = avgSpeed > 0 && avgSpeed < 120 ? 80 + (20 - Math.Abs(avgSpeed - 60) / 3) : 60;
            var score = Math.Min(100, Math.Max(0, speedScore));
            scores.Add(score);

            performance.DriverMetrics.Add(new DriverMetricsDto
            {
                DriverId = driver.Id,
                DriverName = driver.FullName,
                TotalDistanceKm = Math.Round(distance, 2),
                TotalTrips = CountTrips(driverPositions),
                AvgSpeedKph = Math.Round(avgSpeed, 1),
                HarshBrakingEvents = 0, // Would need acceleration data
                HarshAccelerationEvents = 0,
                SpeedingEvents = driverPositions.Count(p => p.SpeedKph > 120),
                FuelEfficiency = distance > 0 ? Math.Round(CalculateFuelEfficiencyForPositions(driverPositions, distance), 2) : 0,
                PerformanceScore = Math.Round(score, 1),
                Rating = GetPerformanceRating(score)
            });
        }

        performance.AveragePerformanceScore = scores.Any() ? Math.Round(scores.Average(), 1) : 0;
        
        performance.TopPerformers = performance.DriverMetrics
            .OrderByDescending(d => d.PerformanceScore)
            .Take(5)
            .Select((d, i) => new DriverRankingDto
            {
                Rank = i + 1,
                DriverId = d.DriverId,
                DriverName = d.DriverName,
                Score = d.PerformanceScore,
                Trend = "stable"
            }).ToList();

        performance.NeedsImprovement = performance.DriverMetrics
            .OrderBy(d => d.PerformanceScore)
            .Take(3)
            .Select((d, i) => new DriverRankingDto
            {
                Rank = i + 1,
                DriverId = d.DriverId,
                DriverName = d.DriverName,
                Score = d.PerformanceScore,
                Trend = "stable"
            }).ToList();

        if (scores.Any())
        {
            performance.Statistics = CalculateStatistics(scores);
        }

        return performance;
    }

    /// <summary>
    /// Efficacité opérationnelle — le seul indicateur MESURABLE ici est le temps
    /// d'inactivité, déduit des positions (part des trames à moins de 3 km/h).
    ///
    /// « Disponibilité flotte » (95), « Livraisons à temps » (92) et « Efficacité
    /// itinéraires » (88) étaient trois constantes en dur : aucune table ne les
    /// alimente — rien n'enregistre une promesse de livraison ni un itinéraire de
    /// référence. Elles se moyennaient en un « score d'efficacité » qui valait
    /// donc TOUJOURS 91,7, affiché en vert avec un objectif de 85 et un écart de
    /// +6,7, pour toutes les sociétés et tous les mois. Elles sont retirées, avec
    /// le KPI qui en dérivait — même principe que les compteurs « Planifiées /
    /// Non planifiées » de la maintenance.
    /// </summary>
    private OperationalEfficiencyDto BuildEfficiency(List<GpsPosition> positions, int daysInMonth)
    {
        var efficiency = new OperationalEfficiencyDto();

        var totalPositions = positions.Count;
        var movingPositions = positions.Count(p => p.SpeedKph > 3);
        var idlePositions = totalPositions - movingPositions;

        efficiency.IdleTimePercentage = totalPositions > 0
            ? Math.Round((double)idlePositions / totalPositions * 100, 1) : 0;

        efficiency.Metrics = new List<EfficiencyMetricDto>
        {
            new() { Name = "Temps d'inactivité", Value = efficiency.IdleTimePercentage, Target = 20,
                    Variance = 20 - efficiency.IdleTimePercentage, Status = efficiency.IdleTimePercentage <= 20 ? "OnTarget" : "Above" }
        };

        return efficiency;
    }

    /// <summary>
    /// Coûts RÉELS de la période. Correspondance avec le DTO, qui n'a pas de
    /// champ dédié aux réparations : <c>MaintenanceCost</c> = entretiens saisis
    /// + réparations (elles ne doivent pas disparaître du total), le détail des
    /// deux restant visible dans <c>ByCategory</c>. <c>InsuranceCost</c> est la
    /// part réelle des dépenses de type assurance, <c>OtherCosts</c> tout le
    /// reste. La somme des quatre champs redonne exactement le total.
    /// </summary>
    private CostAnalysisDto BuildCostAnalysis(List<Vehicle> vehicles, List<GpsPosition> positions, RealCostData real)
    {
        var insurance = real.Expenses.Where(e => IsInsuranceType(e.Type)).Sum(e => e.Amount);

        var costs = new CostAnalysisDto
        {
            FuelCost = Round2(real.Total.Fuel),
            MaintenanceCost = Round2(real.Total.Maintenance + real.Total.Repair),
            InsuranceCost = Round2(insurance),
            OtherCosts = Round2(real.Total.Other - insurance),
            TotalOperationalCost = Round2(real.Total.Total)
        };

        var distanceByVehicle = CostDistanceByVehicle(vehicles, positions, real);
        var totalDistance = distanceByVehicle.Values.Sum();

        // Dénominateur = les véhicules RÉELLEMENT couverts par les dépenses
        // chargées (un utilisateur restreint ne voit que les siens), sinon la
        // moyenne serait diluée par des véhicules hors de sa portée.
        var covered = real.ByVehicle.Count > 0 ? real.ByVehicle.Count : vehicles.Count;

        costs.CostPerKm = totalDistance > 0
            ? Math.Round(real.Total.Total / (decimal)totalDistance, 3) : 0;
        costs.CostPerVehicle = covered > 0
            ? Math.Round(real.Total.Total / covered, 2) : 0;

        costs.ByCategory = BuildCostCategories(real);

        foreach (var vehicle in real.ByVehicle.Values
                     .Where(v => v.Total.Total != 0)
                     .OrderByDescending(v => v.Total.Total))
        {
            var km = distanceByVehicle.GetValueOrDefault(vehicle.VehicleId);
            costs.ByVehicle.Add(new VehicleCostDto
            {
                VehicleId = vehicle.VehicleId,
                VehicleName = vehicle.VehicleName,
                TotalCost = Round2(vehicle.Total.Total),
                FuelCost = Round2(vehicle.Total.Fuel),
                MaintenanceCost = Round2(vehicle.Total.Maintenance + vehicle.Total.Repair),
                CostPerKm = km > 0 ? Math.Round(vehicle.Total.Total / (decimal)km, 3) : 0
            });
        }

        return costs;
    }

    /// <summary>
    /// Répartition par catégorie : carburant / entretien / réparations, puis une
    /// ligne par type de dépense réellement saisi (assurance, visite technique,
    /// vignette, péage…). Seules les lignes non nulles sont rendues — quatre
    /// lignes figées valaient mieux que rien, mais elles étaient fausses.
    /// </summary>
    private List<CostBreakdownDto> BuildCostCategories(RealCostData real)
    {
        var lines = new List<(string Label, decimal Amount)>
        {
            ("Carburant", real.Total.Fuel),
            ("Entretien", real.Total.Maintenance),
            ("Réparations", real.Total.Repair)
        };

        lines.AddRange(real.Expenses
            .Where(e => !IsFuelType(e.Type) && !IsMaintenanceType(e.Type))
            .GroupBy(e => NormalizeType(e.Type))
            .Select(g => (Label: CategoryLabel(g.Key), Amount: g.Sum(e => e.Amount)))
            .OrderByDescending(l => l.Amount));

        var total = real.Total.Total;
        return lines
            .Where(l => l.Amount != 0)
            .Select(l => new CostBreakdownDto
            {
                Category = l.Label,
                Amount = Round2(l.Amount),
                Percentage = total != 0 ? Math.Round((double)(l.Amount / total) * 100, 1) : 0
            })
            .ToList();
    }

    /// <summary>Libellé français d'un type de <c>vehicle_costs</c> (valeurs réellement présentes en base).</summary>
    private static string CategoryLabel(string type) => type switch
    {
        "insurance" or "assurance" => "Assurance",
        "insurance_refund" => "Remboursement assurance",
        "technical_inspection" or "visite_technique" => "Visite technique",
        "tax" or "vignette" => "Taxe / vignette",
        "peage" or "toll" => "Péage",
        "registration" => "Carte grise",
        "repair" or "reparation" => "Réparation (dépense)",
        "parking" => "Parking",
        "lavage" or "wash" => "Lavage",
        "" => "Autres",
        _ => char.ToUpperInvariant(type[0]) + type[1..].Replace('_', ' ')
    };

    /// <summary>
    /// Kilométrage retenu pour les ratios de coût : la distance MESURÉE de
    /// l'agrégateur (trajets terminés, ou relevés compteur des pleins pour un
    /// véhicule sans boîtier) — la même que les rapports de coûts, pour que le
    /// coût/km ne diverge pas d'un écran à l'autre — avec repli sur la distance
    /// échantillonnée des positions quand elle manque.
    /// </summary>
    private Dictionary<int, double> CostDistanceByVehicle(
        List<Vehicle> vehicles, List<GpsPosition> positions, RealCostData real)
    {
        var byDevice = positions.ToLookup(p => p.DeviceId);
        var result = new Dictionary<int, double>();

        foreach (var vehicle in vehicles)
        {
            double km = 0;
            if (real.ByVehicle.TryGetValue(vehicle.Id, out var data) && data.DistanceKm is > 0)
                km = (double)data.DistanceKm.Value;
            else if (vehicle.GpsDeviceId.HasValue)
                km = CalculateDistance(byDevice[vehicle.GpsDeviceId.Value].ToList());

            result[vehicle.Id] = km;
        }

        return result;
    }

    private PeriodComparisonDto BuildComparison(string period, List<GpsPosition> current, List<GpsPosition> previous,
        decimal currentCost, decimal previousCost)
    {
        var currentDistance = CalculateDistance(current);
        var previousDistance = CalculateDistance(previous);
        
        var currentTrips = CountTrips(current);
        var previousTrips = CountTrips(previous);

        return new PeriodComparisonDto
        {
            ComparisonPeriod = period,
            Distance = BuildComparisonMetric("Distance", currentDistance, previousDistance, "km", true),
            FuelConsumption = BuildComparisonMetric("Carburant", 
                CalculateFuelFromPositions(current), CalculateFuelFromPositions(previous), "L", false),
            // Coût = dépenses réelles des deux périodes (avant : litres estimés × 2,1).
            Cost = BuildComparisonMetric("Coût",
                (double)currentCost, (double)previousCost, GisAPI.Domain.Common.AppCurrency.Default, false),
            Utilization = BuildComparisonMetric("Utilisation", 
                current.Select(p => p.DeviceId).Distinct().Count(),
                previous.Select(p => p.DeviceId).Distinct().Count(), "%", true),
            Trips = BuildComparisonMetric("Trajets", currentTrips, previousTrips, "", true)
        };
    }

    private ComparisonMetricDto BuildComparisonMetric(string name, double current, double previous, string unit, bool higherIsBetter)
    {
        var change = current - previous;
        var changePercent = previous > 0 ? (change / previous) * 100 : 0;

        return new ComparisonMetricDto
        {
            MetricName = name,
            CurrentValue = Math.Round(current, 2),
            PreviousValue = Math.Round(previous, 2),
            Change = Math.Round(change, 2),
            ChangePercent = Math.Round(changePercent, 1),
            Trend = change > 0 ? "increase" : change < 0 ? "decrease" : "stable",
            IsPositiveTrend = higherIsBetter ? change >= 0 : change <= 0
        };
    }

    private ExecutiveSummaryDto BuildExecutiveSummary(MonthlyFleetReportDto report, int drivingMinutes)
    {
        var summary = new ExecutiveSummaryDto
        {
            TotalVehicles = report.FleetOverview.TotalVehicles,
            ActiveVehicles = report.FleetOverview.ActiveVehicles,
            TotalDistanceKm = report.Utilization.ByVehicle.Sum(v => v.TotalDistanceKm),
            TotalFuelConsumedLiters = report.FuelAnalytics.TotalFuelConsumedLiters,
            TotalOperationalCost = report.CostAnalysis.TotalOperationalCost,
            FleetUtilizationRate = report.Utilization.OverallUtilizationRate,
            AverageFuelEfficiency = report.FuelAnalytics.AverageFuelEfficiencyKmPerLiter,
            TotalTrips = report.Utilization.ByVehicle.Sum(v => v.TotalTrips),
            // Durée MESURÉE des trajets terminés, pas « jours actifs × 8 ».
            TotalDrivingHours = (int)Math.Round(drivingMinutes / 60.0, MidpointRounding.AwayFromZero)
        };

        // Generate insights
        summary.KeyInsights = new List<string>();
        
        if (report.Utilization.OverallUtilizationRate < 70)
            summary.KeyInsights.Add($"⚠️ Taux d'utilisation faible ({report.Utilization.OverallUtilizationRate}%) - Optimisation de la flotte recommandée");
        else
            summary.KeyInsights.Add($"✅ Bon taux d'utilisation de la flotte ({report.Utilization.OverallUtilizationRate}%)");

        if (report.MonthOverMonth.Distance.ChangePercent > 10)
            summary.KeyInsights.Add($"📈 Distance parcourue en hausse de {report.MonthOverMonth.Distance.ChangePercent}% vs mois précédent");
        else if (report.MonthOverMonth.Distance.ChangePercent < -10)
            summary.KeyInsights.Add($"📉 Distance parcourue en baisse de {Math.Abs(report.MonthOverMonth.Distance.ChangePercent)}% vs mois précédent");

        summary.KeyInsights.Add($"⛽ Consommation moyenne: {report.FuelAnalytics.AverageConsumptionPer100Km} L/100km");

        // Generate recommendations
        summary.Recommendations = new List<string>();
        
        if (report.Utilization.OverallUtilizationRate < 70)
            summary.Recommendations.Add("Envisager la réduction de la taille de la flotte ou l'augmentation des missions");
        
        if (report.FuelAnalytics.AverageConsumptionPer100Km > 10)
            summary.Recommendations.Add("Formation éco-conduite recommandée pour réduire la consommation");

        if (report.DriverPerformance.NeedsImprovement.Any())
            summary.Recommendations.Add($"Suivi individuel recommandé pour {report.DriverPerformance.NeedsImprovement.Count} conducteurs");

        return summary;
    }

    private List<AlertDto> BuildAlerts(MonthlyFleetReportDto report, List<Vehicle> vehicles)
    {
        var alerts = new List<AlertDto>();

        // Low utilization alert
        foreach (var vehicle in report.Utilization.ByVehicle.Where(v => v.UtilizationRate < 30))
        {
            alerts.Add(new AlertDto
            {
                Id = Guid.NewGuid().ToString(),
                Type = "LowUtilization",
                Severity = "Warning",
                Title = "Faible utilisation",
                Description = $"Véhicule {vehicle.VehicleName} avec seulement {vehicle.UtilizationRate}% d'utilisation",
                DetectedAt = DateTime.UtcNow,
                VehicleId = vehicle.VehicleId,
                VehicleName = vehicle.VehicleName,
                RecommendedAction = "Évaluer la nécessité de ce véhicule dans la flotte"
            });
        }

        // High fuel consumption alert
        foreach (var vehicle in report.FuelAnalytics.ByVehicle.Where(v => v.ConsumptionPer100Km > 12))
        {
            alerts.Add(new AlertDto
            {
                Id = Guid.NewGuid().ToString(),
                Type = "HighFuelConsumption",
                Severity = "Warning",
                Title = "Consommation élevée",
                Description = $"Véhicule {vehicle.VehicleName} consomme {vehicle.ConsumptionPer100Km} L/100km",
                DetectedAt = DateTime.UtcNow,
                VehicleId = vehicle.VehicleId,
                VehicleName = vehicle.VehicleName,
                RecommendedAction = "Vérifier l'état du véhicule et le style de conduite"
            });
        }

        // Low driver performance
        foreach (var driver in report.DriverPerformance.DriverMetrics.Where(d => d.PerformanceScore < 60))
        {
            alerts.Add(new AlertDto
            {
                Id = Guid.NewGuid().ToString(),
                Type = "LowDriverPerformance",
                Severity = "Info",
                Title = "Performance conducteur à améliorer",
                Description = $"Conducteur {driver.DriverName} avec un score de {driver.PerformanceScore}",
                DetectedAt = DateTime.UtcNow,
                RecommendedAction = "Planifier une session de formation"
            });
        }

        return alerts.OrderByDescending(a => a.Severity == "Critical")
            .ThenByDescending(a => a.Severity == "Warning")
            .Take(10)
            .ToList();
    }

    private List<KpiDto> BuildKpis(MonthlyFleetReportDto report)
    {
        return new List<KpiDto>
        {
            new() {
                Name = "Taux d'utilisation flotte",
                Category = "Utilisation",
                Value = report.Utilization.OverallUtilizationRate,
                Target = 80,
                Variance = report.Utilization.OverallUtilizationRate - 80,
                VariancePercent = ((report.Utilization.OverallUtilizationRate - 80) / 80) * 100,
                Unit = "%",
                Status = report.Utilization.OverallUtilizationRate >= 80 ? "OnTarget" : "Below",
                Trend = report.MonthOverMonth.Utilization.Trend
            },
            new() {
                Name = "Consommation moyenne",
                Category = "Carburant",
                Value = report.FuelAnalytics.AverageConsumptionPer100Km,
                Target = 8,
                Variance = report.FuelAnalytics.AverageConsumptionPer100Km - 8,
                VariancePercent = ((report.FuelAnalytics.AverageConsumptionPer100Km - 8) / 8) * 100,
                Unit = "L/100km",
                Status = report.FuelAnalytics.AverageConsumptionPer100Km <= 8 ? "OnTarget" : "Above",
                Trend = "stable"
            },
            new() {
                Name = "Coût par kilomètre",
                Category = "Coûts",
                Value = (double)report.CostAnalysis.CostPerKm,
                Target = 0.25,
                Variance = (double)report.CostAnalysis.CostPerKm - 0.25,
                VariancePercent = (((double)report.CostAnalysis.CostPerKm - 0.25) / 0.25) * 100,
                Unit = $"{GisAPI.Domain.Common.AppCurrency.Default}/km",
                Status = (double)report.CostAnalysis.CostPerKm <= 0.25 ? "OnTarget" : "Above",
                Trend = "stable"
            },
            new() {
                Name = "Score performance conducteurs",
                Category = "Conducteurs",
                Value = report.DriverPerformance.AveragePerformanceScore,
                Target = 75,
                Variance = report.DriverPerformance.AveragePerformanceScore - 75,
                VariancePercent = ((report.DriverPerformance.AveragePerformanceScore - 75) / 75) * 100,
                Unit = "points",
                Status = report.DriverPerformance.AveragePerformanceScore >= 75 ? "OnTarget" : "Below",
                Trend = "stable"
            }
            // Pas de KPI « Efficacité opérationnelle » : il moyennait trois
            // constantes en dur et valait toujours 91,7 (voir BuildEfficiency).
        };
    }

    private ChartDataCollectionDto BuildChartData(MonthlyFleetReportDto report, List<Vehicle> vehicles, 
        List<GpsPosition> positions, DateTime startDate, int daysInMonth)
    {
        var charts = new ChartDataCollectionDto();

        // Column: Utilization by vehicle type
        charts.UtilizationByVehicleType = new ChartDataDto
        {
            Title = "Utilisation par type de véhicule",
            Type = "column",
            Labels = report.FleetOverview.ByType.Select(t => t.Type).ToList(),
            Values = report.FleetOverview.ByType.Select(t => t.AvgDistanceKm).ToList(),
            Unit = "km"
        };

        // Column: Maintenance cost by type
        charts.MaintenanceCostByType = new ChartDataDto
        {
            Title = "Coûts maintenance par type",
            Type = "column",
            Labels = report.Maintenance.ByType.Select(t => t.Type).ToList(),
            Values = report.Maintenance.ByType.Select(t => (double)t.TotalCost).ToList(),
            Unit = GisAPI.Domain.Common.AppCurrency.Default
        };

        // Line: Daily distance trend
        charts.DailyDistanceTrend = new MultiSeriesChartDataDto
        {
            Title = "Distance journalière",
            Type = "line",
            Labels = report.Utilization.DailyTrend.Select(d => d.Date.ToString("dd/MM")).ToList(),
            XAxisLabel = "Date",
            YAxisLabel = "Distance (km)",
            Series = new List<ChartSeriesDto>
            {
                new() { Name = "Distance", Data = report.Utilization.DailyTrend.Select(d => d.TotalDistanceKm).ToList(), Color = "#3B82F6" }
            }
        };

        // Line: Efficiency trend
        charts.EfficiencyTrend = new MultiSeriesChartDataDto
        {
            Title = "Tendance efficacité",
            Type = "line",
            Labels = report.Utilization.DailyTrend.Select(d => d.Date.ToString("dd/MM")).ToList(),
            XAxisLabel = "Date",
            YAxisLabel = "Taux (%)",
            Series = new List<ChartSeriesDto>
            {
                new() { Name = "Utilisation", Data = report.Utilization.DailyTrend.Select(d => d.UtilizationRate).ToList(), Color = "#10B981" }
            }
        };

        // Pie: Fleet composition
        charts.FleetComposition = new ChartDataDto
        {
            Title = "Composition de la flotte",
            Type = "pie",
            Labels = report.FleetOverview.ByType.Select(t => t.Type).ToList(),
            Values = report.FleetOverview.ByType.Select(t => (double)t.Count).ToList(),
            Colors = new List<string> { "#3B82F6", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6" }
        };

        // Pie: Cost distribution
        charts.CostDistribution = new ChartDataDto
        {
            Title = "Répartition des coûts",
            Type = "pie",
            Labels = report.CostAnalysis.ByCategory.Select(c => c.Category).ToList(),
            Values = report.CostAnalysis.ByCategory.Select(c => (double)c.Amount).ToList(),
            Colors = new List<string> { "#EF4444", "#F59E0B", "#3B82F6", "#6B7280" }
        };

        // Pie: Vehicle status
        charts.VehicleStatusDistribution = new ChartDataDto
        {
            Title = "Statut des véhicules",
            Type = "pie",
            Labels = report.FleetOverview.ByStatus.Select(s => s.Status).ToList(),
            Values = report.FleetOverview.ByStatus.Select(s => (double)s.Count).ToList(),
            Colors = new List<string> { "#10B981", "#F59E0B", "#EF4444", "#6B7280" }
        };

        // Bar: Vehicle performance ranking
        var topVehicles = report.Utilization.ByVehicle.OrderByDescending(v => v.TotalDistanceKm).Take(10).ToList();
        charts.VehiclePerformanceRanking = new ChartDataDto
        {
            Title = "Top 10 véhicules par distance",
            Type = "bar",
            Labels = topVehicles.Select(v => v.VehicleName).ToList(),
            Values = topVehicles.Select(v => v.TotalDistanceKm).ToList(),
            Unit = "km"
        };

        // Bar: Driver ranking
        charts.DriverRanking = new ChartDataDto
        {
            Title = "Classement conducteurs",
            Type = "bar",
            Labels = report.DriverPerformance.TopPerformers.Select(d => d.DriverName).ToList(),
            Values = report.DriverPerformance.TopPerformers.Select(d => d.Score).ToList(),
            Unit = "points"
        };

        return charts;
    }

    // ==================== HELPER METHODS ====================

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Estimate total fuel consumed from a set of positions (for period comparisons).
    /// Uses FMS rates when available, otherwise FuelRaw drops, otherwise distance-based estimate.
    /// </summary>
    private double CalculateFuelFromPositions(List<GpsPosition> positions)
    {
        if (!positions.Any()) return 0;

        var distance = CalculateDistance(positions);
        if (distance <= 0) return 0;

        // Try FMS CAN bus rates first
        var fmsRates = positions
            .Where(p => p.FuelRateLPer100Km.HasValue && p.FuelRateLPer100Km > 0 && p.FuelRateLPer100Km < 80)
            .Select(p => (double)p.FuelRateLPer100Km!.Value)
            .ToList();

        if (fmsRates.Count >= 5)
        {
            var avgRate = fmsRates.OrderBy(r => r)
                .Skip(fmsRates.Count / 10)
                .Take(fmsRates.Count - 2 * (fmsRates.Count / 10))
                .DefaultIfEmpty(fmsRates.Average())
                .Average();
            return (avgRate / 100.0) * distance;
        }

        // Try FuelRaw sensor drops
        var fuelPositions = positions
            .Where(p => p.FuelRaw.HasValue && p.FuelRaw >= 0 && p.FuelRaw <= 100)
            .OrderBy(p => p.RecordedAt)
            .ToList();

        if (fuelPositions.Count >= 3)
        {
            double totalDrop = 0;
            int lastFuel = fuelPositions[0].FuelRaw!.Value;
            for (int i = 1; i < fuelPositions.Count; i++)
            {
                var curr = fuelPositions[i].FuelRaw!.Value;
                var delta = curr - lastFuel;
                if (delta >= 10) { lastFuel = curr; continue; } // refuel
                if (delta > 0) continue; // noise
                var drop = -delta;
                if (drop > 0 && drop < 50) totalDrop += drop;
                lastFuel = curr;
            }
            if (totalDrop > 0)
            {
                var liters = (totalDrop / 100.0) * 60; // assume 60L tank
                var rate = (liters / distance) * 100.0;
                if (rate >= 2.0 && rate <= 60.0) return liters;
            }
        }

        // Fallback: 8 L/100km default
        return (8.0 / 100.0) * distance;
    }

    /// <summary>
    /// Calculate fuel efficiency (km/L) for a set of positions.
    /// </summary>
    private double CalculateFuelEfficiencyForPositions(List<GpsPosition> driverPositions, double distance)
    {
        if (distance <= 0) return 0;
        var fuel = CalculateFuelFromPositions(driverPositions);
        return fuel > 0 ? distance / fuel : 0;
    }

    private double CalculateDistance(List<GpsPosition> positions)
    {
        double totalDistance = 0;
        var sortedPositions = positions.OrderBy(p => p.RecordedAt).ToList();
        
        for (int i = 1; i < sortedPositions.Count; i++)
        {
            var prev = sortedPositions[i - 1];
            var curr = sortedPositions[i];
            
            if ((curr.SpeedKph ?? 0) > 0 || (prev.SpeedKph ?? 0) > 0)
            {
                totalDistance += HaversineDistance(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
            }
        }
        
        return totalDistance;
    }

    private double CalculateDistanceForVehicles(List<Vehicle> vehicles, List<GpsPosition> allPositions)
    {
        var deviceIds = vehicles.Where(v => v.GpsDeviceId.HasValue).Select(v => v.GpsDeviceId!.Value).ToList();
        var positions = allPositions.Where(p => deviceIds.Contains(p.DeviceId)).ToList();
        return CalculateDistance(positions);
    }

    private int CountTrips(List<GpsPosition> positions)
    {
        int tripCount = 0;
        bool wasMoving = false;

        foreach (var pos in positions.OrderBy(p => p.RecordedAt))
        {
            var isMoving = (pos.SpeedKph ?? 0) > 3.0;
            if (isMoving && !wasMoving) tripCount++;
            wasMoving = isMoving;
        }

        return tripCount;
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private StatisticalMetricsDto CalculateStatistics(List<double> values)
    {
        if (!values.Any()) return new StatisticalMetricsDto();

        var sorted = values.OrderBy(v => v).ToList();
        var count = sorted.Count;
        var mean = sorted.Average();
        var variance = sorted.Sum(v => Math.Pow(v - mean, 2)) / count;

        return new StatisticalMetricsDto
        {
            Mean = Math.Round(mean, 2),
            Median = Math.Round(sorted[count / 2], 2),
            StandardDeviation = Math.Round(Math.Sqrt(variance), 2),
            Variance = Math.Round(variance, 2),
            Min = Math.Round(sorted.First(), 2),
            Max = Math.Round(sorted.Last(), 2),
            Range = Math.Round(sorted.Last() - sorted.First(), 2),
            Percentile25 = Math.Round(sorted[(int)(count * 0.25)], 2),
            Percentile75 = Math.Round(sorted[(int)(count * 0.75)], 2),
            InterquartileRange = Math.Round(sorted[(int)(count * 0.75)] - sorted[(int)(count * 0.25)], 2)
        };
    }

    private string GetEfficiencyRating(double kmPerLiter) => kmPerLiter switch
    {
        >= 15 => "Excellent",
        >= 12 => "Bon",
        >= 10 => "Moyen",
        _ => "Faible"
    };

    private string GetPerformanceRating(double score) => score switch
    {
        >= 90 => "Excellent",
        >= 75 => "Bon",
        >= 60 => "Moyen",
        _ => "À améliorer"
    };
}




