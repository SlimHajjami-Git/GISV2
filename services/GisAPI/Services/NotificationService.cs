using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace GisAPI.Services;

public class NotificationService : INotificationService
{
    private readonly IGisDbContext _context;
    private readonly IHubContext<GpsHub> _hubContext;
    private readonly IFcmService _fcmService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IGisDbContext context,
        IHubContext<GpsHub> hubContext,
        IFcmService fcmService,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _hubContext = hubContext;
        _fcmService = fcmService;
        _logger = logger;
    }

    public async Task<Notification> CreateAndSendAsync(
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
        CancellationToken ct = default)
    {
        var notification = new Notification
        {
            CompanyId = companyId,
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            Priority = priority,
            Channel = "push",
            IsRead = false,
            IsSent = false,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ActionUrl = actionUrl,
            Metadata = metadata
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync(ct);

        // Push via SignalR to user's personal group
        var payload = new
        {
            notification.Id,
            notification.Type,
            notification.Title,
            notification.Message,
            notification.Priority,
            notification.ReferenceType,
            notification.ReferenceId,
            notification.ActionUrl,
            notification.Metadata,
            notification.CreatedAt
        };

        try
        {
            await _hubContext.Clients.Group($"user_{userId}")
                .SendAsync("NewNotification", payload, ct);

            notification.IsSent = true;
            notification.SentAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            _logger.LogDebug("Notification sent to user_{UserId}: {Type} - {Title}", userId, type, title);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push notification to user_{UserId}", userId);
        }

        // Push via FCM for mobile devices (works even when app is closed)
        try
        {
            var fcmData = new Dictionary<string, string>
            {
                ["notificationId"] = notification.Id.ToString(),
                ["type"] = type,
                ["click_action"] = "FLUTTER_NOTIFICATION_CLICK"
            };
            if (metadata != null)
            {
                foreach (var kv in metadata)
                    fcmData[kv.Key] = kv.Value?.ToString() ?? "";
            }
            await _fcmService.SendToUserAsync(userId, title, message, fcmData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send FCM push to user {UserId}", userId);
        }

        // Email fan-out is intentionally NOT done here anymore.
        // Alert emails for assurance / taxe_circulation / visite_technique / entretien
        // are dispatched via IAlertEmailDispatcher from the producers that actually
        // know the alert type (e.g. PredictiveAlertService). In-app notifications
        // (SignalR + FCM above) continue to fire for every notification.

        return notification;
    }

    public async Task SendToUserAsync(int userId, object notification)
    {
        await _hubContext.Clients.Group($"user_{userId}")
            .SendAsync("NewNotification", notification);
    }

    public async Task SendToCompanyAsync(int companyId, object notification)
    {
        await _hubContext.Clients.Group($"company_{companyId}")
            .SendAsync("NewNotification", notification);
    }

    public async Task SendUnreadCountAsync(int userId, int count)
    {
        await _hubContext.Clients.Group($"user_{userId}")
            .SendAsync("UnreadCountChanged", new { count });
    }
}
