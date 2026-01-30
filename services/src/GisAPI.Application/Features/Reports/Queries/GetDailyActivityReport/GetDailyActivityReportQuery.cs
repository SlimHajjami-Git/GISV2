using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetDailyActivityReport;

public record GetDailyActivityReportQuery(
    int VehicleId,
    DateTime Date,
    int MinStopDurationSeconds = 120,
    double StopSpeedThresholdKph = 3.0
) : IQuery<DailyActivityReportDto>;

public record GetDailyActivityReportsQuery(
    DateTime Date,
    int[]? VehicleIds = null,
    int MinStopDurationSeconds = 120,
    double StopSpeedThresholdKph = 3.0
) : IQuery<List<DailyActivityReportDto>>;

public class DailyActivityReportDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public string? DriverName { get; set; }
    public DateTime ReportDate { get; set; }
    public bool HasActivity { get; set; }
    public DailyStartEventDto? FirstStart { get; set; }
    public DailyEndEventDto? LastPosition { get; set; }
    public List<ActivitySegmentDto> Activities { get; set; } = new();
    public DailySummaryDto Summary { get; set; } = new();
}

public class DailyStartEventDto
{
    public DateTime Timestamp { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
}

public class DailyEndEventDto
{
    public DateTime Timestamp { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public bool IgnitionOn { get; set; }
}

public class ActivitySegmentDto
{
    public string Type { get; set; } = string.Empty;
    public int SequenceNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationSeconds { get; set; }
    public string DurationFormatted { get; set; } = string.Empty;
    public LocationDto StartLocation { get; set; } = new();
    public LocationDto? EndLocation { get; set; }
    public double? DistanceKm { get; set; }
    public double? AvgSpeedKph { get; set; }
    public double? MaxSpeedKph { get; set; }
}

public class LocationDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
}

public class DailySummaryDto
{
    public int TotalActiveSeconds { get; set; }
    public int TotalDrivingSeconds { get; set; }
    public int TotalStoppedSeconds { get; set; }
    public string TotalActiveFormatted { get; set; } = string.Empty;
    public string TotalDrivingFormatted { get; set; } = string.Empty;
    public string TotalStoppedFormatted { get; set; } = string.Empty;
    public double TotalDistanceKm { get; set; }
    public int StopCount { get; set; }
    public int DriveCount { get; set; }
    public double MaxSpeedKph { get; set; }
    public double AvgSpeedKph { get; set; }
    public int PositionCount { get; set; }
}



