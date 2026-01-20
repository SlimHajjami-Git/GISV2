using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetMileageReport;

public record GetMileageReportQuery(
    int VehicleId,
    DateTime StartDate,
    DateTime EndDate
) : IQuery<MileageReportDto>;

public record GetMileageReportsQuery(
    DateTime StartDate,
    DateTime EndDate,
    int[]? VehicleIds = null
) : IQuery<List<MileageReportDto>>;

public class MileageReportDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public string? DriverName { get; set; }
    public string? VehicleType { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool HasData { get; set; }
    
    // Odometer readings
    public double? StartOdometerKm { get; set; }
    public double? EndOdometerKm { get; set; }
    public double? OdometerDifferenceKm { get; set; }
    
    // Calculated mileage from GPS
    public double TotalDistanceKm { get; set; }
    public double AverageDailyKm { get; set; }
    
    // Breakdowns
    public List<DailyMileageDto> DailyBreakdown { get; set; } = new();
    public List<WeeklyMileageDto> WeeklyBreakdown { get; set; } = new();
    public List<MonthlyMileageDto> MonthlyBreakdown { get; set; } = new();
    
    // Period comparison
    public PeriodComparisonDto? PreviousPeriodComparison { get; set; }
    
    // Summary statistics
    public MileageSummaryDto Summary { get; set; } = new();
}

public class DailyMileageDto
{
    public DateTime Date { get; set; }
    public string DayOfWeek { get; set; } = string.Empty;
    public double DistanceKm { get; set; }
    public double? StartOdometerKm { get; set; }
    public double? EndOdometerKm { get; set; }
    public int TripCount { get; set; }
    public int DrivingMinutes { get; set; }
    public double MaxSpeedKph { get; set; }
    public double AvgSpeedKph { get; set; }
}

public class WeeklyMileageDto
{
    public int WeekNumber { get; set; }
    public DateTime WeekStart { get; set; }
    public DateTime WeekEnd { get; set; }
    public double DistanceKm { get; set; }
    public double AverageDailyKm { get; set; }
    public int TripCount { get; set; }
    public int DrivingMinutes { get; set; }
}

public class MonthlyMileageDto
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = string.Empty;
    public double DistanceKm { get; set; }
    public double AverageDailyKm { get; set; }
    public int TripCount { get; set; }
    public int DaysWithActivity { get; set; }
}

public class PeriodComparisonDto
{
    public double PreviousPeriodDistanceKm { get; set; }
    public double CurrentPeriodDistanceKm { get; set; }
    public double DifferenceKm { get; set; }
    public double PercentageChange { get; set; }
    public string Trend { get; set; } = string.Empty; // "increase", "decrease", "stable"
}

public class MileageSummaryDto
{
    public double TotalDistanceKm { get; set; }
    public double AverageDailyKm { get; set; }
    public double MaxDailyKm { get; set; }
    public double MinDailyKm { get; set; }
    public DateTime? MaxDailyDate { get; set; }
    public DateTime? MinDailyDate { get; set; }
    public int TotalTripCount { get; set; }
    public int TotalDrivingMinutes { get; set; }
    public string TotalDrivingFormatted { get; set; } = string.Empty;
    public double MaxSpeedKph { get; set; }
    public double AvgSpeedKph { get; set; }
    public int DaysWithActivity { get; set; }
    public int TotalDays { get; set; }
    public double ActivityPercentage { get; set; }
}
