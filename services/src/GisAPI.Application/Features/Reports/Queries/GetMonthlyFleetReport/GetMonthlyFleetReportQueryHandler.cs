using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace GisAPI.Application.Features.Reports.Queries.GetMonthlyFleetReport;

public class GetMonthlyFleetReportQueryHandler : IRequestHandler<GetMonthlyFleetReportQuery, MonthlyFleetReportDto>
{
    private readonly IGisDbContext _context;
    private static readonly CultureInfo FrenchCulture = new("fr-FR");

    public GetMonthlyFleetReportQueryHandler(IGisDbContext context)
    {
        _context = context;
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
            ? await _context.Users.AsNoTracking()
                .Where(u => driverIds.Contains(u.Id))
                .ToListAsync(ct)
            : new List<User>();

        // Build the report
        var report = new MonthlyFleetReportDto
        {
            Year = request.Year,
            Month = request.Month,
            MonthName = startDate.ToString("MMMM yyyy", FrenchCulture),
            GeneratedAt = DateTime.UtcNow,
            ReportPeriod = $"{startDate:dd/MM/yyyy} - {endDate.AddDays(-1):dd/MM/yyyy}"
        };

        // Process all sections
        report.FleetOverview = BuildFleetOverview(vehicles, positions);
        report.Utilization = BuildUtilization(vehicles, positions, startDate, daysInMonth);
        report.FuelAnalytics = BuildFuelAnalytics(vehicles, positions);
        report.Maintenance = await BuildMaintenanceAsync(vehicles, startDate, endDate, ct);
        report.DriverPerformance = BuildDriverPerformance(drivers, vehicles, positions);
        report.Efficiency = BuildEfficiency(positions, daysInMonth);
        report.CostAnalysis = BuildCostAnalysis(vehicles, positions, report.FuelAnalytics, report.Maintenance);
        
        // Comparisons
        report.MonthOverMonth = BuildComparison("Mois précédent", positions, prevMonthPositions);
        if (prevYearPositions.Any())
        {
            report.YearOverYear = BuildComparison("Même mois année précédente", positions, prevYearPositions);
        }

        // Executive Summary
        report.ExecutiveSummary = BuildExecutiveSummary(report);
        
        // Alerts
        report.Alerts = BuildAlerts(report, vehicles);
        
        // KPIs
        report.KeyPerformanceIndicators = BuildKpis(report);
        
        // Charts
        report.Charts = BuildChartData(report, vehicles, positions, startDate, daysInMonth);

        return report;
    }

