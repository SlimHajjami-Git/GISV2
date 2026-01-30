using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetMonthlyFleetReport;

public record GetMonthlyFleetReportQuery(
    int Year,
    int Month,
    int? VehicleTypeFilter = null,
    int? DepartmentFilter = null,
    int[]? VehicleIds = null
) : IQuery<MonthlyFleetReportDto>;

// ==================== MAIN REPORT DTO ====================

public class MonthlyFleetReportDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public string ReportPeriod { get; set; } = string.Empty;
    
    // Executive Summary
    public ExecutiveSummaryDto ExecutiveSummary { get; set; } = new();
    
    // Fleet Overview
    public FleetOverviewDto FleetOverview { get; set; } = new();
    
    // Vehicle Utilization
    public VehicleUtilizationDto Utilization { get; set; } = new();
    
    // Fuel Analytics
    public FuelAnalyticsDto FuelAnalytics { get; set; } = new();
    
    // Maintenance Analytics
    public MaintenanceAnalyticsDto Maintenance { get; set; } = new();
    
    // Driver Performance
    public DriverPerformanceDto DriverPerformance { get; set; } = new();
    
    // Operational Efficiency
    public OperationalEfficiencyDto Efficiency { get; set; } = new();
    
    // Cost Analysis
    public CostAnalysisDto CostAnalysis { get; set; } = new();
    
    // Comparisons
    public PeriodComparisonDto MonthOverMonth { get; set; } = new();
    public PeriodComparisonDto? YearOverYear { get; set; }
    
    // Alerts & Anomalies
    public List<AlertDto> Alerts { get; set; } = new();
    
    // KPIs
    public List<KpiDto> KeyPerformanceIndicators { get; set; } = new();
    
    // Chart Data
    public ChartDataCollectionDto Charts { get; set; } = new();
}

// ==================== EXECUTIVE SUMMARY ====================

public class ExecutiveSummaryDto
{
    public int TotalVehicles { get; set; }
    public int ActiveVehicles { get; set; }
    public double TotalDistanceKm { get; set; }
    public double TotalFuelConsumedLiters { get; set; }
    public decimal TotalOperationalCost { get; set; }
    public double FleetUtilizationRate { get; set; }
    public double AverageFuelEfficiency { get; set; }
    public int TotalTrips { get; set; }
    public int TotalDrivingHours { get; set; }
    public List<string> KeyInsights { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

// ==================== FLEET OVERVIEW ====================

public class FleetOverviewDto
{
    public int TotalVehicles { get; set; }
    public int ActiveVehicles { get; set; }
    public int InactiveVehicles { get; set; }
    public int InMaintenanceVehicles { get; set; }
    public List<VehicleTypeSummaryDto> ByType { get; set; } = new();
    public List<VehicleStatusSummaryDto> ByStatus { get; set; } = new();
    public List<DepartmentSummaryDto> ByDepartment { get; set; } = new();
}

public class VehicleTypeSummaryDto
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
    public double TotalDistanceKm { get; set; }
    public double AvgDistanceKm { get; set; }
}

public class VehicleStatusSummaryDto
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Percentage { get; set; }
}

public class DepartmentSummaryDto
{
    public string Department { get; set; } = string.Empty;
    public int VehicleCount { get; set; }
    public double TotalDistanceKm { get; set; }
    public decimal TotalCost { get; set; }
}

// ==================== VEHICLE UTILIZATION ====================

public class VehicleUtilizationDto
{
    public double OverallUtilizationRate { get; set; }
    public double AverageDailyUsageHours { get; set; }
    public double AverageDailyDistanceKm { get; set; }
    public int TotalOperatingDays { get; set; }
    public int TotalIdleDays { get; set; }
    public List<DailyUtilizationDto> DailyTrend { get; set; } = new();
    public List<VehicleUtilizationDetailDto> ByVehicle { get; set; } = new();
    public StatisticalMetricsDto Statistics { get; set; } = new();
}

public class DailyUtilizationDto
{
    public DateTime Date { get; set; }
    public double UtilizationRate { get; set; }
    public int ActiveVehicles { get; set; }
    public double TotalDistanceKm { get; set; }
    public int TotalTrips { get; set; }
}

public class VehicleUtilizationDetailDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public double UtilizationRate { get; set; }
    public double TotalDistanceKm { get; set; }
    public int TotalTrips { get; set; }
    public int OperatingDays { get; set; }
    public double AvgDailyKm { get; set; }
}

// ==================== FUEL ANALYTICS ====================

