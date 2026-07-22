using System.Security.Claims;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;

namespace GisAPI.Middleware;

/// <summary>
/// Piste d'audit GLOBALE : journalise chaque action authentifiée qui MODIFIE
/// des données (POST/PUT/PATCH/DELETE sur /api) dans audit_logs — utilisateur,
/// société, module, entité, IP, statut. Complète les événements login/logout
/// déjà écrits par les handlers d'authentification.
///
/// Choix délibéré : les GET ne sont PAS journalisés (le polling du monitoring
/// génèrerait des milliers de lignes/heure sans valeur). « Chaque mouvement »
/// = chaque action qui change quelque chose + connexions/déconnexions.
/// L'écriture est best-effort : un échec d'audit ne casse jamais la requête.
/// </summary>
public class AuditTrailMiddleware
{
    private static readonly string[] AuditedMethods = { "POST", "PUT", "PATCH", "DELETE" };

    // Exclusions : déjà journalisés par leurs handlers (auth), purement
    // techniques (refresh, hubs), ou sans intérêt d'audit (broadcast interne).
    private static readonly string[] ExcludedPrefixes =
    {
        "/api/auth/login", "/api/auth/refresh", "/api/auth/logout",
        "/api/hubs", "/api/assistant"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<AuditTrailMiddleware> _logger;

    public AuditTrailMiddleware(RequestDelegate next, ILogger<AuditTrailMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        try
        {
            var path = context.Request.Path.Value ?? "";
            if (!AuditedMethods.Contains(context.Request.Method)) return;
            if (!path.StartsWith("/api/")) return;
            if (ExcludedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return;
            if (context.Response.StatusCode >= 400) return;                  // seules les actions RÉUSSIES
            if (context.User?.Identity?.IsAuthenticated != true) return;

            var userIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? context.User.FindFirst("sub")?.Value;
            int.TryParse(userIdStr, out var userId);
            int.TryParse(context.User.FindFirst("companyId")?.Value, out var companyId);

            var (action, module, entityId) = DescribeRequest(context.Request.Method, path);

            // Scope dédié : le middleware vit hors du scope DI de la requête MVC.
            using var scope = context.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GisDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                UserId = userId > 0 ? userId : null,
                CompanyId = companyId > 0 ? companyId : null,
                Action = action,
                EntityType = module,
                EntityId = entityId,
                Description = $"{context.Request.Method} {path}",
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua
                    ? (ua.Length > 250 ? ua[..250] : ua) : null,
                Timestamp = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // best-effort : jamais bloquant
            _logger.LogDebug(ex, "Audit trail write failed for {Path}", context.Request.Path);
        }
    }

    /// <summary>« POST /api/admin/vehicles/12 » → (creation_vehicles, vehicles, 12).</summary>
    private static (string action, string module, int? entityId) DescribeRequest(string method, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries); // ["api","admin","vehicles","12"]
        var segs = parts.Skip(1).Where(s => !s.Equals("admin", StringComparison.OrdinalIgnoreCase)).ToArray();
        var module = segs.Length > 0 ? segs[0].ToLowerInvariant() : "api";

        int? entityId = null;
        foreach (var s in segs.Skip(1))
            if (int.TryParse(s, out var id)) { entityId = id; break; }

        var verb = method switch
        {
            "POST" => "creation",
            "DELETE" => "suppression",
            _ => "modification"
        };
        return ($"{verb}_{module}", module, entityId);
    }
}
