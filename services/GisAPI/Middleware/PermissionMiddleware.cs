using System.Security.Claims;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Middleware;

public class PermissionMiddleware
{
    private readonly RequestDelegate _next;

    public PermissionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, GisDbContext dbContext)
    {
        // Skip for non-authenticated requests
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value?.ToLower() ?? "";
        
        // Admin routes check
        if (path.StartsWith("/api/admin"))
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
                
                // Only system_admin or platform_admin can access /api/admin routes
                // This is reserved for application administrators and developers only
                var isSystemAdmin = user?.UserType == "system_admin" || user?.UserType == "platform_admin";
                if (user == null || !isSystemAdmin)
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new { message = "Accès réservé aux administrateurs" });
                    return;
                }
            }
        }

        await _next(context);
    }
}

public static class PermissionMiddlewareExtensions
{
    public static IApplicationBuilder UsePermissionMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PermissionMiddleware>();
    }
}
