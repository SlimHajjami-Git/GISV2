using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common;
using GisAPI.Infrastructure.Persistence;

namespace GisAPI.Middleware;

/// <summary>
/// Bloque à chaque requête les sociétés dont l'abonnement est suspendu/annulé par
/// le sys_admin, ou expiré AU-DELÀ de la période de grâce (7 jours). Pendant la
/// grâce, l'accès est maintenu (la bannière rouge côté client avertit) et le statut
/// passe à "expired" pour la supervision — sans toucher IsActive, qui est réservé
/// à la suspension manuelle. Les règles viennent de <see cref="SubscriptionPolicy"/>.
/// </summary>
public class SubscriptionExpirationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SubscriptionExpirationMiddleware> _logger;

    // Paths that should always be accessible regardless of subscription status.
    // /api/subscriptions/banner doit rester joignable une fois bloqué : c'est lui
    // qui dit à l'écran de blocage pourquoi et jusqu'à quand.
    private static readonly string[] AllowedPaths = new[]
    {
        "/api/auth",
        "/api/admin",
        "/api/health",
        "/api/subscription/status",
        "/api/subscription/renew",
        "/api/subscriptions/banner",
        "/swagger",
        "/hub"
    };

    public SubscriptionExpirationMiddleware(RequestDelegate next, ILogger<SubscriptionExpirationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, GisDbContext dbContext)
    {
        var path = context.Request.Path.Value?.ToLower() ?? "";

        if (AllowedPaths.Any(p => path.StartsWith(p.ToLower())))
        {
            await _next(context);
            return;
        }

        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        // Le sys_admin plateforme n'est jamais bloqué par l'abonnement de SA société.
        if (context.User.IsInRole("system_admin"))
        {
            await _next(context);
            return;
        }

        var companyIdClaim = context.User.FindFirst("companyId")?.Value;
        if (string.IsNullOrEmpty(companyIdClaim) || !int.TryParse(companyIdClaim, out var companyId))
        {
            await _next(context);
            return;
        }

        var company = await dbContext.Societes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company == null)
        {
            await _next(context);
            return;
        }

        var state = SubscriptionPolicy.Evaluate(company, DateTime.UtcNow);

        // Date dépassée : marquer "expired" pour la supervision — SANS IsActive=false
        // (l'accès reste ouvert pendant la grâce ; IsActive = suspension manuelle).
        if (company.SubscriptionExpiresAt < DateTime.UtcNow && company.SubscriptionStatus == "active")
        {
            var toUpdate = await dbContext.Societes.FindAsync(companyId);
            if (toUpdate != null)
            {
                toUpdate.SubscriptionStatus = "expired";
                toUpdate.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync();
            }
        }

        if (!state.IsBlocked)
        {
            await _next(context);
            return;
        }

        _logger.LogWarning("Blocked access for company {CompanyId}: subscription {Reason}", companyId, state.Reason);

        context.Response.StatusCode = 403;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "subscription_" + state.Reason,
            message = state.Reason switch
            {
                "suspended" => "L'abonnement de votre société est suspendu. Veuillez contacter votre prestataire.",
                "cancelled" => "L'abonnement de votre société a été annulé. Veuillez contacter votre prestataire.",
                _ => "L'abonnement de votre société a expiré. Veuillez le renouveler pour continuer à utiliser la plateforme."
            },
            status = state.Reason,
            expiredAt = state.ExpiresAt
        });
    }
}

/// <summary>
/// Extension methods for subscription middleware
/// </summary>
public static class SubscriptionExpirationMiddlewareExtensions
{
    public static IApplicationBuilder UseSubscriptionExpiration(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SubscriptionExpirationMiddleware>();
    }
}