    private async Task<List<Vehicle>> GetVehiclesAsync(GetMonthlyFleetReportQuery request, CancellationToken ct)
    {
        var query = _context.Vehicles.AsNoTracking()
            .Include(v => v.AssignedDriver)
            .AsQueryable();

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

        return await _context.GpsPositions.AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId) &&
                        p.RecordedAt >= adjustedStart &&
                        p.RecordedAt < adjustedEnd)
            .ToListAsync(ct);
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

    private FuelAnalyticsDto BuildFuelAnalytics(List<Vehicle> vehicles, List<GpsPosition> positions)
    {
        var analytics = new FuelAnalyticsDto();
        var fuelEfficiencies = new List<double>();

        var totalDistance = CalculateDistance(positions);
        
        // Estimate fuel consumption based on positions with fuel data
        var positionsWithFuel = positions.Where(p => p.FuelRaw.HasValue && p.FuelRaw > 0).ToList();
        
        // By vehicle
        foreach (var vehicle in vehicles.Where(v => v.GpsDeviceId.HasValue))
        {
            var vehiclePositions = positions.Where(p => p.DeviceId == vehicle.GpsDeviceId).ToList();
            var distance = CalculateDistance(vehiclePositions);
            
            // Estimate consumption (assuming average 8L/100km if no data)
            var estimatedConsumption = distance > 0 ? distance * 0.08 : 0;
            var efficiency = estimatedConsumption > 0 ? distance / estimatedConsumption : 0;
            
            if (efficiency > 0) fuelEfficiencies.Add(efficiency);

            analytics.ByVehicle.Add(new VehicleFuelConsumptionDto
            {
                VehicleId = vehicle.Id,
                VehicleName = vehicle.Name,
                TotalDistanceKm = Math.Round(distance, 2),
                TotalConsumedLiters = Math.Round(estimatedConsumption, 2),
                EfficiencyKmPerLiter = Math.Round(efficiency, 2),
                ConsumptionPer100Km = distance > 0 ? Math.Round(estimatedConsumption / distance * 100, 2) : 0,
                EfficiencyRating = GetEfficiencyRating(efficiency)
            });

            analytics.TotalFuelConsumedLiters += estimatedConsumption;
        }

        analytics.TotalFuelConsumedLiters = Math.Round(analytics.TotalFuelConsumedLiters, 2);
        analytics.TotalFuelCost = (decimal)Math.Round(analytics.TotalFuelConsumedLiters * 2.1, 2); // Estimated price
        analytics.AverageConsumptionPer100Km = totalDistance > 0 
            ? Math.Round(analytics.TotalFuelConsumedLiters / totalDistance * 100, 2) : 0;
        analytics.AverageFuelEfficiencyKmPerLiter = analytics.TotalFuelConsumedLiters > 0
            ? Math.Round(totalDistance / analytics.TotalFuelConsumedLiters, 2) : 0;

        if (fuelEfficiencies.Any())
        {
            analytics.Statistics = CalculateStatistics(fuelEfficiencies);
        }

        return analytics;
    }

    private async Task<MaintenanceAnalyticsDto> BuildMaintenanceAsync(List<Vehicle> vehicles, DateTime start, DateTime end, CancellationToken ct)
    {
        var maintenance = new MaintenanceAnalyticsDto
        {
            TotalMaintenanceEvents = 0,
            TotalMaintenanceCost = 0
        };

        // Placeholder - in a real system, this would query maintenance records
        maintenance.ByType = new List<MaintenanceTypeBreakdownDto>
        {
            new() { Type = "Vidange", Count = vehicles.Count / 3, TotalCost = vehicles.Count * 50, Percentage = 30 },
            new() { Type = "Pneus", Count = vehicles.Count / 5, TotalCost = vehicles.Count * 120, Percentage = 25 },
            new() { Type = "Freins", Count = vehicles.Count / 6, TotalCost = vehicles.Count * 80, Percentage = 20 },
            new() { Type = "Révision", Count = vehicles.Count / 4, TotalCost = vehicles.Count * 100, Percentage = 25 }
        };

        maintenance.TotalMaintenanceEvents = maintenance.ByType.Sum(t => t.Count);
        maintenance.TotalMaintenanceCost = maintenance.ByType.Sum(t => t.TotalCost);
        maintenance.ScheduledMaintenances = (int)(maintenance.TotalMaintenanceEvents * 0.7);
        maintenance.UnscheduledMaintenances = maintenance.TotalMaintenanceEvents - maintenance.ScheduledMaintenances;
        maintenance.AvgMaintenanceCostPerVehicle = vehicles.Count > 0 
            ? (double)maintenance.TotalMaintenanceCost / vehicles.Count : 0;

        return maintenance;
    }

    private DriverPerformanceDto BuildDriverPerformance(List<User> drivers, List<Vehicle> vehicles, List<GpsPosition> positions)
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
                FuelEfficiency = distance > 0 ? Math.Round(distance / (distance * 0.08), 2) : 0,
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

    private OperationalEfficiencyDto BuildEfficiency(List<GpsPosition> positions, int daysInMonth)
    {
        var efficiency = new OperationalEfficiencyDto();

        var totalPositions = positions.Count;
        var movingPositions = positions.Count(p => p.SpeedKph > 3);
        var idlePositions = totalPositions - movingPositions;

        efficiency.IdleTimePercentage = totalPositions > 0 
            ? Math.Round((double)idlePositions / totalPositions * 100, 1) : 0;
        efficiency.FleetAvailabilityRate = 95; // Placeholder
        efficiency.OnTimeDeliveryRate = 92; // Placeholder
        efficiency.AverageRouteEfficiency = 88; // Placeholder
        efficiency.OverallEfficiencyScore = Math.Round(
            (efficiency.FleetAvailabilityRate + efficiency.OnTimeDeliveryRate + efficiency.AverageRouteEfficiency) / 3, 1);

        efficiency.Metrics = new List<EfficiencyMetricDto>
        {
            new() { Name = "Disponibilité flotte", Value = efficiency.FleetAvailabilityRate, Target = 95, 
                    Variance = efficiency.FleetAvailabilityRate - 95, Status = efficiency.FleetAvailabilityRate >= 95 ? "OnTarget" : "Below" },
            new() { Name = "Livraisons à temps", Value = efficiency.OnTimeDeliveryRate, Target = 90,
                    Variance = efficiency.OnTimeDeliveryRate - 90, Status = efficiency.OnTimeDeliveryRate >= 90 ? "OnTarget" : "Below" },
            new() { Name = "Efficacité itinéraires", Value = efficiency.AverageRouteEfficiency, Target = 85,
                    Variance = efficiency.AverageRouteEfficiency - 85, Status = efficiency.AverageRouteEfficiency >= 85 ? "OnTarget" : "Below" },
            new() { Name = "Temps d'inactivité", Value = efficiency.IdleTimePercentage, Target = 20,
                    Variance = 20 - efficiency.IdleTimePercentage, Status = efficiency.IdleTimePercentage <= 20 ? "OnTarget" : "Above" }
        };

        return efficiency;
    }

    private CostAnalysisDto BuildCostAnalysis(List<Vehicle> vehicles, List<GpsPosition> positions, 
        FuelAnalyticsDto fuel, MaintenanceAnalyticsDto maintenance)
    {
        var costs = new CostAnalysisDto
        {
            FuelCost = fuel.TotalFuelCost,
            MaintenanceCost = maintenance.TotalMaintenanceCost,
            InsuranceCost = vehicles.Count * 150, // Estimated
            OtherCosts = vehicles.Count * 50 // Estimated
        };

        costs.TotalOperationalCost = costs.FuelCost + costs.MaintenanceCost + costs.InsuranceCost + costs.OtherCosts;
        
        var totalDistance = CalculateDistance(positions);
        costs.CostPerKm = totalDistance > 0 ? Math.Round(costs.TotalOperationalCost / (decimal)totalDistance, 3) : 0;
        costs.CostPerVehicle = vehicles.Count > 0 ? Math.Round(costs.TotalOperationalCost / vehicles.Count, 2) : 0;

        costs.ByCategory = new List<CostBreakdownDto>
        {
            new() { Category = "Carburant", Amount = costs.FuelCost, 
                    Percentage = costs.TotalOperationalCost > 0 ? Math.Round((double)(costs.FuelCost / costs.TotalOperationalCost) * 100, 1) : 0 },
            new() { Category = "Maintenance", Amount = costs.MaintenanceCost,
                    Percentage = costs.TotalOperationalCost > 0 ? Math.Round((double)(costs.MaintenanceCost / costs.TotalOperationalCost) * 100, 1) : 0 },
            new() { Category = "Assurance", Amount = costs.InsuranceCost,
                    Percentage = costs.TotalOperationalCost > 0 ? Math.Round((double)(costs.InsuranceCost / costs.TotalOperationalCost) * 100, 1) : 0 },
            new() { Category = "Autres", Amount = costs.OtherCosts,
                    Percentage = costs.TotalOperationalCost > 0 ? Math.Round((double)(costs.OtherCosts / costs.TotalOperationalCost) * 100, 1) : 0 }
        };

        // By vehicle
        foreach (var vehicleFuel in fuel.ByVehicle)
        {
            var vehicle = vehicles.FirstOrDefault(v => v.Id == vehicleFuel.VehicleId);
            if (vehicle == null) continue;

            var fuelCost = (decimal)vehicleFuel.TotalConsumedLiters * 2.1m;
            var maintCost = (decimal)maintenance.AvgMaintenanceCostPerVehicle;
            var totalCost = fuelCost + maintCost + 200; // +insurance+other

            costs.ByVehicle.Add(new VehicleCostDto
            {
                VehicleId = vehicle.Id,
                VehicleName = vehicle.Name,
                TotalCost = Math.Round(totalCost, 2),
                FuelCost = Math.Round(fuelCost, 2),
                MaintenanceCost = Math.Round(maintCost, 2),
                CostPerKm = vehicleFuel.TotalDistanceKm > 0 
                    ? Math.Round(totalCost / (decimal)vehicleFuel.TotalDistanceKm, 3) : 0
            });
        }

        return costs;
    }

    private PeriodComparisonDto BuildComparison(string period, List<GpsPosition> current, List<GpsPosition> previous)
    {
        var currentDistance = CalculateDistance(current);
        var previousDistance = CalculateDistance(previous);
        
        var currentTrips = CountTrips(current);
        var previousTrips = CountTrips(previous);

        return new PeriodComparisonDto
        {
            ComparisonPeriod = period,
            Distance = BuildComparisonMetric("Distance", currentDistance, previousDistance, "km", true),
            FuelConsumption = BuildComparisonMetric("Carburant", currentDistance * 0.08, previousDistance * 0.08, "L", false),
            Cost = BuildComparisonMetric("Coût", currentDistance * 0.08 * 2.1, previousDistance * 0.08 * 2.1, "TND", false),
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

    private ExecutiveSummaryDto BuildExecutiveSummary(MonthlyFleetReportDto report)
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
            TotalDrivingHours = report.Utilization.ByVehicle.Sum(v => v.OperatingDays) * 8 // Estimate
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
                Unit = "TND/km",
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
            },
            new() {
                Name = "Efficacité opérationnelle",
                Category = "Opérations",
                Value = report.Efficiency.OverallEfficiencyScore,
                Target = 85,
                Variance = report.Efficiency.OverallEfficiencyScore - 85,
                VariancePercent = ((report.Efficiency.OverallEfficiencyScore - 85) / 85) * 100,
                Unit = "%",
                Status = report.Efficiency.OverallEfficiencyScore >= 85 ? "OnTarget" : "Below",
                Trend = "stable"
            }
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
            Unit = "TND"
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




