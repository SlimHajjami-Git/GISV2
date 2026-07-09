using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>Result of an FCM multicast send — used by the test endpoint and logs.</summary>
public record FcmSendResult(bool Initialized, int TokenCount, int SuccessCount, int FailureCount, List<string> Errors);

public interface IFcmService
{
    Task<FcmSendResult> SendToUserAsync(int userId, string title, string body, Dictionary<string, string>? data = null, int? badgeCount = null);
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

    public async Task<FcmSendResult> SendToUserAsync(int userId, string title, string body, Dictionary<string, string>? data = null, int? badgeCount = null)
    {
        if (!_initialized)
        {
            _logger.LogWarning("FCM push skipped for user {UserId}: Firebase not initialized (service account missing?).", userId);
            return new FcmSendResult(false, 0, 0, 0, new List<string> { "firebase_not_initialized" });
        }

        var entities = await _context.UserDeviceTokens
            .Where(t => t.UserId == userId && t.IsActive)
            .ToListAsync();

        // Hygiène : un jeton jamais revu depuis 60 jours est considéré périmé
        // (appli désinstallée / téléphone remplacé) — on le désactive au lieu
        // de continuer à lui envoyer des pushs.
        var staleCutoff = DateTime.UtcNow.AddDays(-60);
        foreach (var t in entities.Where(t => (t.LastUsedAt ?? t.RegisteredAt) < staleCutoff).ToList())
        {
            t.IsActive = false;
            entities.Remove(t);
        }

        var tokens = entities.Select(t => t.Token).ToList();

        if (tokens.Count == 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("FCM push skipped for user {UserId}: no active device tokens.", userId);
            return new FcmSendResult(true, 0, 0, 0, new List<string> { "no_active_tokens" });
        }

        // Clé de collapse STABLE par événement : si le même téléphone possède
        // plusieurs jetons encore livrables (réinstallations pendant les tests
        // Play Store → 4 copies constatées), les N pushs se REMPLACENT à
        // l'affichage (tag Android / apns-collapse-id iOS) au lieu de s'empiler.
        var collapseKey = MakeCollapseKey(title, body, data);

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
                    // Même tag = la notification remplace la précédente sur l'appareil.
                    Tag = collapseKey,
                    // App-icon badge count ("cercle avec le nombre"). Launcher-dependent:
                    // Samsung/Xiaomi/etc. render the number, stock Android shows a dot.
                    // Null leaves the badge unchanged.
                    NotificationCount = badgeCount
                }
            },
            // iOS : collapse-id (remplace la notification de même id) + badge éventuel.
            Apns = new ApnsConfig
            {
                Headers = new Dictionary<string, string> { ["apns-collapse-id"] = collapseKey },
                Aps = badgeCount.HasValue
                    ? new Aps { Badge = badgeCount.Value, Sound = "default" }
                    : new Aps { Sound = "default" }
            }
        };

        try
        {
            var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);

            // Collect per-token error codes + deactivate permanently-invalid tokens.
            var errors = new List<string>();
            for (int i = 0; i < response.Responses.Count; i++)
            {
                if (response.Responses[i].IsSuccess)
                {
                    // Jeton confirmé vivant — utilisé par la purge des 60 jours.
                    entities[i].LastUsedAt = DateTime.UtcNow;
                }
                if (!response.Responses[i].IsSuccess)
                {
                    var respEx = response.Responses[i].Exception;
                    var error = respEx?.MessagingErrorCode;
                    // When the code is null ("unknown") the real cause is in the message
                    // (e.g. "Firebase Cloud Messaging API has not been used ... or it is
                    // disabled", auth/permission errors, project mismatch). Surface it.
                    errors.Add(error?.ToString()
                        ?? respEx?.Message?.Replace(",", ";")
                        ?? "unknown");
                    if (error == MessagingErrorCode.Unregistered || error == MessagingErrorCode.InvalidArgument)
                    {
                        var badToken = tokens[i];
                        var entity = await _context.UserDeviceTokens
                            .FirstOrDefaultAsync(t => t.Token == badToken);
                        if (entity != null)
                        {
                            entity.IsActive = false;
                            _logger.LogInformation("Deactivated invalid FCM token for user {UserId}", userId);
                        }
                    }
                }
            }
            await _context.SaveChangesAsync();

            // Information level (was Debug) so FCM delivery is observable in prod logs.
            _logger.LogInformation("FCM sent to user {UserId}: {Success}/{Total} success{ErrorSuffix}",
                userId, response.SuccessCount, tokens.Count,
                errors.Count > 0 ? $" — errors: {string.Join(",", errors)}" : string.Empty);

            return new FcmSendResult(true, tokens.Count, response.SuccessCount, response.FailureCount, errors);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send FCM to user {UserId}", userId);
            return new FcmSendResult(true, tokens.Count, 0, tokens.Count, new List<string> { ex.Message });
        }
    }

    /// <summary>
    /// Clé stable identifiant l'ÉVÉNEMENT notifié : type + référence quand la
    /// payload les fournit, sinon empreinte du titre + corps. Deux pushs du même
    /// événement (multi-jetons d'un même téléphone) partagent la clé et se
    /// remplacent à l'affichage ; deux événements distincts ont des clés
    /// différentes et s'empilent normalement.
    /// </summary>
    private static string MakeCollapseKey(string title, string body, Dictionary<string, string>? data)
    {
        string basis;
        if (data != null
            && data.TryGetValue("type", out var type) && !string.IsNullOrEmpty(type)
            && data.TryGetValue("referenceId", out var refId) && !string.IsNullOrEmpty(refId))
        {
            basis = $"{type}:{refId}";
        }
        else
        {
            basis = $"{title}|{body}";
        }

        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(basis));
        return "evt-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
