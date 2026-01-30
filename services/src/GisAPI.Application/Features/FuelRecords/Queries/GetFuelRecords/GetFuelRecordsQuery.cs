using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FuelRecords.Queries.GetFuelRecords;

public record GetFuelRecordsQuery(
    int? VehicleId = null,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    string? EventType = null,
    bool? AnomaliesOnly = null,
    int Page = 1,
    int PageSize = 50
) : IQuery<FuelRecordsResultDto>;

public record FuelRecordsResultDto(
    List<FuelRecordDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    FuelSummaryDto? Summary
);

public record FuelRecordDto(
    long Id,
    int VehicleId,
    string? VehicleName,
    string? VehiclePlate,
    int? DriverId,
    string? DriverName,
    DateTime RecordedAt,
    short FuelPercent,
    decimal? FuelLiters,
    short? FuelChange,
    string EventType,
    long? OdometerKm,
    double? SpeedKph,
    short? Rpm,
    bool? IgnitionOn,
    double Latitude,
    double Longitude,
    bool IsAnomaly,
    string? AnomalyReason,
    decimal? RefuelAmount,
    decimal? RefuelCost,
    string? RefuelStation
);

public record FuelSummaryDto(
    int TotalRecords,
    int RefuelCount,
    int AnomalyCount,
    decimal? TotalRefuelLiters,
    decimal? TotalRefuelCost,
    double? AverageConsumptionLPer100Km
);



