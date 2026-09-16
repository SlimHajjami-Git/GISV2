using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace GisAPI.Middleware;

/// <summary>
/// Limitation de débit de l'API. Seules les routes ci-dessous sont plafonnées ; tout le
/// reste passe sans limite (rafraîchissement de jeton, hub SignalR et lectures compris).
/// Les partitions sont chaînées : une requête doit passer toutes celles qui la visent.
/// L'adresse IP vient de X-Forwarded-For derrière Traefik (AssistantController.ResolveClientIp).
///
/// Sortie du corps de Program.cs pour être testée telle qu'elle est branchée : DEF-030
/// venait d'une route (la connexion) absente de cette chaîne, ce qu'un test rejouant
/// sa propre copie de la chaîne ne pouvait pas voir.
/// </summary>
public static class RateLimitPolicies
{
    public const string TooManyQuestionsMessage =
        "Trop de questions en peu de temps. Patientez quelques instants avant de réessayer.";

    public const string TooManyRegistrationsMessage =
        "Trop de demandes d'inscription en peu de temps. Réessayez dans une heure.";

    public const string TooManyRequestsMessage =
        "Trop de requêtes en peu de temps. Patientez quelques instants avant de réessayer.";

    /// <summary>Retry-After quand le limiteur n'en fournit pas (fenêtre glissante de l'assistant).</summary>
    public const int DefaultRetryAfterSeconds = 30;

    public static bool IsAssistant(HttpContext c) => c.Request.Path.StartsWithSegments("/api/assistant");

    // Invoice scan hits the paid Groq VISION model per request — throttle per IP
    // so an authenticated user (or a runaway client retry) can't drain cost/disk.
    public static bool IsInvoiceScan(HttpContext c) => c.Request.Path.StartsWithSegments("/api/costs/scan-invoice");

    // L'inscription libre est la seule route d'écriture ouverte sans jeton : sans
    // plafond, une boucle crée des milliers de sociétés et d'utilisateurs. Deux
    // barrières : une par adresse IP, et une globale qui borne les dégâts d'un
    // réseau de machines.
    // Le renvoi de confirmation est plafonné avec l'inscription : sans cela, il
    // servirait à noyer une boîte mail sous des courriels que NOUS envoyons.
    public static bool IsRegister(HttpContext c) =>
        c.Request.Path.StartsWithSegments("/api/auth/register")
        || c.Request.Path.StartsWithSegments("/api/auth/resend-confirmation");

    public static void Configure(RateLimiterOptions options, IConfiguration configuration)
    {
        // For /api/assistant we apply a CHAINED limiter (both parts must pass): a per-IP
        // sliding window (spam/abuse) AND a service-wide fixed window (a hard cost ceiling
        // across all IPs).
        var aiPerIpPerMin  = configuration.GetValue<int?>("AiAssistant:RequestsPerMinutePerIp") ?? 10;
        var aiGlobalPerMin = configuration.GetValue<int?>("AiAssistant:GlobalRequestsPerMinute") ?? 90;
        // Connexion : réglable par déploiement sans reconstruire l'image, au cas où un
        // client nombreux derrière une même adresse publique buterait sur le plafond.
        var loginPerIpPerMin = configuration.GetValue<int?>("Auth:LoginAttemptsPerMinutePerIp")
            ?? GisAPI.Controllers.AuthController.DefaultLoginAttemptsPerMinutePerIp;

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
            // -3) connexion : plafond par IP contre la force brute (DEF-030)
            PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                GisAPI.Controllers.AuthController.LoginAttemptPartition(ctx, loginPerIpPerMin)),
            // -2) mot de passe oublié : 5 demandes par heure et par IP
            PartitionedRateLimiter.Create<HttpContext, string>(
                GisAPI.Controllers.AuthController.PasswordResetRequestPartition),
            // -1) inscription libre : 3 par heure et par IP, 30 par heure au total
            PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                IsRegister(ctx)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        "register:" + GisAPI.Controllers.AssistantController.ResolveClientIp(ctx),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 3,
                            Window = TimeSpan.FromHours(1),
                            QueueLimit = 0
                        })
                    : RateLimitPartition.GetNoLimiter("open")),
            PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                IsRegister(ctx)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        "register-global",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromHours(1),
                            QueueLimit = 0
                        })
                    : RateLimitPartition.GetNoLimiter("open")),
            // 0) invoice scan: per-IP fixed window (runs before auth, so partition by IP)
            PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                IsInvoiceScan(ctx)
                    ? RateLimitPartition.GetFixedWindowLimiter(
                        "scan:" + GisAPI.Controllers.AssistantController.ResolveClientIp(ctx),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 12,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        })
                    : RateLimitPartition.GetNoLimiter("open")),
            // 1) per-IP sliding window
            PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                IsAssistant(ctx)
                    ? RateLimitPartition.GetSlidingWindowLimiter(
                        "ip:" + GisAPI.Controllers.AssistantController.ResolveClientIp(ctx),
                        _ => new SlidingWindowRateLimiterOptions
                        {
                            PermitLimit = aiPerIpPerMin,
                            Window = TimeSpan.FromMinutes(1),
                            SegmentsPerWindow = 6,
                            QueueLimit = 0
                        })
                    : RateLimitPartition.GetNoLimiter("open")),
            // 2) service-wide fixed window (cost safety net)
            PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                IsAssistant(ctx)
                    ? RateLimitPartition.GetFixedWindowLimiter("assistant-global",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = aiGlobalPerMin,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0
                        })
                    : RateLimitPartition.GetNoLimiter("open")));

        options.OnRejected = async (context, token) =>
        {
            var http = context.HttpContext;
            http.Response.Headers["Retry-After"] =
                RetryAfterSeconds(context.Lease).ToString(System.Globalization.CultureInfo.InvariantCulture);
            http.Response.ContentType = "application/json";
            await http.Response.WriteAsJsonAsync(new
            {
                status = 429,
                message = RejectionMessage(http)
            }, token);
        };
    }

    /// <summary>
    /// Un message unique disait « Trop de questions » à quelqu'un qui se connecte ou
    /// s'inscrit, et « quelques instants » quand la reprise n'arrive qu'une heure plus tard.
    /// </summary>
    public static string RejectionMessage(HttpContext http) =>
        GisAPI.Controllers.AuthController.IsLoginAttempt(http)
            ? GisAPI.Controllers.AuthController.TooManyLoginAttemptsMessage
            : GisAPI.Controllers.AuthController.IsPasswordResetRequest(http)
                ? GisAPI.Controllers.AuthController.TooManyPasswordResetRequestsMessage
                : IsRegister(http)
                    ? TooManyRegistrationsMessage
                    : IsAssistant(http)
                        ? TooManyQuestionsMessage
                        : TooManyRequestsMessage;

    /// <summary>
    /// Une fenêtre fixe annonce la LONGUEUR de sa fenêtre, pas le temps qui en reste :
    /// Retry-After est donc une borne haute, jamais une reprise trop tôt. La fenêtre
    /// glissante de l'assistant n'annonce rien, d'où la valeur par défaut.
    /// </summary>
    public static int RetryAfterSeconds(RateLimitLease lease) =>
        lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))
            : DefaultRetryAfterSeconds;
}
