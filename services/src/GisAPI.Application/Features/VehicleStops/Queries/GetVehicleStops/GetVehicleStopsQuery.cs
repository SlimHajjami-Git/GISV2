using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.VehicleStops.Queries.GetVehicleStops;

public record GetVehicleStopsQuery(
    int? VehicleId = null,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    string? StopType = null,
    int Page = 1,
    int PageSize = 50
) : IQuery<VehicleStopsResultDto>;

public record VehicleStopsResultDto(
    List<VehicleStopDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public record VehicleStopDto(
    long Id,
    int VehicleId,
    string? VehicleName,
    string? VehiclePlate,
    int? DriverId,
    string? DriverName,
    DateTime StartTime,
    DateTime? EndTime,
    int DurationSeconds,
    double Latitude,
    double Longitude,
    string? Address,
    string StopType,
    bool IgnitionOff,
    bool IsAuthorized,
    int? FuelLevelStart,
    int? FuelLevelEnd,
    int? FuelConsumed,
    bool InsideGeofence,
    string? GeofenceName,
    string? Notes
);
