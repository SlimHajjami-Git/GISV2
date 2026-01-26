using System.Text.Json.Serialization;
using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetMileagePeriodReport;

/// <summary>
/// Query for getting mileage breakdown by period (Hour, Day, or Month)
/// </summary>
public record GetMileagePeriodReportQuery(
    int VehicleId,
    DateTime StartDate,
    DateTime EndDate,
    MileagePeriodType PeriodType
) : IQuery<MileagePeriodReportDto>;

/// <summary>
/// Period type for mileage breakdown
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MileagePeriodType
{
    [JsonPropertyName("hour")]
    Hour,   // 24-hour breakdown for a specific date
    [JsonPropertyName("day")]
    Day,    // Daily breakdown between two dates
    [JsonPropertyName("month")]
    Month   // Monthly breakdown for date range
}

/// <summary>
/// Main response DTO for mileage period report
/// </summary>
public class MileagePeriodReportDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public string? DriverName { get; set; }
    public string? VehicleType { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public MileagePeriodType PeriodType { get; set; }
    public bool HasData { get; set; }
    
    // Summary
    public double TotalDistanceKm { get; set; }
    public double AverageDistanceKm { get; set; }
    public double MaxDistanceKm { get; set; }
    public double MinDistanceKm { get; set; }
    public int TotalTripCount { get; set; }
    public int TotalDrivingMinutes { get; set; }
    public string TotalDrivingFormatted { get; set; } = string.Empty;
    
    // Period breakdown data
    public List<HourlyMileageDto> HourlyBreakdown { get; set; } = new();
    public List<DailyMileagePeriodDto> DailyBreakdown { get; set; } = new();
    public List<MonthlyMileagePeriodDto> MonthlyBreakdown { get; set; } = new();
    
    // Chart data (generic for all period types)
    public List<ChartDataPoint> ChartData { get; set; } = new();
}

/// <summary>
/// Hourly mileage breakdown (for Hour period type)
/// </summary>
public class HourlyMileageDto
{
    public int Hour { get; set; }
    public string HourLabel { get; set; } = string.Empty; // "00:00", "01:00", etc.
    public double DistanceKm { get; set; }
    public int TripCount { get; set; }
    public int DrivingMinutes { get; set; }
    public double MaxSpeedKph { get; set; }
    public double AvgSpeedKph { get; set; }
}

/// <summary>
/// Daily mileage breakdown (for Day period type)
/// </summary>
public class DailyMileagePeriodDto
{
    public DateTime Date { get; set; }
    public string DateLabel { get; set; } = string.Empty;
    public string DayOfWeek { get; set; } = string.Empty;
    public double DistanceKm { get; set; }
    public int TripCount { get; set; }
    public int DrivingMinutes { get; set; }
    public double MaxSpeedKph { get; set; }
    public double AvgSpeedKph { get; set; }
}

/// <summary>
/// Monthly mileage breakdown (for Month period type)
/// </summary>
public class MonthlyMileagePeriodDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthLabel { get; set; } = string.Empty; // "Janvier 2026"
    public double DistanceKm { get; set; }
    public double AverageDailyKm { get; set; }
    public int TripCount { get; set; }
    public int DrivingMinutes { get; set; }
    public int DaysWithActivity { get; set; }
    public int TotalDays { get; set; }
}

/// <summary>
/// Generic chart data point for visualization
/// </summary>
public class ChartDataPoint
{
    public string Label { get; set; } = string.Empty;
    public double Value { get; set; }
    public string? Tooltip { get; set; }
}
