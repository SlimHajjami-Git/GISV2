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

    // ─── Parc SANS GPS (recette du 11/09/2026) ───

    /// <summary>
    /// Au moins un véhicule du périmètre a un boîtier GPS. Sans boîtier,
    /// l'utilisation, les trajets, les heures de conduite et le score des
    /// conducteurs ne sont pas MESURÉS — ce n'est pas qu'ils valent 0 : l'écran
    /// les masque au lieu d'afficher « taux d'utilisation 0 % ».
    /// </summary>
    public bool FleetHasGps { get; set; }
    public int VehiclesWithGps { get; set; }

    /// <summary>
    /// Une ligne par véhicule du périmètre, AVEC OU SANS boîtier : kilométrage,
    /// carburant et coûts du mois. Le tableau par véhicule reposait sur
    /// <see cref="VehicleUtilizationDto.ByVehicle"/>, qui ne liste que les
    /// véhicules équipés : il était vide pour un compte GPA.
    /// </summary>
    public List<MonthlyVehicleRowDto> Vehicles { get; set; } = new();

    /// <summary>Totaux de <see cref="Vehicles"/> (ligne TOTAL du tableau).</summary>
    public MonthlyFleetTotalsDto Totals { get; set; } = new();
}

/// <summary>Une ligne du tableau par véhicule du rapport mensuel flotte.</summary>
public class MonthlyVehicleRowDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public bool HasGps { get; set; }

    /// <summary>Kilométrage du mois ; null quand il n'est pas mesurable.</summary>
    public double? DistanceKm { get; set; }

    /// <summary>
    /// "gps" : boîtier (trajets terminés, à défaut positions) ; "odometer" :
    /// reconstitué des relevés compteur saisis (pleins, entretiens, réparations,
    /// dépenses) ; "none" : non mesurable ce mois.
    /// </summary>
    public string DistanceSource { get; set; } = "none";
    public bool ReliableDistance { get; set; }

    /// <summary>Litres ACHETÉS (pleins + dépenses carburant), jamais estimés.</summary>
    public double Liters { get; set; }
    /// <summary>null sans kilométrage ou sans litres : 0 L/100 km serait faux.</summary>
    public double? ConsumptionPer100Km { get; set; }

    public decimal FuelCost { get; set; }
    public decimal MaintenanceCost { get; set; }
    public decimal RepairCost { get; set; }
    public decimal OtherCost { get; set; }
    public decimal TotalCost { get; set; }
    /// <summary>null sans kilométrage.</summary>
    public decimal? CostPerKm { get; set; }

    /// <summary>null sans boîtier : non mesuré.</summary>
    public double? UtilizationRate { get; set; }
    /// <summary>null sans boîtier : non mesuré.</summary>
    public int? Trips { get; set; }
}

/// <summary>Totaux du tableau par véhicule.</summary>
public class MonthlyFleetTotalsDto
{
    public double DistanceKm { get; set; }
    /// <summary>Véhicules dont le kilométrage du mois est connu.</summary>
    public int MeasuredVehicles { get; set; }
    public double Liters { get; set; }
    /// <summary>
    /// Litres des véhicules au kilométrage connu, rapportés à ce kilométrage.
    /// Les litres d'un véhicule sans kilométrage n'entrent pas au numérateur :
    /// ils gonflaient la moyenne sans rien ajouter au dénominateur.
    /// </summary>
    public double? ConsumptionPer100Km { get; set; }
    public decimal FuelCost { get; set; }
    public decimal MaintenanceCost { get; set; }
    public decimal RepairCost { get; set; }
    public decimal OtherCost { get; set; }
    public decimal TotalCost { get; set; }
    /// <summary>Coût des véhicules MESURÉS rapporté à leur kilométrage (celui d'un
    /// véhicule sans kilométrage n'y entre pas) ; null sans kilométrage.</summary>
    public decimal? CostPerKm { get; set; }
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

    /// <summary>
    /// Somme des durées des trajets TERMINÉS de la période (table <c>trips</c>),
    /// arrondie à l'heure. C'était « jours actifs × 8 » : un véhicule vu cinq
    /// minutes un jour comptait huit heures de conduite.
    /// </summary>
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

    /// <summary>Dépenses carburant RÉELLES de la période (pleins saisis + dépenses de type carburant).</summary>
    public decimal TotalFuelCost { get; set; }

    /// <summary>
    /// Vrai dès qu'au moins un véhicule entre dans le total avec des litres
    /// ESTIMÉS (capteur FMS, sonde, ou taux par type de véhicule) faute de
    /// plein saisi. Le coût, lui, ne provient jamais d'une estimation.
    /// </summary>
    public bool IsEstimated { get; set; }

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

    /// <summary>Litres estimés (aucun plein saisi sur la période) plutôt que mesurés.</summary>
    public bool IsEstimated { get; set; }
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

/// <summary>
/// Efficacité opérationnelle. N'expose plus que ce qui est MESURÉ : la part des
/// trames à l'arrêt. « Disponibilité flotte », « Livraisons à temps » et
/// « Efficacité itinéraires » étaient trois constantes (95 / 92 / 88) sans
/// aucune source en base, et leur moyenne — le « score d'efficacité » — valait
/// donc toujours 91,7 quel que soit le mois ou la société.
/// </summary>
public class OperationalEfficiencyDto
{
    public double IdleTimePercentage { get; set; }
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



