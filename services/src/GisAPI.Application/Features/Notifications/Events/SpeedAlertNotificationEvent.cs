using MediatR;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Published when a vehicle exceeds speed limit. Triggers user notification creation.
/// </summary>
public record SpeedAlertNotificationEvent(
    int CompanyId,
    int? VehicleId,
    string? VehicleName,
    string? Plate,
    double SpeedKph,
    double Latitude,
    double Longitude,
    DateTime Timestamp
) : INotification;
