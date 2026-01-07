using System.Security.Claims;
using GisAPI.Domain.Interfaces;

namespace GisAPI.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentTenantService tenantService)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                ?? context.User.FindFirst("sub")?.Value;
            var companyIdClaim = context.User.FindFirst("companyId")?.Value;
            var emailClaim = context.User.FindFirst(ClaimTypes.Email)?.Value 
                ?? context.User.FindFirst("email")?.Value;
            var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
            var permissions = context.User.FindAll("permission").Select(c => c.Value).ToArray();

            if (int.TryParse(userIdClaim, out var userId) && int.TryParse(companyIdClaim, out var companyId))
            {
                tenantService.SetTenant(companyId, userId, emailClaim ?? "", roles, permissions);
            }
        }

        await _next(context);
    }
}

public static class TenantMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TenantMiddleware>();
    }
}
