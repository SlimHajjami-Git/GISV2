using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reports.Queries.GetTripsReport;

public record GetTripsReportQuery(
    int VehicleId,
    DateTime StartDate,
    DateTime EndDate
) : IQuery<TripsReportDto>;

public record GetTripsReportAllVehiclesQuery(
    DateTime StartDate,
    DateTime EndDate,
    int[]? VehicleIds = null
) : IQuery<List<TripsReportDto>>;

// ==================== DTOs ====================

public class TripsReportDto
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public string? Plate { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<TripDayDto> Days { get; set; } = new();
    public TripsSummaryDto Summary { get; set; } = new();
}

public class TripDayDto
{
    public DateTime Date { get; set; }
    public List<TripEntryDto> Trips { get; set; } = new();
    public int TotalTripSeconds { get; set; }
    public string TotalTripFormatted { get; set; } = string.Empty;
    public double TotalDistanceKm { get; set; }
    public int TripCount { get; set; }
}

public class TripEntryDto
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationSeconds { get; set; }
    public string DurationFormatted { get; set; } = string.Empty;
    public double DistanceKm { get; set; }
    public double MaxSpeedKph { get; set; }
    public double AvgSpeedKph { get; set; }
    public double StartLatitude { get; set; }
    public double StartLongitude { get; set; }
    public string? StartAddress { get; set; }
    public double EndLatitude { get; set; }
    public double EndLongitude { get; set; }
    public string? EndAddress { get; set; }
}

public class TripsSummaryDto
{
    public int TotalTrips { get; set; }
    public int TotalTripSeconds { get; set; }
    public string TotalTripFormatted { get; set; } = string.Empty;
    public double TotalDistanceKm { get; set; }
    public double MaxSpeedKph { get; set; }
    public double AvgSpeedKph { get; set; }
    public double AvgDistanceKm { get; set; }
}

/// <summary>
/// Lightweight projection for trip detection — only needed columns
/// </summary>
public class TripPositionSlim
{
    public DateTime RecordedAt { get; set; }
    public bool? IgnitionOn { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? SpeedKph { get; set; }
}
