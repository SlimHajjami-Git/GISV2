using MediatR;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Published when a vehicle exceeds the in-zone speed limit configured on a
/// geofence (Geofence.AlertSpeedLimit). Distinct from SpeedAlertNotificationEvent
/// because the alert message references the specific zone and uses the zone's
/// limit — not the vehicle-wide speed limit.
/// </summary>
public record GeofenceSpeedAlertNotificationEvent(
    int CompanyId,
    int? VehicleId,
    string? VehicleName,
    string? Plate,
    int GeofenceId,
    string GeofenceName,
    int GeofenceSpeedLimitKph,
    double SpeedKph,
    double Latitude,
    double Longitude,
    DateTime Timestamp
) : INotification;
