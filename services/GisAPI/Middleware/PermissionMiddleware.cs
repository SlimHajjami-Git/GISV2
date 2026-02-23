using System.Security.Claims;
using GisAPI.Domain.Entities;
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
        { "/api/employees", "CanDrivers" },
        { "/api/reports", "CanReports" },
        { "/api/geofences", "CanGeofences" },
        { "/api/maintenance", "CanMaintenance" },
        { "/api/maintenancetemplates", "CanMaintenance" },
        { "/api/maintenancescheduler", "CanMaintenance" },
        { "/api/vehiclemaintenance", "CanMaintenance" },
        { "/api/costs", "CanCosts" },
        { "/api/fuelentries", "CanCosts" },
        { "/api/fuelexpenses", "CanCosts" },
        { "/api/fuelrecords", "CanCosts" },
        { "/api/documents", "CanDocuments" },
        { "/api/accidentclaims", "CanAccidents" },
        { "/api/suppliers", "CanSuppliers" },
        { "/api/fleetmanagement", "CanFleetManagement" },
        { "/api/gps", "CanMonitoring" },
        { "/api/gpsdevices", "CanMonitoring" },
        { "/api/trips", "CanMonitoring" },
        { "/api/vehiclestops", "CanMonitoring" },
        { "/api/alerts", "CanMonitoring" },
        { "/api/drivingbehavior", "CanMonitoring" },
        { "/api/vehicles", "CanVehicles" },
        { "/api/vehicleassignments", "CanVehicles" },
    };

    // Map API route prefixes to the required subscription module flag
    private static readonly Dictionary<string, Func<SubscriptionType, bool>> _subscriptionModuleChecks = new(StringComparer.OrdinalIgnoreCase)
    {
        { "/api/gps", sub => sub.ModuleMonitoring },
        { "/api/gpsdevices", sub => sub.ModuleMonitoring },
        { "/api/trips", sub => sub.ModuleMonitoring },
        { "/api/vehiclestops", sub => sub.ModuleMonitoring },
        { "/api/alerts", sub => sub.ModuleMonitoring },
        { "/api/drivingbehavior", sub => sub.DrivingBehavior },
        { "/api/geofences", sub => sub.ModuleGeofences },
        { "/api/reports", sub => sub.ModuleReports },
        { "/api/maintenance", sub => sub.ModuleMaintenance },
        { "/api/maintenancetemplates", sub => sub.ModuleMaintenance },
        { "/api/maintenancescheduler", sub => sub.ModuleMaintenance },
        { "/api/vehiclemaintenance", sub => sub.ModuleMaintenance },
        { "/api/costs", sub => sub.ModuleCosts },
        { "/api/fuelentries", sub => sub.ModuleCosts },
        { "/api/fuelexpenses", sub => sub.ModuleCosts },
        { "/api/fuelrecords", sub => sub.FuelAnalysis },
        { "/api/documents", sub => sub.ModuleDocuments },
        { "/api/accidentclaims", sub => sub.ModuleAccidents },
        { "/api/suppliers", sub => sub.ModuleSuppliers },
        { "/api/fleetmanagement", sub => sub.ModuleFleetManagement },
        { "/api/users", sub => sub.ModuleUsers },
        { "/api/roles", sub => sub.ModuleUsers },
        { "/api/employees", sub => sub.ModuleEmployees },
        { "/api/drivers", sub => sub.ModuleEmployees },
        { "/api/vehicles", sub => sub.ModuleVehicles },
        { "/api/vehicleassignments", sub => sub.ModuleVehicles },
    };

    // Routes that skip all permission/subscription checks (always accessible when authenticated)
    private static readonly HashSet<string> _skipRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth",
        "/api/dashboard",
        "/api/notifications",
        "/api/settings",
        "/api/profile",
        "/api/subscription",
        "/api/brands",
        "/api/fuelprices",
        "/api/parts",
        "/api/poi",
        "/api/routing",
        "/api/statistics",
        "/api/tours",
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

        // Skip non-API routes
        if (!path.StartsWith("/api/"))
        {
            await _next(context);
            return;
        }

        // Skip always-accessible routes
        if (_skipRoutes.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

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
            await _next(context);
            return;
        }

        // ──────────────────────────────────────────────────────────
        // Load user with company + subscription (single query)
        // ──────────────────────────────────────────────────────────
        var uid = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(uid, out var currentUserId))
        {
            await _next(context);
            return;
        }

        var currentUser = await dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.Societe)
                .ThenInclude(s => s!.SubscriptionType)
            .FirstOrDefaultAsync(u => u.Id == currentUserId);

        if (currentUser == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { message = "Utilisateur introuvable" });
            return;
        }

        // System admins bypass all checks
        if (currentUser.Role?.IsSystemRole == true)
        {
            await _next(context);
            return;
        }

        // ──────────────────────────────────────────────────────────
        // CHECK 1: Subscription module access (company-level limit)
        // ──────────────────────────────────────────────────────────
        var subscriptionType = currentUser.Societe?.SubscriptionType;
        if (subscriptionType != null)
        {
            var matchedSub = _subscriptionModuleChecks
                .FirstOrDefault(kv => path.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase));

            if (matchedSub.Key != null)
            {
                var moduleEnabled = matchedSub.Value(subscriptionType);
                if (!moduleEnabled)
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        message = "Cette fonctionnalité n'est pas incluse dans votre abonnement",
                        code = "SUBSCRIPTION_MODULE_BLOCKED",
                        subscription = subscriptionType.Name
                    });
                    return;
                }
            }
        }

        // ──────────────────────────────────────────────────────────
        // CHECK 2: User-level permission (per-user module access)
        // ──────────────────────────────────────────────────────────
        var isAdmin = currentUser.Role?.IsCompanyAdmin == true || currentUser.AccessLevel == "admin";
        if (!isAdmin)
        {
            var matchedPermission = _modulePermissions
                .FirstOrDefault(kv => path.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase));

            if (matchedPermission.Key != null)
            {
                var allowed = matchedPermission.Value switch
                {
                    "CanMonitoring" => currentUser.CanMonitoring,
                    "CanVehicles" => currentUser.CanVehicles,
                    "CanUsers" => currentUser.CanUsers,
                    "CanDrivers" => currentUser.CanDrivers,
                    "CanReports" => currentUser.CanReports,
                    "CanGeofences" => currentUser.CanGeofences,
                    "CanMaintenance" => currentUser.CanMaintenance,
                    "CanCosts" => currentUser.CanCosts,
                    "CanDocuments" => currentUser.CanDocuments,
                    "CanAccidents" => currentUser.CanAccidents,
                    "CanSuppliers" => currentUser.CanSuppliers,
                    "CanFleetManagement" => currentUser.CanFleetManagement,
                    _ => true
                };

                if (!allowed)
                {
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        message = "Vous n'avez pas accès à ce module",
                        code = "USER_PERMISSION_DENIED"
                    });
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
