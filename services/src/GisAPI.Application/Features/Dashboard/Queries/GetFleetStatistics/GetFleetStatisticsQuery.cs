using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Dashboard.Queries.GetFleetStatistics;

/// <summary>
/// Query to retrieve detailed fleet statistics with filters
/// </summary>
public record GetFleetStatisticsQuery(
    int? Year = null,
    int? Month = null,
    string? GroupBy = null, // vehicle, driver, type, department
    int[]? VehicleIds = null,
    int? PageNumber = null,
    int? PageSize = null
) : IQuery<FleetStatisticsDto>;

// ==================== Statistics DTOs ====================

public class FleetStatisticsDto
{
    public DateTime GeneratedAt { get; set; }
    public string Period { get; set; } = string.Empty;
    public string GroupedBy { get; set; } = string.Empty;
    
    // Summary
    public FleetSummaryDto Summary { get; set; } = new();
    
    // Detailed statistics by group
    public List<VehicleStatisticsDto> VehicleStats { get; set; } = new();
    
    // Pagination info
    public PaginationDto Pagination { get; set; } = new();
    
    // Statistical analysis
    public StatisticalAnalysisDto Analysis { get; set; } = new();
}

public class FleetSummaryDto
{
    public int TotalRecords { get; set; }
    public double TotalDistanceKm { get; set; }
    public double TotalFuelLiters { get; set; }
    public decimal TotalCost { get; set; }
    public int TotalTrips { get; set; }
    public double TotalHours { get; set; }
    public double AvgUtilizationRate { get; set; }
    public double AvgEfficiency { get; set; }
}

public class VehicleStatisticsDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public string? VehicleType { get; set; }
    public string? Department { get; set; }
    public string? DriverName { get; set; }
    
    // Distance metrics
    public double TotalDistanceKm { get; set; }
    public double AvgDailyDistanceKm { get; set; }
    public double MaxDailyDistanceKm { get; set; }
    
    // Utilization metrics
    public double UtilizationRate { get; set; }
    public int OperatingDays { get; set; }
    public int IdleDays { get; set; }
    public double TotalDrivingHours { get; set; }
    
    // Fuel metrics
    public double TotalFuelLiters { get; set; }
    public double AvgConsumptionPer100Km { get; set; }
    public double FuelEfficiencyKmPerLiter { get; set; }
    public double FuelVariancePercent { get; set; }
    
    // Cost metrics
    public decimal TotalCost { get; set; }
    public decimal FuelCost { get; set; }
    public decimal MaintenanceCost { get; set; }
    public decimal CostPerKm { get; set; }
    public double CostVariancePercent { get; set; }
    
    // Performance metrics
    public int TotalTrips { get; set; }
    public double AvgSpeedKph { get; set; }
    public double MaxSpeedKph { get; set; }
    public int SafetyIncidents { get; set; }
    
    // Ranking
    public int DistanceRank { get; set; }
    public int EfficiencyRank { get; set; }
    public int CostRank { get; set; }
}

public class PaginationDto
{
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public int TotalRecords { get; set; }
    public bool HasPrevious { get; set; }
    public bool HasNext { get; set; }
}

public class StatisticalAnalysisDto
{
    // Distance analysis
    public double DistanceMean { get; set; }
    public double DistanceMedian { get; set; }
    public double DistanceStdDev { get; set; }
    public double DistanceMin { get; set; }
    public double DistanceMax { get; set; }
    
    // Fuel analysis
    public double FuelMean { get; set; }
    public double FuelMedian { get; set; }
    public double FuelStdDev { get; set; }
    
    // Cost analysis
    public decimal CostMean { get; set; }
    public decimal CostMedian { get; set; }
    public decimal CostStdDev { get; set; }
    
    // Outliers
    public List<int> HighDistanceOutliers { get; set; } = new();
    public List<int> HighFuelOutliers { get; set; } = new();
    public List<int> HighCostOutliers { get; set; } = new();
}
