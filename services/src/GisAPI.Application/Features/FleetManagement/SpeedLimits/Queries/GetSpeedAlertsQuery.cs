using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FleetManagement.SpeedLimits.Queries;

public record GetSpeedAlertsQuery(
    int? VehicleId = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    bool? IsAcknowledged = null,
    int Page = 1,
    int PageSize = 50
) : IQuery<SpeedAlertsResult>;

public record SpeedAlertsResult(
    List<SpeedAlertDto> Alerts,
    int TotalCount,
    int Page,
    int PageSize
);

public record SpeedAlertDto(
    int Id,
    int VehicleId,
    string VehicleName,
    string? VehiclePlate,
    int SpeedLimit,
    int ActualSpeed,
    int ExcessSpeed,
    double Latitude,
    double Longitude,
    string? Address,
    DateTime RecordedAt,
    bool IsAcknowledged,
    DateTime? AcknowledgedAt,
    string? AcknowledgedByName
);



