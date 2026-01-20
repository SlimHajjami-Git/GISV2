using Microsoft.EntityFrameworkCore;
using GisAPI.Infrastructure.Persistence;
using System.Security.Claims;

namespace GisAPI.Middleware;

/// <summary>
/// Middleware to check subscription expiration and block access for expired subscriptions
/// </summary>
public class SubscriptionExpirationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SubscriptionExpirationMiddleware> _logger;

    // Paths that should always be accessible regardless of subscription status
    private static readonly string[] AllowedPaths = new[]
    {
        "/api/auth",
        "/api/admin",
        "/api/health",
        "/api/subscription/status",
        "/api/subscription/renew",
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

        // Allow certain paths without subscription check
        if (AllowedPaths.Any(p => path.StartsWith(p.ToLower())))
        {
            await _next(context);
            return;
        }

        // Check if user is authenticated
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        // Get company ID from claims
        var companyIdClaim = context.User.FindFirst("companyId")?.Value;
        if (string.IsNullOrEmpty(companyIdClaim) || !int.TryParse(companyIdClaim, out var companyId))
        {
            await _next(context);
            return;
        }

        // Check company subscription status
        var company = await dbContext.Societes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company == null)
        {
            await _next(context);
            return;
        }

        // Check if subscription is expired
        if (company.SubscriptionExpiresAt.HasValue && company.SubscriptionExpiresAt.Value < DateTime.UtcNow)
        {
            // Update company status if not already expired
            if (company.SubscriptionStatus != "expired")
            {
                var companyToUpdate = await dbContext.Societes.FindAsync(companyId);
                if (companyToUpdate != null)
                {
                    companyToUpdate.SubscriptionStatus = "expired";
                    companyToUpdate.IsActive = false;
                    companyToUpdate.UpdatedAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync();
                }
            }

            _logger.LogWarning("Blocked access for company {CompanyId} due to expired subscription", companyId);

            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "subscription_expired",
                message = "Votre abonnement a expiré. Veuillez renouveler pour continuer à utiliser la plateforme.",
                expiredAt = company.SubscriptionExpiresAt,
                renewUrl = "/subscription/renew"
            });
            return;
        }

        // Check if subscription is suspended
        if (company.SubscriptionStatus == "suspended" || company.SubscriptionStatus == "cancelled")
        {
            _logger.LogWarning("Blocked access for company {CompanyId} due to {Status} subscription", companyId, company.SubscriptionStatus);

            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "subscription_" + company.SubscriptionStatus,
                message = company.SubscriptionStatus == "suspended" 
                    ? "Votre abonnement est suspendu. Veuillez contacter le support."
                    : "Votre abonnement a été annulé. Veuillez renouveler pour continuer.",
                status = company.SubscriptionStatus
            });
            return;
        }

        await _next(context);
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
