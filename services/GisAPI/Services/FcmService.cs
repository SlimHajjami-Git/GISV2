using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

public interface IFcmService
{
    Task SendToUserAsync(int userId, string title, string body, Dictionary<string, string>? data = null, int? badgeCount = null);
}

public class FcmService : IFcmService
{
    private readonly IGisDbContext _context;
    private readonly ILogger<FcmService> _logger;
    private readonly bool _initialized;

    public FcmService(IGisDbContext context, ILogger<FcmService> logger, IConfiguration config)
    {
        _context = context;
        _logger = logger;

        if (FirebaseApp.DefaultInstance == null)
        {
            var credPath = config["Firebase:ServiceAccountPath"];
            if (!string.IsNullOrEmpty(credPath) && File.Exists(credPath))
            {
                FirebaseApp.Create(new AppOptions
                {
                    Credential = GoogleCredential.FromFile(credPath)
                });
                _initialized = true;
                _logger.LogInformation("Firebase initialized from {Path}", credPath);
            }
            else
            {
                _logger.LogWarning("Firebase service account not found at '{Path}'. Push notifications disabled.", credPath);
            }
        }
        else
        {
            _initialized = true;
        }
    }

    public async Task SendToUserAsync(int userId, string title, string body, Dictionary<string, string>? data = null, int? badgeCount = null)
    {
        if (!_initialized) return;

        var tokens = await _context.UserDeviceTokens
            .Where(t => t.UserId == userId && t.IsActive)
            .Select(t => t.Token)
            .ToListAsync();

        if (tokens.Count == 0) return;

        var message = new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification
            {
                Title = title,
                Body = body,
            },
            Data = data,
            Android = new AndroidConfig
            {
                Priority = Priority.High,
                Notification = new AndroidNotification
                {
                    Sound = "default",
                    ChannelId = "immobilization",
                    Priority = NotificationPriority.MAX,
                    // App-icon badge count ("cercle avec le nombre"). Launcher-dependent:
                    // Samsung/Xiaomi/etc. render the number, stock Android shows a dot.
                    // Null leaves the badge unchanged.
                    NotificationCount = badgeCount
                }
            },
            // iOS: set the springboard (app-icon) badge number.
            Apns = badgeCount.HasValue
                ? new ApnsConfig { Aps = new Aps { Badge = badgeCount.Value, Sound = "default" } }
                : null
        };

        try
        {
            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
            _logger.LogDebug("FCM sent to user {UserId}: {Success}/{Total} success",
                userId, response.SuccessCount, tokens.Count);

            // Deactivate invalid tokens
            for (int i = 0; i < response.Responses.Count; i++)
            {
                if (!response.Responses[i].IsSuccess)
                {
                    var error = response.Responses[i].Exception?.MessagingErrorCode;
                    if (error == MessagingErrorCode.Unregistered || error == MessagingErrorCode.InvalidArgument)
                    {
                        var badToken = tokens[i];
                        var entity = await _context.UserDeviceTokens
                            .FirstOrDefaultAsync(t => t.Token == badToken);
                        if (entity != null)
                        {
                            entity.IsActive = false;
                            _logger.LogDebug("Deactivated invalid FCM token for user {UserId}", userId);
                        }
                    }
                }
            }
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send FCM to user {UserId}", userId);
        }
    }
}
