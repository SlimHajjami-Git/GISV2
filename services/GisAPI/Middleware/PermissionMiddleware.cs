using System.Security.Claims;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Middleware;

public class PermissionMiddleware
{
    private readonly RequestDelegate _next;

    // Map API route prefixes to the required user permission field
    private static readonly Dictionary<string, string> _modulePermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        { "/api/users", "CanUsers" },
        { "/api/roles", "CanUsers" },
        { "/api/drivers", "CanDrivers" },
        { "/api/reports", "CanReports" },
        { "/api/geofences", "CanGeofences" },
        { "/api/maintenance", "CanMaintenance" },
        { "/api/maintenancetemplates", "CanMaintenance" },
        { "/api/costs", "CanCosts" },
        { "/api/fuelentries", "CanCosts" },
        { "/api/documents", "CanDocuments" },
        { "/api/accidentclaims", "CanAccidents" },
        { "/api/suppliers", "CanSuppliers" },
        { "/api/fleetmanagement", "CanFleetManagement" },
    };

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
        
        // Admin routes check - only System Admin can access /api/admin/*
        if (path.StartsWith("/api/admin"))
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                var user = await dbContext.Users
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.Id == userId);
                
                if (user == null || user.Role == null || !user.Role.IsSystemRole)
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new { message = "Accès réservé aux administrateurs système" });
                    return;
                }
            }
        }

        // Module permission check - enforce canUsers, canDrivers, etc.
        // Skip auth, dashboard, vehicles (handled at query level), gps, notifications, settings endpoints
        var matchedPermission = _modulePermissions
            .FirstOrDefault(kv => path.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase));

        if (matchedPermission.Key != null)
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdClaim, out var userId))
            {
                var user = await dbContext.Users
                    .AsNoTracking()
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user != null)
                {
                    // Company admins bypass module checks
                    var isAdmin = user.Role?.IsCompanyAdmin == true || user.Role?.IsSystemRole == true || user.AccessLevel == "admin";
                    if (!isAdmin)
                    {
                        var allowed = matchedPermission.Value switch
                        {
                            "CanUsers" => user.CanUsers,
                            "CanDrivers" => user.CanDrivers,
                            "CanReports" => user.CanReports,
                            "CanGeofences" => user.CanGeofences,
                            "CanMaintenance" => user.CanMaintenance,
                            "CanCosts" => user.CanCosts,
                            "CanDocuments" => user.CanDocuments,
                            "CanAccidents" => user.CanAccidents,
                            "CanSuppliers" => user.CanSuppliers,
                            "CanFleetManagement" => user.CanFleetManagement,
                            _ => true
                        };

                        if (!allowed)
                        {
                            context.Response.StatusCode = 403;
                            await context.Response.WriteAsJsonAsync(new { message = "Vous n'avez pas accès à ce module" });
                            return;
                        }
                    }
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
