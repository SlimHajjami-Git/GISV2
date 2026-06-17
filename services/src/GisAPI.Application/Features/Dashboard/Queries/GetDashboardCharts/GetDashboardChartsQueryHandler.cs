using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Dashboard.Queries.GetDashboardCharts;

internal record VehicleDistanceData(int VehicleId, double Distance);

internal sealed class ChartDayAgg
{
    public int DeviceId { get; set; }
    public DateTime Day { get; set; }
    public double SpeedSum { get; set; }
}

public class GetDashboardChartsQueryHandler : IRequestHandler<GetDashboardChartsQuery, DashboardChartsDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    private static readonly string[] ChartColors = new[]
    {
        "#3B82F6", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6",
        "#EC4899", "#06B6D4", "#84CC16", "#F97316", "#6366F1"
    };

    public GetDashboardChartsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<DashboardChartsDto> Handle(GetDashboardChartsQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? 0;
        
        var now = DateTime.UtcNow;
        var year = request.Year ?? now.Year;
        var month = request.Month ?? now.Month;
        
        var periodStart = DateTime.SpecifyKind(new DateTime(year, month, 1), DateTimeKind.Utc);
        var periodEnd = DateTime.SpecifyKind(periodStart.AddMonths(1).AddSeconds(-1), DateTimeKind.Utc);

        // Get vehicles
        var vehiclesQuery = _context.Vehicles.Where(v => v.CompanyId == companyId);
        if (request.VehicleIds?.Any() == true)
            vehiclesQuery = vehiclesQuery.Where(v => request.VehicleIds.Contains(v.Id));

        var vehicles = await vehiclesQuery.ToListAsync(cancellationToken);
        var vehicleIds = vehicles.Select(v => v.Id).ToList();

        // Get device IDs (Vehicle has GpsDeviceId, build reverse map)
        var deviceVehicleMap = vehicles
            .Where(v => v.GpsDeviceId.HasValue)
            .ToDictionary(v => v.GpsDeviceId!.Value, v => v.Id);

        var deviceIds = deviceVehicleMap.Keys.ToList();

        // Agrégation POUSSÉE EN BASE : une ligne par (device, jour) au lieu de toutes les
        // positions du mois. Suffit pour la distance/véhicule et les tendances journalières.
        var perDeviceDay = new List<ChartDayAgg>();
        if (deviceIds.Count > 0)
        {
            perDeviceDay = await _context.GpsPositions.AsNoTracking()
                .Where(p => deviceIds.Contains(p.DeviceId) && p.RecordedAt >= periodStart && p.RecordedAt <= periodEnd)
                .GroupBy(p => new { p.DeviceId, Day = p.RecordedAt.Date })
                .Select(g => new ChartDayAgg
                {
                    DeviceId = g.Key.DeviceId,
                    Day = g.Key.Day,
                    SpeedSum = g.Sum(p => (double?)p.SpeedKph) ?? 0.0
                })
                .ToListAsync(cancellationToken);
        }

        // Distance par véhicule (somme des vitesses / 60) — formule inchangée.
        var distanceByVehicle = perDeviceDay
            .GroupBy(x => deviceVehicleMap.GetValueOrDefault(x.DeviceId))
            .Where(g => g.Key > 0)
            .Select(g => new VehicleDistanceData(g.Key, g.Sum(x => x.SpeedSum) / 60.0))
            .ToList();

        var totalDistance = distanceByVehicle.Sum(d => d.Distance);

        // Pré-agrégats journaliers (tous véhicules confondus) pour les tendances.
        var distanceByDay = perDeviceDay
            .GroupBy(x => x.Day)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.SpeedSum) / 60.0);
        var activeDevicesByDay = perDeviceDay
            .GroupBy(x => x.Day)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DeviceId).Distinct().Count());

        // Build charts
        var result = new DashboardChartsDto
        {
            GeneratedAt = DateTime.UtcNow,
            Period = $"{periodStart:MMMM yyyy}",
            DistanceByVehicle = BuildDistanceByVehicleChart(vehicles, distanceByVehicle),
            FuelDistribution = BuildFuelDistributionChart(vehicles, distanceByVehicle),
            MaintenanceTrend = BuildMaintenanceTrendChart(vehicles, periodStart),
            DailyDistanceTrend = BuildDailyDistanceTrend(distanceByDay, periodStart, periodEnd),
            UtilizationTrend = BuildUtilizationTrend(activeDevicesByDay, vehicles.Count, periodStart, periodEnd),
            CostBreakdown = BuildCostBreakdownChart(totalDistance),
            VehicleStatusChart = BuildVehicleStatusChart(vehicles),
            TopVehicles = BuildTopVehiclesChart(vehicles, distanceByVehicle)
        };

        return result;
    }

    private BarChartDataDto BuildDistanceByVehicleChart(
        List<Vehicle> vehicles,
        List<VehicleDistanceData> distanceByVehicle)
    {
        var items = vehicles.Select((v, i) => new BarChartItemDto
        {
            Label = v.Name,
            Value = Math.Round(distanceByVehicle
                .FirstOrDefault(d => d.VehicleId == v.Id)?.Distance ?? 0, 1),
            Color = ChartColors[i % ChartColors.Length],
            Id = v.Id
        }).OrderByDescending(x => x.Value).ToList();

        return new BarChartDataDto
        {
            Title = "Kilomètres par véhicule",
            XAxisLabel = "Véhicule",
            YAxisLabel = "Distance (km)",
            Unit = "km",
            Data = items
        };
    }

    private PieChartDataDto BuildFuelDistributionChart(
        List<Vehicle> vehicles,
        List<VehicleDistanceData> distanceByVehicle)
    {
        // Estimate fuel consumption (8L/100km average)
        var fuelConsumption = vehicles.Select((v, i) =>
        {
            var distData = distanceByVehicle.FirstOrDefault(d => d.VehicleId == v.Id);
            var distance = distData?.Distance ?? 0.0;
            return new { Vehicle = v, Fuel = distance * 0.08, Index = i };
        }).ToList();

        var totalFuel = fuelConsumption.Sum(f => f.Fuel);

        var slices = fuelConsumption.Select(f => new PieChartSliceDto
        {
            Label = f.Vehicle.Name,
            Value = Math.Round((double)f.Fuel, 1),
            Percentage = totalFuel > 0 ? Math.Round((double)(f.Fuel / totalFuel * 100), 1) : 0,
            Color = ChartColors[f.Index % ChartColors.Length],
            Id = f.Vehicle.Id
        }).Where(s => s.Value > 0).ToList();

        return new PieChartDataDto
        {
            Title = "Répartition consommation carburant",
            Unit = "L",
            Total = Math.Round(totalFuel, 1),
            Slices = slices
        };
    }

    private AreaChartDataDto BuildMaintenanceTrendChart(
        List<Vehicle> vehicles,
        DateTime periodStart)
    {
        var weekLabels = new List<string> { "Semaine 1", "Semaine 2", "Semaine 3", "Semaine 4" };
        
        // Simulated maintenance costs per vehicle per week
        var series = vehicles.Take(5).Select((v, i) => new AreaChartSeriesDto
        {
            Name = v.Name,
            Color = ChartColors[i % ChartColors.Length],
            BackgroundColor = $"{ChartColors[i % ChartColors.Length]}33",
            Values = new List<double> { 
                Math.Round(Random.Shared.NextDouble() * 200, 2),
                Math.Round(Random.Shared.NextDouble() * 200, 2),
                Math.Round(Random.Shared.NextDouble() * 200, 2),
                Math.Round(Random.Shared.NextDouble() * 200, 2)
            },
            VehicleId = v.Id
        }).ToList();

        return new AreaChartDataDto
        {
            Title = "Coûts de maintenance par période",
            XAxisLabel = "Période",
            YAxisLabel = "Coût (TND)",
            Unit = "TND",
            Labels = weekLabels,
            Series = series
        };
    }

    private LineChartDataDto BuildDailyDistanceTrend(
        Dictionary<DateTime, double> distanceByDay,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var daysInMonth = (periodEnd - periodStart).Days + 1;
        var labels = Enumerable.Range(0, Math.Min(daysInMonth, 31))
            .Select(d => periodStart.AddDays(d).ToString("dd/MM"))
            .ToList();

        var dailyDistance = Enumerable.Range(0, Math.Min(daysInMonth, 31))
            .Select(d => Math.Round(distanceByDay.GetValueOrDefault(periodStart.AddDays(d).Date), 1))
            .ToList();

        return new LineChartDataDto
        {
            Title = "Tendance distance journalière",
            XAxisLabel = "Date",
            YAxisLabel = "Distance (km)",
            Labels = labels,
            Series = new List<LineChartSeriesDto>
            {
                new()
                {
                    Name = "Distance (km)",
                    Color = "#3B82F6",
                    Values = dailyDistance,
                    Fill = true
                }
            }
        };
    }

    private LineChartDataDto BuildUtilizationTrend(
        Dictionary<DateTime, int> activeDevicesByDay,
        int totalVehicles,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var daysInMonth = (periodEnd - periodStart).Days + 1;
        var labels = Enumerable.Range(0, Math.Min(daysInMonth, 31))
            .Select(d => periodStart.AddDays(d).ToString("dd/MM"))
            .ToList();

        var dailyUtilization = Enumerable.Range(0, Math.Min(daysInMonth, 31))
            .Select(d =>
            {
                var activeDevices = activeDevicesByDay.GetValueOrDefault(periodStart.AddDays(d).Date);
                return totalVehicles > 0 ? (double)activeDevices / totalVehicles * 100 : 0;
            })
            .Select(v => Math.Round(v, 1))
            .ToList();

        return new LineChartDataDto
        {
            Title = "Tendance utilisation",
            XAxisLabel = "Date",
            YAxisLabel = "Utilisation (%)",
            Labels = labels,
            Series = new List<LineChartSeriesDto>
            {
                new()
                {
                    Name = "Utilisation (%)",
                    Color = "#10B981",
                    Values = dailyUtilization,
                    Fill = false
                }
            }
        };
    }

    private PieChartDataDto BuildCostBreakdownChart(double totalDistance)
    {
        var fuelCost = totalDistance * 0.15; // TND per km
        var maintenanceCost = totalDistance * 0.05;
        var insuranceCost = totalDistance * 0.02;
        var otherCost = totalDistance * 0.01;
        var totalCost = fuelCost + maintenanceCost + insuranceCost + otherCost;

        return new PieChartDataDto
        {
            Title = "Répartition des coûts",
            Unit = "TND",
            Total = Math.Round(totalCost, 2),
            Slices = new List<PieChartSliceDto>
            {
                new() { Label = "Carburant", Value = Math.Round(fuelCost, 2), Percentage = totalCost > 0 ? Math.Round(fuelCost / totalCost * 100, 1) : 0, Color = "#F59E0B" },
                new() { Label = "Maintenance", Value = Math.Round(maintenanceCost, 2), Percentage = totalCost > 0 ? Math.Round(maintenanceCost / totalCost * 100, 1) : 0, Color = "#EF4444" },
                new() { Label = "Assurance", Value = Math.Round(insuranceCost, 2), Percentage = totalCost > 0 ? Math.Round(insuranceCost / totalCost * 100, 1) : 0, Color = "#8B5CF6" },
                new() { Label = "Autres", Value = Math.Round(otherCost, 2), Percentage = totalCost > 0 ? Math.Round(otherCost / totalCost * 100, 1) : 0, Color = "#6B7280" }
            }
        };
    }

    private BarChartDataDto BuildVehicleStatusChart(List<Vehicle> vehicles)
    {
        var statusGroups = vehicles
            .GroupBy(v => v.Status ?? "unknown")
            .Select((g, i) => new BarChartItemDto
            {
                Label = g.Key switch
                {
                    "active" => "Actif",
                    "inactive" => "Inactif",
                    "maintenance" => "Maintenance",
                    _ => g.Key
                },
                Value = g.Count(),
                Color = g.Key switch
                {
                    "active" => "#10B981",
                    "inactive" => "#6B7280",
                    "maintenance" => "#F59E0B",
                    _ => "#3B82F6"
                }
            })
            .ToList();

        return new BarChartDataDto
        {
            Title = "Statut des véhicules",
            XAxisLabel = "Statut",
            YAxisLabel = "Nombre",
            Unit = "véhicules",
            Data = statusGroups
        };
    }

    private BarChartDataDto BuildTopVehiclesChart(
        List<Vehicle> vehicles,
        List<VehicleDistanceData> distanceByVehicle)
    {
        var topVehicles = vehicles
            .Select((v, i) => new
            {
                Vehicle = v,
                Distance = distanceByVehicle.FirstOrDefault(d => d.VehicleId == v.Id)?.Distance ?? 0.0,
                Index = i
            })
            .OrderByDescending(x => x.Distance)
            .Take(5)
            .Select((x, rank) => new BarChartItemDto
            {
                Label = x.Vehicle.Name,
                Value = Math.Round(x.Distance, 1),
                Color = ChartColors[rank % ChartColors.Length],
                Id = x.Vehicle.Id
            })
            .ToList();

        return new BarChartDataDto
        {
            Title = "Top 5 véhicules par distance",
            XAxisLabel = "Véhicule",
            YAxisLabel = "Distance (km)",
            Unit = "km",
            Data = topVehicles
        };
    }
}



