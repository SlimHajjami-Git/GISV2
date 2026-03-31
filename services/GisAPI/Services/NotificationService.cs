using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Services;

public class NotificationService : INotificationService
{
    private readonly IGisDbContext _context;
    private readonly IHubContext<GpsHub> _hubContext;
    private readonly IEmailService _emailService;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        IGisDbContext context,
        IHubContext<GpsHub> hubContext,
        IEmailService emailService,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _hubContext = hubContext;
        _emailService = emailService;
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

        // Send email notification (TEST: forced to hajjami.selim@gmail.com)
        try
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendNotificationEmailAsync(
                        "hajjami.selim@gmail.com",
                        "Selim Hajjami",
                        type,
                        title,
                        message,
                        actionUrl,
                        CancellationToken.None
                    );
                }
                catch (Exception emailEx)
                {
                    _logger.LogWarning(emailEx, "Failed to send notification email to test address");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send test notification email");
        }

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
