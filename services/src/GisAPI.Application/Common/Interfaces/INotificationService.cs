using GisAPI.Domain.Entities;

namespace GisAPI.Application.Common.Interfaces;

public interface INotificationService
{
    /// <summary>
    /// Create a notification, persist it to DB, and push via SignalR if the user is online.
    /// </summary>
    Task<Notification> CreateAndSendAsync(
        int companyId,
        int userId,
        string type,
        string title,
        string message,
        string priority = "normal",
        string? referenceType = null,
        int? referenceId = null,
        string? actionUrl = null,
        Dictionary<string, object>? metadata = null,
        CancellationToken ct = default);

    /// <summary>
    /// Send a real-time notification push to a specific user (without persisting).
    /// </summary>
    Task SendToUserAsync(int userId, object notification);

    /// <summary>
    /// Send a real-time notification push to all users of a company.
    /// </summary>
    Task SendToCompanyAsync(int companyId, object notification);

    /// <summary>
    /// Notify user that their unread count changed.
    /// </summary>
    Task SendUnreadCountAsync(int userId, int count);
}
