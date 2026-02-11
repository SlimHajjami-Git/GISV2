using MediatR;

namespace GisAPI.Application.Features.Notifications.Events;

/// <summary>
/// Published when a vehicle's maintenance schedule is due or overdue.
/// </summary>
public record MaintenanceDueNotificationEvent(
    int CompanyId,
    int VehicleId,
    string VehicleName,
    int ScheduleId,
    string TemplateName,
    string Status, // "due" or "overdue"
    int? KmUntilDue,
    int? DaysUntilDue
) : INotification;
