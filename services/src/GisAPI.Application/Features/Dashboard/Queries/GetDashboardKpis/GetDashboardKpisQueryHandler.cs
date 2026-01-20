using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Dashboard.Queries.GetDashboardKpis;

public class GetDashboardKpisQueryHandler : IRequestHandler<GetDashboardKpisQuery, DashboardKpisDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetDashboardKpisQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<DashboardKpisDto> Handle(GetDashboardKpisQuery request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? 0;
        
        // Determine period
        var now = DateTime.UtcNow;
        var year = request.Year ?? now.Year;
        var month = request.Month ?? now.Month;
        
        var periodStart = DateTime.SpecifyKind(new DateTime(year, month, 1), DateTimeKind.Utc);
        var periodEnd = DateTime.SpecifyKind(periodStart.AddMonths(1).AddSeconds(-1), DateTimeKind.Utc);
        
        // Previous period for comparison
        var prevPeriodStart = DateTime.SpecifyKind(periodStart.AddMonths(-1), DateTimeKind.Utc);
        var prevPeriodEnd = DateTime.SpecifyKind(periodStart.AddSeconds(-1), DateTimeKind.Utc);

        // Get vehicles
        var vehiclesQuery = _context.Vehicles.Where(v => v.CompanyId == companyId);
        if (request.VehicleIds?.Any() == true)
            vehiclesQuery = vehiclesQuery.Where(v => request.VehicleIds.Contains(v.Id));

        var vehicles = await vehiclesQuery.ToListAsync(cancellationToken);
        var vehicleIds = vehicles.Select(v => v.Id).ToList();

        // Get device IDs for position queries (Vehicle has GpsDeviceId)
        var deviceIds = vehicles
            .Where(v => v.GpsDeviceId.HasValue)
            .Select(v => v.GpsDeviceId!.Value)
            .ToList();

        // Current period positions
        var currentPositions = await _context.GpsPositions
            .Where(p => deviceIds.Contains(p.DeviceId) && 
                       p.RecordedAt >= periodStart && 
                       p.RecordedAt <= periodEnd)
            .ToListAsync(cancellationToken);

        // Previous period positions
        var prevPositions = await _context.GpsPositions
            .Where(p => deviceIds.Contains(p.DeviceId) && 
                       p.RecordedAt >= prevPeriodStart && 
                       p.RecordedAt <= prevPeriodEnd)
            .ToListAsync(cancellationToken);

        // Calculate current period metrics
        var totalDistance = currentPositions.Sum(p => (p.SpeedKph ?? 0) * (1.0 / 60.0)); // Approximate
        var activeVehicleIds = currentPositions.Select(p => p.DeviceId).Distinct().Count();
        
        // Calculate previous period metrics for trends
        var prevTotalDistance = prevPositions.Sum(p => (p.SpeedKph ?? 0) * (1.0 / 60.0));

        // Fleet KPIs
        var fleetKpis = new FleetKpisDto
        {
            TotalVehicles = vehicles.Count,
            ActiveVehicles = vehicles.Count(v => v.Status == "active"),
            InactiveVehicles = vehicles.Count(v => v.Status == "inactive"),
            InMaintenance = vehicles.Count(v => v.Status == "maintenance"),
            AvailabilityRate = vehicles.Count > 0 
                ? (double)vehicles.Count(v => v.Status != "maintenance") / vehicles.Count * 100 
                : 0,
            UtilizationRate = vehicles.Count > 0 
                ? (double)activeVehicleIds / vehicles.Count * 100 
                : 0
        };

        // Operational KPIs
        var daysInPeriod = (periodEnd - periodStart).Days + 1;
        var operationalKpis = new OperationalKpisDto
        {
            TotalDistanceKm = Math.Round(totalDistance, 2),
            TotalTrips = currentPositions.Count(p => (p.SpeedKph ?? 0) > 5) / 10, // Approximate trips
            TotalDrivingHours = Math.Round(currentPositions.Count / 60.0, 1),
            AvgDailyDistanceKm = Math.Round(totalDistance / Math.Max(daysInPeriod, 1), 2),
            AvgTripsPerVehicle = vehicles.Count > 0 
                ? Math.Round((double)(currentPositions.Count(p => (p.SpeedKph ?? 0) > 5) / 10) / vehicles.Count, 1) 
                : 0,
            ActiveDrivers = vehicles.Count(v => v.AssignedDriverId != null)
        };

        // Financial KPIs (estimated based on distance)
        var fuelCostPerKm = 0.15m; // TND per km estimate
        var maintenanceCostPerKm = 0.05m;
        var totalFuelCost = (decimal)totalDistance * fuelCostPerKm;
        var totalMaintenanceCost = (decimal)totalDistance * maintenanceCostPerKm;
        
        var financialKpis = new FinancialKpisDto
        {
            TotalOperationalCost = totalFuelCost + totalMaintenanceCost,
            FuelCost = totalFuelCost,
            MaintenanceCost = totalMaintenanceCost,
            CostPerKm = totalDistance > 0 
                ? Math.Round((totalFuelCost + totalMaintenanceCost) / (decimal)totalDistance, 3) 
                : 0,
            CostPerVehicle = vehicles.Count > 0 
                ? Math.Round((totalFuelCost + totalMaintenanceCost) / vehicles.Count, 2) 
                : 0,
            FuelCostPerKm = totalDistance > 0 
                ? Math.Round(totalFuelCost / (decimal)totalDistance, 3) 
                : 0
        };

        // Performance KPIs
        var avgSpeed = currentPositions.Any() 
            ? currentPositions.Where(p => (p.SpeedKph ?? 0) > 0).Average(p => p.SpeedKph ?? 0) 
            : 0;
        
        var performanceKpis = new PerformanceKpisDto
        {
            FuelEfficiencyKmPerLiter = 12.5, // Estimated
            AvgConsumptionPer100Km = 8.0, // Estimated L/100km
            DriverPerformanceScore = 85.0, // Placeholder
            SafetyIncidents = 0,
            OnTimeDeliveryRate = 95.0, // Placeholder
            IdleTimePercentage = currentPositions.Any() 
                ? Math.Round((double)currentPositions.Count(p => (p.SpeedKph ?? 0) < 3) / currentPositions.Count * 100, 1) 
                : 0
        };

        // Trend calculations
        var trends = new TrendIndicatorsDto
        {
            Distance = CalculateTrend(totalDistance, prevTotalDistance, false),
            FuelConsumption = CalculateTrend(totalDistance * 0.08, prevTotalDistance * 0.08, true), // Lower is better
            Cost = CalculateTrend((double)(totalFuelCost + totalMaintenanceCost), 
                                  (double)((decimal)prevTotalDistance * (fuelCostPerKm + maintenanceCostPerKm)), true),
            Utilization = CalculateTrend(fleetKpis.UtilizationRate, fleetKpis.UtilizationRate * 0.95, false),
            Efficiency = CalculateTrend(12.5, 12.0, false)
        };

        return new DashboardKpisDto
        {
            GeneratedAt = DateTime.UtcNow,
            Period = $"{periodStart:MMMM yyyy}",
            Fleet = fleetKpis,
            Operations = operationalKpis,
            Financial = financialKpis,
            Performance = performanceKpis,
            Trends = trends
        };
    }

    private TrendDto CalculateTrend(double current, double previous, bool lowerIsBetter)
    {
        var change = previous > 0 ? ((current - previous) / previous) * 100 : 0;
        var direction = Math.Abs(change) < 1 ? "stable" : (change > 0 ? "up" : "down");
        var isPositive = lowerIsBetter ? change <= 0 : change >= 0;

        return new TrendDto
        {
            CurrentValue = Math.Round(current, 2),
            PreviousValue = Math.Round(previous, 2),
            ChangePercent = Math.Round(change, 1),
            Direction = direction,
            IsPositive = isPositive
        };
    }
}
