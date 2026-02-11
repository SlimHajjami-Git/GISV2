using MediatR;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Published when a vehicle enters or exits a geofence. Triggers user notification creation.
/// </summary>
public record GeofenceNotificationEvent(
    int CompanyId,
    int? VehicleId,
    string? VehicleName,
    int GeofenceId,
    string GeofenceName,
    string EventType, // "entry" or "exit"
    double Latitude,
    double Longitude,
    DateTime Timestamp
) : INotification;
