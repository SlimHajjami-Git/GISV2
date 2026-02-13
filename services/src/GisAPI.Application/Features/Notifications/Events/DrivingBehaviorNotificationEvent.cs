using MediatR;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Published when dangerous driving behavior is detected (harsh braking, rapid acceleration, sharp turns).
/// Triggers notification to company admins.
/// </summary>
public record DrivingBehaviorNotificationEvent(
    int CompanyId,
    int? VehicleId,
    string? VehicleName,
    string? Plate,
    string BehaviorType, // "harsh_braking", "rapid_acceleration", "sharp_turn", "pothole", "overspeed"
    double? SpeedKph,
    double Latitude,
    double Longitude,
    DateTime Timestamp
) : INotification;
