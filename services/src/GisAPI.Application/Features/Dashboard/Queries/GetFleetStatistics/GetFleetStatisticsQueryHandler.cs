using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Dashboard.Queries.GetFleetStatistics;

public class GetFleetStatisticsQueryHandler : IRequestHandler<GetFleetStatisticsQuery, FleetStatisticsDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetFleetStatisticsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<FleetStatisticsDto> Handle(GetFleetStatisticsQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? 0;
        
        var now = DateTime.UtcNow;
        var year = request.Year ?? now.Year;
        var month = request.Month ?? now.Month;
        
        var periodStart = DateTime.SpecifyKind(new DateTime(year, month, 1), DateTimeKind.Utc);
        var periodEnd = DateTime.SpecifyKind(periodStart.AddMonths(1).AddSeconds(-1), DateTimeKind.Utc);
        var daysInPeriod = (periodEnd - periodStart).Days + 1;

        // Get vehicles with drivers
        var vehiclesQuery = _context.Vehicles
            .AsNoTracking()
            .Include(v => v.AssignedDriver)
            .Where(v => v.CompanyId == companyId);
            
        if (request.VehicleIds?.Any() == true)
            vehiclesQuery = vehiclesQuery.Where(v => request.VehicleIds.Contains(v.Id));

        var vehicles = await vehiclesQuery.ToListAsync(cancellationToken);
        var vehicleIds = vehicles.Select(v => v.Id).ToList();

        // Get device mappings (Vehicle has GpsDeviceId, build reverse map)
        var deviceVehicleMap = vehicles
            .Where(v => v.GpsDeviceId.HasValue)
            .ToDictionary(v => v.GpsDeviceId!.Value, v => v.Id);

        var deviceIds = deviceVehicleMap.Keys.ToList();

        // UN SEUL parcours : agrégat par (device, jour) avec toutes les métriques
        // (≤ nb véhicules × jours du mois lignes). On en déduit les totaux par véhicule
        // (cumul des jours) ET les distances journalières — sans second scan de la table.
        List<DeviceDayAgg> perDeviceDay = new();
        if (deviceIds.Count > 0)
        {
            perDeviceDay = await _context.GpsPositions.AsNoTracking()
                .Where(p => deviceIds.Contains(p.DeviceId) && p.RecordedAt >= periodStart && p.RecordedAt <= periodEnd)
                .GroupBy(p => new { p.DeviceId, Day = p.RecordedAt.Date })
                .Select(g => new DeviceDayAgg
                {
                    DeviceId = g.Key.DeviceId,
                    Day = g.Key.Day,
                    Count = g.Count(),
                    SpeedSum = g.Sum(p => (double?)p.SpeedKph) ?? 0.0,
                    MovingCount = g.Sum(p => (p.SpeedKph ?? 0) > 5 ? 1 : 0),
                    MaxSpeed = g.Max(p => (double?)p.SpeedKph) ?? 0.0,
                    MovingSpeedSum = g.Sum(p => (p.SpeedKph ?? 0) > 0 ? (double?)p.SpeedKph : null) ?? 0.0,
                    MovingPoints = g.Sum(p => (p.SpeedKph ?? 0) > 0 ? 1 : 0)
                })
                .ToListAsync(cancellationToken);
        }

        var daysByDevice = perDeviceDay.GroupBy(x => x.DeviceId).ToDictionary(g => g.Key, g => g.ToList());

        // Calculate per-vehicle statistics
        var vehicleStats = new List<VehicleStatisticsDto>();
        var allDistances = new List<double>();
        var allFuel = new List<double>();
        var allCosts = new List<decimal>();

        foreach (var vehicle in vehicles)
        {
            var vehicleDeviceIds = deviceVehicleMap
                .Where(kvp => kvp.Value == vehicle.Id)
                .Select(kvp => kvp.Key)
                .ToList();

            // Cumul des métriques du/des device(s) du véhicule à partir des lignes par jour
            // (un seul jeu de données, pas de second scan).
            int positionCount = 0, movingCount = 0, movingPoints = 0;
            double speedSum = 0.0, movingSpeedSum = 0.0, maxSpeed = 0.0;
            var dayDistances = new Dictionary<DateTime, double>();
            foreach (var did in vehicleDeviceIds)
            {
                if (!daysByDevice.TryGetValue(did, out var days)) continue;
                foreach (var d in days)
                {
                    positionCount += d.Count;
                    movingCount += d.MovingCount;
                    movingPoints += d.MovingPoints;
                    speedSum += d.SpeedSum;
                    movingSpeedSum += d.MovingSpeedSum;
                    if (d.MaxSpeed > maxSpeed) maxSpeed = d.MaxSpeed;
                    dayDistances[d.Day] = (dayDistances.TryGetValue(d.Day, out var cur) ? cur : 0.0) + d.SpeedSum;
                }
            }

            // Calculate metrics (mêmes formules qu'avant, mais à partir des agrégats SQL)
            var totalDistance = speedSum / 60.0;
            var operatingDays = dayDistances.Count;
            var avgDailyDistance = operatingDays > 0 ? totalDistance / operatingDays : 0;
            var maxDailyDistance = dayDistances.Count > 0 ? dayDistances.Values.Max() / 60.0 : 0;

            var totalFuel = totalDistance * 0.08; // 8L/100km estimate
            var fuelCost = (decimal)(totalDistance * 0.15);
            var maintenanceCost = (decimal)(totalDistance * 0.05);
            var totalCost = fuelCost + maintenanceCost;

            var avgSpeed = movingPoints > 0 ? movingSpeedSum / movingPoints : 0;

            var stat = new VehicleStatisticsDto
            {
                VehicleId = vehicle.Id,
                VehicleName = vehicle.Name,
                Plate = vehicle.Plate,
                VehicleType = vehicle.Type,
                DriverName = vehicle.AssignedDriver?.FullName,
                
                TotalDistanceKm = Math.Round(totalDistance, 2),
                AvgDailyDistanceKm = Math.Round(avgDailyDistance, 2),
                MaxDailyDistanceKm = Math.Round(maxDailyDistance, 2),
                
                UtilizationRate = Math.Round((double)operatingDays / daysInPeriod * 100, 1),
                OperatingDays = operatingDays,
                IdleDays = daysInPeriod - operatingDays,
                TotalDrivingHours = Math.Round(positionCount / 60.0, 1),
                
                TotalFuelLiters = Math.Round(totalFuel, 2),
                AvgConsumptionPer100Km = 8.0,
                FuelEfficiencyKmPerLiter = 12.5,
                
                TotalCost = Math.Round(totalCost, 2),
                FuelCost = Math.Round(fuelCost, 2),
                MaintenanceCost = Math.Round(maintenanceCost, 2),
                CostPerKm = totalDistance > 0 ? Math.Round(totalCost / (decimal)totalDistance, 3) : 0,
                
                TotalTrips = movingCount / 10,
                AvgSpeedKph = Math.Round(avgSpeed, 1),
                MaxSpeedKph = Math.Round(maxSpeed, 1),
                SafetyIncidents = 0
            };

            vehicleStats.Add(stat);
            allDistances.Add(totalDistance);
            allFuel.Add(totalFuel);
            allCosts.Add(totalCost);
        }

        // Calculate rankings
        var distanceRanked = vehicleStats.OrderByDescending(v => v.TotalDistanceKm).ToList();
        var efficiencyRanked = vehicleStats.OrderByDescending(v => v.FuelEfficiencyKmPerLiter).ToList();
        var costRanked = vehicleStats.OrderBy(v => v.CostPerKm).ToList();

        for (int i = 0; i < vehicleStats.Count; i++)
        {
            vehicleStats[i].DistanceRank = distanceRanked.FindIndex(v => v.VehicleId == vehicleStats[i].VehicleId) + 1;
            vehicleStats[i].EfficiencyRank = efficiencyRanked.FindIndex(v => v.VehicleId == vehicleStats[i].VehicleId) + 1;
            vehicleStats[i].CostRank = costRanked.FindIndex(v => v.VehicleId == vehicleStats[i].VehicleId) + 1;
        }

        // Calculate variance from fleet average
        var avgDistance = allDistances.Any() ? allDistances.Average() : 0;
        var avgFuel = allFuel.Any() ? allFuel.Average() : 0;
        var avgCost = allCosts.Any() ? (double)allCosts.Average() : 0;

        foreach (var stat in vehicleStats)
        {
            stat.FuelVariancePercent = avgFuel > 0 
                ? Math.Round((stat.TotalFuelLiters - avgFuel) / avgFuel * 100, 1) 
                : 0;
            stat.CostVariancePercent = avgCost > 0 
                ? Math.Round(((double)stat.TotalCost - avgCost) / avgCost * 100, 1) 
                : 0;
        }

        // Pagination
        var pageSize = request.PageSize ?? 25;
        var pageNumber = request.PageNumber ?? 1;
        var totalRecords = vehicleStats.Count;
        var totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);

        var paginatedStats = vehicleStats
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        // Statistical analysis
        var analysis = CalculateStatisticalAnalysis(allDistances, allFuel, allCosts, vehicleStats);

        return new FleetStatisticsDto
        {
            GeneratedAt = DateTime.UtcNow,
            Period = $"{periodStart:MMMM yyyy}",
            GroupedBy = request.GroupBy ?? "vehicle",
            Summary = new FleetSummaryDto
            {
                TotalRecords = totalRecords,
                TotalDistanceKm = Math.Round(allDistances.Sum(), 2),
                TotalFuelLiters = Math.Round(allFuel.Sum(), 2),
                TotalCost = Math.Round(allCosts.Sum(), 2),
                TotalTrips = vehicleStats.Sum(v => v.TotalTrips),
                TotalHours = Math.Round(vehicleStats.Sum(v => v.TotalDrivingHours), 1),
                AvgUtilizationRate = vehicleStats.Any() 
                    ? Math.Round(vehicleStats.Average(v => v.UtilizationRate), 1) 
                    : 0,
                AvgEfficiency = 12.5
            },
            VehicleStats = paginatedStats,
            Pagination = new PaginationDto
            {
                CurrentPage = pageNumber,
                PageSize = pageSize,
                TotalPages = totalPages,
                TotalRecords = totalRecords,
                HasPrevious = pageNumber > 1,
                HasNext = pageNumber < totalPages
            },
            Analysis = analysis
        };
    }

    private StatisticalAnalysisDto CalculateStatisticalAnalysis(
        List<double> distances,
        List<double> fuel,
        List<decimal> costs,
        List<VehicleStatisticsDto> stats)
    {
        var analysis = new StatisticalAnalysisDto();

        if (!distances.Any()) return analysis;

        // Distance stats
        var sortedDistances = distances.OrderBy(d => d).ToList();
        analysis.DistanceMean = Math.Round(distances.Average(), 2);
        analysis.DistanceMedian = Math.Round(GetMedian(sortedDistances), 2);
        analysis.DistanceStdDev = Math.Round(CalculateStdDev(distances), 2);
        analysis.DistanceMin = Math.Round(distances.Min(), 2);
        analysis.DistanceMax = Math.Round(distances.Max(), 2);

        // Fuel stats
        var sortedFuel = fuel.OrderBy(f => f).ToList();
        analysis.FuelMean = Math.Round(fuel.Average(), 2);
        analysis.FuelMedian = Math.Round(GetMedian(sortedFuel), 2);
        analysis.FuelStdDev = Math.Round(CalculateStdDev(fuel), 2);

        // Cost stats
        var costsDouble = costs.Select(c => (double)c).ToList();
        var sortedCosts = costsDouble.OrderBy(c => c).ToList();
        analysis.CostMean = Math.Round((decimal)costsDouble.Average(), 2);
        analysis.CostMedian = Math.Round((decimal)GetMedian(sortedCosts), 2);
        analysis.CostStdDev = Math.Round((decimal)CalculateStdDev(costsDouble), 2);

        // Find outliers (> 2 standard deviations)
        var distanceThreshold = analysis.DistanceMean + 2 * analysis.DistanceStdDev;
        var fuelThreshold = analysis.FuelMean + 2 * analysis.FuelStdDev;
        var costThreshold = (double)analysis.CostMean + 2 * (double)analysis.CostStdDev;

        analysis.HighDistanceOutliers = stats
            .Where(s => s.TotalDistanceKm > distanceThreshold)
            .Select(s => s.VehicleId)
            .ToList();

        analysis.HighFuelOutliers = stats
            .Where(s => s.TotalFuelLiters > fuelThreshold)
            .Select(s => s.VehicleId)
            .ToList();

        analysis.HighCostOutliers = stats
            .Where(s => (double)s.TotalCost > costThreshold)
            .Select(s => s.VehicleId)
            .ToList();

        return analysis;
    }

    private double GetMedian(List<double> sorted)
    {
        if (!sorted.Any()) return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 
            ? (sorted[mid - 1] + sorted[mid]) / 2 
            : sorted[mid];
    }

    private double CalculateStdDev(List<double> values)
    {
        if (values.Count < 2) return 0;
        var avg = values.Average();
        var sumOfSquares = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumOfSquares / (values.Count - 1));
    }

    // Agrégat par (device, jour) projeté par EF Core (aucune position n'est matérialisée
    // côté API). Sert à la fois aux totaux par véhicule et aux distances journalières.
    private sealed class DeviceDayAgg
    {
        public int DeviceId { get; set; }
        public DateTime Day { get; set; }
        public int Count { get; set; }
        public double SpeedSum { get; set; }
        public int MovingCount { get; set; }
        public double MaxSpeed { get; set; }
        public double MovingSpeedSum { get; set; }
        public int MovingPoints { get; set; }
    }
}



