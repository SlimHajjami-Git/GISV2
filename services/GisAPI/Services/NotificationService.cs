using GisAPI.Application.Common;
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

        // Heures silencieuses (recette client du 11/09/2026) : pendant la plage de
        // l'utilisateur, une notification non « critical » est quand même enregistrée et
        // arrive dans la cloche (ligne + SignalR + compteur non lu), mais SANS push FCM
        // ni toast à l'écran (Silent = true, ignoré par notification-toast.service).
        // Les accidents restent « critical » et passent toujours.
        var quiet = await IsInQuietHoursAsync(userId, priority, ct);

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
            notification.CreatedAt,
            Silent = quiet
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

        // Authoritative unread count — used BOTH for the FCM app-icon badge (so the
        // "cercle avec le nombre" shows even when the app is closed) and the SignalR
        // bell badge below. Computed once after the insert so it includes this notif.
        var unreadCount = 0;
        try
        {
            unreadCount = await _context.Notifications
                .AsNoTracking()
                .CountAsync(n => n.UserId == userId && !n.IsRead, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute unread count for user {UserId}", userId);
        }

        // Push via FCM for mobile devices (works even when app is closed)
        if (quiet)
        {
            _logger.LogInformation(
                "Heures silencieuses : push FCM non envoyé à user {UserId} ({Type}, priorité {Priority}) — notification {NotificationId} conservée dans la cloche",
                userId, type, priority, notification.Id);
        }
        else
        {
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
                await _fcmService.SendToUserAsync(userId, title, message, fcmData, unreadCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send FCM push to user {UserId}", userId);
            }
        }

        // Email fan-out is intentionally NOT done here anymore.
        // Alert emails for assurance / taxe_circulation / visite_technique / entretien
        // are dispatched via IAlertEmailDispatcher from the producers that actually
        // know the alert type (e.g. PredictiveAlertService). In-app notifications
        // (SignalR + FCM above) continue to fire for every notification.

        // Calypso 6 (P9): push the authoritative unread count so the bell
        // badge updates in real-time for ALL notification types (geofence,
        // document expiry, accident, tow detected, etc.). Previously only
        // the NewNotification event fired, and clients had to increment
        // locally — which caused desync when the client missed the event.
        try
        {
            await SendUnreadCountAsync(userId, unreadCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to push unread count to user {UserId}", userId);
        }

        return notification;
    }

    /// <summary>
    /// Le destinataire est-il dans ses heures silencieuses ? Jamais pour une notification
    /// « critical ». Heures lues dans le fuseau de SA société (Societe.Settings.Timezone,
    /// repli Africa/Tunis), pas celui du navigateur. Une erreur de lecture ne doit jamais
    /// empêcher une notification de partir : on retombe alors sur l'envoi normal.
    /// IgnoreQueryFilters : appelé aussi depuis les services d'arrière-plan (sans tenant).
    /// </summary>
    private async Task<bool> IsInQuietHoursAsync(int userId, string priority, CancellationToken ct)
    {
        if (string.Equals(priority, "critical", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            var prefs = await _context.Users
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(u => u.Id == userId)
                .Select(u => new { u.QuietHoursEnabled, u.QuietHoursStart, u.QuietHoursEnd, u.CompanyId })
                .FirstOrDefaultAsync(ct);

            // Cas courant (réglage désactivé) : une seule lecture par clé primaire.
            if (prefs == null || !prefs.QuietHoursEnabled) return false;

            var settings = await _context.Societes
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(s => s.Id == prefs.CompanyId)
                .Select(s => s.Settings)
                .FirstOrDefaultAsync(ct);

            return QuietHoursPolicy.IsQuietNow(
                prefs.QuietHoursEnabled,
                prefs.QuietHoursStart,
                prefs.QuietHoursEnd,
                DateTime.UtcNow,
                QuietHoursPolicy.ResolveTimeZone(settings?.Timezone));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lecture des heures silencieuses impossible pour user {UserId} — envoi normal", userId);
            return false;
        }
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
