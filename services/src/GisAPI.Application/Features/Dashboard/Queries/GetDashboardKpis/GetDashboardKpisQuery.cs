using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Dashboard.Queries.GetDashboardKpis;

/// <summary>
/// Query to retrieve lightweight KPI data for quick dashboard loading
/// </summary>
public record GetDashboardKpisQuery(
    int? Year = null,
    int? Month = null,
    int[]? VehicleIds = null
) : IQuery<DashboardKpisDto>;

// ==================== KPI DTOs ====================

public class DashboardKpisDto
{
    public DateTime GeneratedAt { get; set; }
    public string Period { get; set; } = string.Empty;
    
    // Fleet KPIs
    public FleetKpisDto Fleet { get; set; } = new();
    
    // Operational KPIs
    public OperationalKpisDto Operations { get; set; } = new();
    
    // Financial KPIs
    public FinancialKpisDto Financial { get; set; } = new();
    
    // Performance KPIs
    public PerformanceKpisDto Performance { get; set; } = new();
    
    // Trends (compared to previous period)
    public TrendIndicatorsDto Trends { get; set; } = new();
}

public class FleetKpisDto
{
    public int TotalVehicles { get; set; }
    public int ActiveVehicles { get; set; }
    public int InactiveVehicles { get; set; }
    public int InMaintenance { get; set; }
    public double AvailabilityRate { get; set; }
    public double UtilizationRate { get; set; }
}

public class OperationalKpisDto
{
    public double TotalDistanceKm { get; set; }
    public int TotalTrips { get; set; }
    public double TotalDrivingHours { get; set; }
    public double AvgDailyDistanceKm { get; set; }
    public double AvgTripsPerVehicle { get; set; }
    public int ActiveDrivers { get; set; }
}

public class FinancialKpisDto
{
    public decimal TotalOperationalCost { get; set; }
    public decimal FuelCost { get; set; }
    public decimal MaintenanceCost { get; set; }
    public decimal CostPerKm { get; set; }
    public decimal CostPerVehicle { get; set; }
    public decimal FuelCostPerKm { get; set; }
}

public class PerformanceKpisDto
{
    public double FuelEfficiencyKmPerLiter { get; set; }
    public double AvgConsumptionPer100Km { get; set; }
    public double DriverPerformanceScore { get; set; }
    public int SafetyIncidents { get; set; }
    public double OnTimeDeliveryRate { get; set; }
    public double IdleTimePercentage { get; set; }
}

public class TrendIndicatorsDto
{
    public TrendDto Distance { get; set; } = new();
    public TrendDto FuelConsumption { get; set; } = new();
    public TrendDto Cost { get; set; } = new();
    public TrendDto Utilization { get; set; } = new();
    public TrendDto Efficiency { get; set; } = new();
}

public class TrendDto
{
    public double CurrentValue { get; set; }
    public double PreviousValue { get; set; }
    public double ChangePercent { get; set; }
    public string Direction { get; set; } = "stable"; // up, down, stable
    public bool IsPositive { get; set; }
}



