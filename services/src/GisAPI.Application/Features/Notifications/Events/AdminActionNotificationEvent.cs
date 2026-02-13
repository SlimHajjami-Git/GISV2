using MediatR;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Published when an employee performs a notable action (creates vehicle, driver, maintenance, etc.).
/// Triggers notification to company admins so they stay informed of employee activity.
/// </summary>
public record AdminActionNotificationEvent(
    int CompanyId,
    int ActorUserId,
    string ActorName,
    string ActionType, // "vehicle_created", "driver_created", "user_created", "maintenance_created", "cost_created", "document_created", "accident_created"
    string EntityName,
    int? EntityId,
    string? EntityType // "vehicle", "driver", "user", "maintenance", "cost", "document", "accident"
) : INotification;