public class FuelAnalyticsDto
{
    public double TotalFuelConsumedLiters { get; set; }
    public decimal TotalFuelCost { get; set; }
    public double AverageConsumptionPer100Km { get; set; }
    public double AverageFuelEfficiencyKmPerLiter { get; set; }
    public List<DailyFuelConsumptionDto> DailyTrend { get; set; } = new();
    public List<VehicleFuelConsumptionDto> ByVehicle { get; set; } = new();
    public List<FuelEventDto> RefuelEvents { get; set; } = new();
    public List<FuelAnomalyDto> Anomalies { get; set; } = new();
    public StatisticalMetricsDto Statistics { get; set; } = new();
}

public class DailyFuelConsumptionDto
{
    public DateTime Date { get; set; }
    public double ConsumptionLiters { get; set; }
    public double DistanceKm { get; set; }
    public double EfficiencyKmPerLiter { get; set; }
}

public class VehicleFuelConsumptionDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public double TotalConsumedLiters { get; set; }
    public double TotalDistanceKm { get; set; }
    public double EfficiencyKmPerLiter { get; set; }
    public double ConsumptionPer100Km { get; set; }
    public string EfficiencyRating { get; set; } = string.Empty; // Excellent, Good, Average, Poor
}

public class FuelEventDto
{
    public DateTime Timestamp { get; set; }
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public double AmountLiters { get; set; }
    public decimal? Cost { get; set; }
    public string Location { get; set; } = string.Empty;
}

public class FuelAnomalyDto
{
    public DateTime DetectedAt { get; set; }
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string AnomalyType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty; // Low, Medium, High, Critical
}

// ==================== MAINTENANCE ANALYTICS ====================

public class MaintenanceAnalyticsDto
{
    public int TotalMaintenanceEvents { get; set; }
    public decimal TotalMaintenanceCost { get; set; }
    public int ScheduledMaintenances { get; set; }
    public int UnscheduledMaintenances { get; set; }
    public double AvgMaintenanceCostPerVehicle { get; set; }
    public List<MaintenanceTypeBreakdownDto> ByType { get; set; } = new();
    public List<VehicleMaintenanceDto> ByVehicle { get; set; } = new();
    public List<MaintenanceEventDto> RecentEvents { get; set; } = new();
    public List<UpcomingMaintenanceDto> Upcoming { get; set; } = new();
}

public class MaintenanceTypeBreakdownDto
{
    public string Type { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal TotalCost { get; set; }
    public double Percentage { get; set; }
}

public class VehicleMaintenanceDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public int MaintenanceCount { get; set; }
    public decimal TotalCost { get; set; }
    public DateTime? LastMaintenanceDate { get; set; }
}

public class MaintenanceEventDto
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Cost { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class UpcomingMaintenanceDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string MaintenanceType { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public int DaysUntilDue { get; set; }
}

// ==================== DRIVER PERFORMANCE ====================

public class DriverPerformanceDto
{
    public int TotalDrivers { get; set; }
    public int ActiveDrivers { get; set; }
    public double AveragePerformanceScore { get; set; }
    public List<DriverMetricsDto> DriverMetrics { get; set; } = new();
    public List<DriverRankingDto> TopPerformers { get; set; } = new();
    public List<DriverRankingDto> NeedsImprovement { get; set; } = new();
    public List<DrivingEventSummaryDto> EventsSummary { get; set; } = new();
    public StatisticalMetricsDto Statistics { get; set; } = new();
}

public class DriverMetricsDto
{
    public int DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public double TotalDistanceKm { get; set; }
    public int TotalTrips { get; set; }
    public double AvgSpeedKph { get; set; }
    public int HarshBrakingEvents { get; set; }
    public int HarshAccelerationEvents { get; set; }
    public int SpeedingEvents { get; set; }
    public double FuelEfficiency { get; set; }
    public double PerformanceScore { get; set; }
    public string Rating { get; set; } = string.Empty;
}

public class DriverRankingDto
{
    public int Rank { get; set; }
    public int DriverId { get; set; }
    public string DriverName { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Trend { get; set; } = string.Empty; // up, down, stable
}

public class DrivingEventSummaryDto
{
    public string EventType { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int UniqueDrivers { get; set; }
    public double AvgPerDriver { get; set; }
}

// ==================== OPERATIONAL EFFICIENCY ====================

public class OperationalEfficiencyDto
{
    public double OverallEfficiencyScore { get; set; }
    public double FleetAvailabilityRate { get; set; }
    public double OnTimeDeliveryRate { get; set; }
    public double IdleTimePercentage { get; set; }
    public double AverageRouteEfficiency { get; set; }
    public List<DailyEfficiencyDto> DailyTrend { get; set; } = new();
    public List<EfficiencyMetricDto> Metrics { get; set; } = new();
}

public class DailyEfficiencyDto
{
    public DateTime Date { get; set; }
    public double EfficiencyScore { get; set; }
    public double AvailabilityRate { get; set; }
    public double IdleTimePercent { get; set; }
}

public class EfficiencyMetricDto
{
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Target { get; set; }
    public double Variance { get; set; }
    public string Status { get; set; } = string.Empty; // OnTarget, Above, Below
}

// ==================== COST ANALYSIS ====================

public class CostAnalysisDto
{
    public decimal TotalOperationalCost { get; set; }
    public decimal FuelCost { get; set; }
    public decimal MaintenanceCost { get; set; }
    public decimal InsuranceCost { get; set; }
    public decimal OtherCosts { get; set; }
    public decimal CostPerKm { get; set; }
    public decimal CostPerVehicle { get; set; }
    public List<CostBreakdownDto> ByCategory { get; set; } = new();
    public List<DailyCostDto> DailyTrend { get; set; } = new();
    public List<VehicleCostDto> ByVehicle { get; set; } = new();
}

public class CostBreakdownDto
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percentage { get; set; }
}

public class DailyCostDto
{
    public DateTime Date { get; set; }
    public decimal TotalCost { get; set; }
    public decimal FuelCost { get; set; }
    public decimal MaintenanceCost { get; set; }
}

public class VehicleCostDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
    public decimal FuelCost { get; set; }
    public decimal MaintenanceCost { get; set; }
    public decimal CostPerKm { get; set; }
}

// ==================== COMPARISONS ====================

public class PeriodComparisonDto
{
    public string ComparisonPeriod { get; set; } = string.Empty;
    public ComparisonMetricDto Distance { get; set; } = new();
    public ComparisonMetricDto FuelConsumption { get; set; } = new();
    public ComparisonMetricDto Cost { get; set; } = new();
    public ComparisonMetricDto Utilization { get; set; } = new();
    public ComparisonMetricDto Efficiency { get; set; } = new();
    public ComparisonMetricDto Trips { get; set; } = new();
}

public class ComparisonMetricDto
{
    public string MetricName { get; set; } = string.Empty;
    public double CurrentValue { get; set; }
    public double PreviousValue { get; set; }
    public double Change { get; set; }
    public double ChangePercent { get; set; }
    public string Trend { get; set; } = string.Empty; // increase, decrease, stable
    public bool IsPositiveTrend { get; set; }
}

// ==================== ALERTS & KPIS ====================

public class AlertDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty; // Info, Warning, Critical
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public int? VehicleId { get; set; }
    public string? VehicleName { get; set; }
    public string RecommendedAction { get; set; } = string.Empty;
}

