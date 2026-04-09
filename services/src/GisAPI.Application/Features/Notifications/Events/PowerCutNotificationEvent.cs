using MediatR;

namespace GisAPI.Application.Features.Notifications.Events;

public record PowerCutNotificationEvent(
    int CompanyId,
    int? VehicleId,
    string? VehicleName,
    int OfflineDurationSecs,
    double? LastKnownLat,
    double? LastKnownLon,
    string EventType,
    DateTime EventAt
) : INotification;
