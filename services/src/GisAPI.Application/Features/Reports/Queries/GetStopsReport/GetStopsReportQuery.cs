using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetStopsReport;

/// <summary>
/// Query for a single vehicle stops report
/// </summary>
public record GetStopsReportQuery(
    int VehicleId,
    DateTime StartDate,
    DateTime EndDate,
    int MinStopDurationSeconds = 60
) : IQuery<StopsReportDto>;

/// <summary>
/// Query for all vehicles stops report
/// </summary>
public record GetStopsReportAllVehiclesQuery(
    DateTime StartDate,
    DateTime EndDate,
    int[]? VehicleIds = null,
    int MinStopDurationSeconds = 60
) : IQuery<List<StopsReportDto>>;

// ==================== DTOs ====================

public class StopsReportDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<StopDayDto> Days { get; set; } = new();
    public StopsSummaryDto Summary { get; set; } = new();
}

public class StopDayDto
{
    public DateTime Date { get; set; }
    public List<StopEntryDto> Stops { get; set; } = new();
    public int TotalStopSeconds { get; set; }
    public string TotalStopFormatted { get; set; } = string.Empty;
    public int StopCount { get; set; }
}

public class StopEntryDto
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationSeconds { get; set; }
    public string DurationFormatted { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
}

public class StopsSummaryDto
{
    public int TotalStops { get; set; }
    public int TotalStopSeconds { get; set; }
    public string TotalStopFormatted { get; set; } = string.Empty;
    public int AvgStopSeconds { get; set; }
    public string AvgStopFormatted { get; set; } = string.Empty;
    public int MaxStopSeconds { get; set; }
    public string MaxStopFormatted { get; set; } = string.Empty;
    public int MinStopSeconds { get; set; }
    public string MinStopFormatted { get; set; } = string.Empty;
}