public class KpiDto
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Target { get; set; }
    public double Variance { get; set; }
    public double VariancePercent { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // OnTarget, Above, Below
    public string Trend { get; set; } = string.Empty;
}

// ==================== STATISTICAL METRICS ====================

public class StatisticalMetricsDto
{
    public double Mean { get; set; }
    public double Median { get; set; }
    public double StandardDeviation { get; set; }
    public double Variance { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Range { get; set; }
    public double Percentile25 { get; set; }
    public double Percentile75 { get; set; }
    public double InterquartileRange { get; set; }
}

// ==================== CHART DATA ====================

public class ChartDataCollectionDto
{
    // Column Charts
    public ChartDataDto UtilizationByVehicleType { get; set; } = new();
    public ChartDataDto MaintenanceCostByType { get; set; } = new();
    public ChartDataDto DistanceByDepartment { get; set; } = new();
    
    // Line Charts
    public MultiSeriesChartDataDto FuelConsumptionTrend { get; set; } = new();
    public MultiSeriesChartDataDto DriverPerformanceTrend { get; set; } = new();
    public MultiSeriesChartDataDto EfficiencyTrend { get; set; } = new();
    public MultiSeriesChartDataDto DailyDistanceTrend { get; set; } = new();
    
    // Pie Charts
    public ChartDataDto FleetComposition { get; set; } = new();
    public ChartDataDto CostDistribution { get; set; } = new();
    public ChartDataDto MaintenanceTypeBreakdown { get; set; } = new();
    public ChartDataDto VehicleStatusDistribution { get; set; } = new();
    
    // Bar Charts
    public ChartDataDto DepartmentComparison { get; set; } = new();
    public ChartDataDto VehiclePerformanceRanking { get; set; } = new();
    public ChartDataDto DriverRanking { get; set; } = new();
}

public class ChartDataDto
{
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // column, line, pie, bar
    public List<string> Labels { get; set; } = new();
    public List<double> Values { get; set; } = new();
    public string? Unit { get; set; }
    public List<string>? Colors { get; set; }
}

public class MultiSeriesChartDataDto
{
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = new();
    public List<ChartSeriesDto> Series { get; set; } = new();
    public string? XAxisLabel { get; set; }
    public string? YAxisLabel { get; set; }
}

public class ChartSeriesDto
{
    public string Name { get; set; } = string.Empty;
    public List<double> Data { get; set; } = new();
    public string? Color { get; set; }
}



