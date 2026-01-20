using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Dashboard.Queries.GetDashboardCharts;

/// <summary>
/// Query to retrieve chart-ready data for dashboard visualizations
/// </summary>
public record GetDashboardChartsQuery(
    int? Year = null,
    int? Month = null,
    string[]? ChartTypes = null, // distance, fuel, maintenance, utilization, cost
    int[]? VehicleIds = null
) : IQuery<DashboardChartsDto>;

// ==================== Chart DTOs ====================

public class DashboardChartsDto
{
    public DateTime GeneratedAt { get; set; }
    public string Period { get; set; } = string.Empty;
    
    // Bar Chart: Distance per vehicle
    public BarChartDataDto DistanceByVehicle { get; set; } = new();
    
    // Pie Chart: Fuel consumption distribution
    public PieChartDataDto FuelDistribution { get; set; } = new();
    
    // Area Chart: Maintenance costs over time
    public AreaChartDataDto MaintenanceTrend { get; set; } = new();
    
    // Line Chart: Daily distance trend
    public LineChartDataDto DailyDistanceTrend { get; set; } = new();
    
    // Line Chart: Utilization trend
    public LineChartDataDto UtilizationTrend { get; set; } = new();
    
    // Pie Chart: Cost breakdown
    public PieChartDataDto CostBreakdown { get; set; } = new();
    
    // Bar Chart: Vehicle status distribution
    public BarChartDataDto VehicleStatusChart { get; set; } = new();
    
    // Bar Chart: Top performing vehicles
    public BarChartDataDto TopVehicles { get; set; } = new();
}

public class BarChartDataDto
{
    public string Title { get; set; } = string.Empty;
    public string XAxisLabel { get; set; } = string.Empty;
    public string YAxisLabel { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public List<BarChartItemDto> Data { get; set; } = new();
}

public class BarChartItemDto
{
    public string Label { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Color { get; set; } = string.Empty;
    public int? Id { get; set; }
}

public class PieChartDataDto
{
    public string Title { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public double Total { get; set; }
    public List<PieChartSliceDto> Slices { get; set; } = new();
}

public class PieChartSliceDto
{
    public string Label { get; set; } = string.Empty;
    public double Value { get; set; }
    public double Percentage { get; set; }
    public string Color { get; set; } = string.Empty;
    public int? Id { get; set; }
}

public class LineChartDataDto
{
    public string Title { get; set; } = string.Empty;
    public string XAxisLabel { get; set; } = string.Empty;
    public string YAxisLabel { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = new();
    public List<LineChartSeriesDto> Series { get; set; } = new();
}

public class LineChartSeriesDto
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public List<double> Values { get; set; } = new();
    public bool Fill { get; set; } = false;
}

public class AreaChartDataDto
{
    public string Title { get; set; } = string.Empty;
    public string XAxisLabel { get; set; } = string.Empty;
    public string YAxisLabel { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public List<string> Labels { get; set; } = new();
    public List<AreaChartSeriesDto> Series { get; set; } = new();
}

public class AreaChartSeriesDto
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string BackgroundColor { get; set; } = string.Empty;
    public List<double> Values { get; set; } = new();
    public int? VehicleId { get; set; }
}
