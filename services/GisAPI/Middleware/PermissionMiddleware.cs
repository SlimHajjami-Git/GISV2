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
        // Per-report user permissions (specific routes before generic /api/reports)
        { "/api/reports/trips", "CanReportTrips" },
        { "/api/reports/fuel", "CanReportFuel" },
        { "/api/reports/speed-infraction", "CanReportSpeedInfraction" },
        { "/api/reports/speed", "CanReportSpeed" },
        { "/api/reports/stops", "CanReportStops" },
        { "/api/reports/mileage-period", "CanReportMileagePeriod" },
        { "/api/reports/mileage", "CanReportMileage" },
        { "/api/reports/costs", "CanReportCosts" },
        // Rapports de coûts (04/09/2026) — déjà couverts par le préfixe ci-dessus, déclarés pour la lisibilité.
        { "/api/reports/costs/operating", "CanReportCosts" },
        { "/api/reports/costs/evolution", "CanReportCosts" },
        { "/api/reports/costs/ranking", "CanReportCosts" },
        { "/api/reports/costs/repair-frequency", "CanReportCosts" },
        { "/api/reports/maintenance", "CanReportMaintenance" },
        { "/api/reports/daily", "CanReportDaily" },
        { "/api/reports/monthly", "CanReportMonthly" },
        { "/api/reports/driving-behavior", "CanReportDrivingBehavior" },
        { "/api/reports/monthly-costs", "CanReportMonthlyCosts" },
        // Generic reports fallback (list, create, schedules)
        { "/api/reports", "CanReports" },
        { "/api/geofences", "CanGeofences" },
        { "/api/maintenance", "CanMaintenance" },
        { "/api/maintenancetemplates", "CanMaintenance" },
        { "/api/maintenancescheduler", "CanMaintenance" },
        { "/api/vehiclemaintenance", "CanMaintenance" },
        { "/api/costs", "CanCosts" },
        { "/api/fuelentries", "CanFuel" },
        { "/api/fuelexpenses", "CanFuel" },
        { "/api/fuelrecords", "CanFuel" },
        { "/api/documents", "CanDocuments" },
        { "/api/accidentclaims", "CanAccidents" },
        { "/api/suppliers", "CanSuppliers" },
        { "/api/fleetmanagement", "CanFleetManagement" },
        { "/api/tours", "CanTours" },
        { "/api/gps", "CanMonitoring" },
        { "/api/gpsdevices", "CanMonitoring" },
        { "/api/trips", "CanMonitoring" },
        { "/api/vehiclestops", "CanMonitoring" },
        { "/api/alerts", "CanMonitoring" },
        { "/api/drivingbehavior", "CanMonitoring" },
        { "/api/vehicles/with-positions", "CanMonitoring" },
        { "/api/vehicles", "CanVehicles" },
        { "/api/vehicleassignments", "CanVehicles" },
    };

    // Map API route prefixes to the required subscription module flag
    // NOTE: More specific routes (e.g. /api/reports/trips) must appear before
    //       their parent (e.g. /api/reports) because matching uses FirstOrDefault.
    private static readonly Dictionary<string, Func<SubscriptionType, bool>> _subscriptionModuleChecks = new(StringComparer.OrdinalIgnoreCase)
    {
        { "/api/gps", sub => sub.ModuleMonitoring },
        { "/api/gpsdevices", sub => sub.ModuleMonitoring },
        { "/api/trips", sub => sub.ModuleMonitoring },
        { "/api/vehiclestops", sub => sub.ModuleMonitoring },
        { "/api/alerts", sub => sub.ModuleMonitoring },
        { "/api/drivingbehavior", sub => sub.DrivingBehavior },
        { "/api/geofences", sub => sub.ModuleGeofences },
        // Report-level subscription checks (specific report sub-routes)
        { "/api/reports/trips", sub => sub.ModuleReports && sub.ReportTrips },
        { "/api/reports/fuel", sub => sub.ModuleReports && sub.ReportFuel },
        { "/api/reports/speed", sub => sub.ModuleReports && sub.ReportSpeed },
        { "/api/reports/stops", sub => sub.ModuleReports && sub.ReportStops },
        { "/api/reports/mileage-period", sub => sub.ModuleReports && sub.ReportMileagePeriod },
        { "/api/reports/mileage", sub => sub.ModuleReports && sub.ReportMileage },
        { "/api/reports/costs", sub => sub.ModuleReports && sub.ReportCosts },
        // Rapports de coûts (04/09/2026) — déjà couverts par le préfixe ci-dessus, déclarés pour la lisibilité.
        { "/api/reports/costs/operating", sub => sub.ModuleReports && sub.ReportCosts },
        { "/api/reports/costs/evolution", sub => sub.ModuleReports && sub.ReportCosts },
        { "/api/reports/costs/ranking", sub => sub.ModuleReports && sub.ReportCosts },
        { "/api/reports/costs/repair-frequency", sub => sub.ModuleReports && sub.ReportCosts },
        { "/api/reports/maintenance", sub => sub.ModuleReports && sub.ReportMaintenance },
        { "/api/reports/daily", sub => sub.ModuleReports && sub.ReportDaily },
        { "/api/reports/monthly", sub => sub.ModuleReports && sub.ReportMonthly },
        { "/api/reports/speed-infraction", sub => sub.ModuleReports && sub.ReportSpeedInfraction },
        { "/api/reports/driving-behavior", sub => sub.ModuleReports && sub.ReportDrivingBehavior },
        { "/api/reports/monthly-costs", sub => sub.ModuleReports && sub.ReportMonthlyCosts },
        // Generic /api/reports fallback (list reports, create, schedules, etc.)
        { "/api/reports", sub => sub.ModuleReports },
        { "/api/maintenance", sub => sub.ModuleMaintenance },
        { "/api/maintenancetemplates", sub => sub.ModuleMaintenance },
        { "/api/maintenancescheduler", sub => sub.ModuleMaintenance },
        { "/api/vehiclemaintenance", sub => sub.ModuleMaintenance },
        { "/api/costs", sub => sub.ModuleCosts },
        { "/api/fuelentries", sub => sub.ModuleFuel },
        // Ces deux endpoints DÉDUISENT la consommation des positions GPS
        // (FuelCalculationService lit GpsPositions et Trips). Sans boîtier la
        // distance vaut 0 et le résultat est une estimation fabriquée, doublée
        // d'un verdict « anti-fraude » qui classe toute la flotte en suspect.
        // Ils relèvent donc de ReportFuel, pas du simple module Carburant — qui
        // reste ouvert pour la saisie manuelle des pleins.
        // Placés AVANT le préfixe générique : la correspondance retient la clé
        // la plus longue, mais on les déclare ici pour que l'intention soit lisible.
        { "/api/fuelexpenses/statistics", sub => sub.ModuleFuel && sub.ReportFuel },
        { "/api/fuelexpenses/comparison", sub => sub.ModuleFuel && sub.ReportFuel },
        { "/api/fuelexpenses/vehicle-audit", sub => sub.ModuleFuel && sub.ReportFuel },
        // /api/fuelexpenses/real-consumption reste volontairement sur ModuleFuel :
        // c'est le calcul plein-à-plein, purement manuel, écrit pour les clients
        // sans boîtier. Il ne doit PAS dépendre de ReportFuel.
        { "/api/fuelexpenses", sub => sub.ModuleFuel },
        { "/api/fuelrecords", sub => sub.ModuleFuel },
        { "/api/documents", sub => sub.ModuleDocuments },
        { "/api/accidentclaims", sub => sub.ModuleAccidents },
        { "/api/suppliers", sub => sub.ModuleSuppliers },
        { "/api/fleetmanagement", sub => sub.ModuleFleetManagement },
        { "/api/tours", sub => sub.ModuleTours },
        { "/api/users", sub => sub.ModuleUsers },
        { "/api/roles", sub => sub.ModuleUsers },
        { "/api/employees", sub => sub.ModuleEmployees },
        { "/api/drivers", sub => sub.ModuleEmployees },
        { "/api/vehicles/with-positions", sub => sub.ModuleMonitoring },
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
    };

    // Shared reference data routes: GET is always allowed (many modules need these),
    // but write operations (POST/PUT/DELETE) still require the specific permission.
    private static readonly HashSet<string> _readOnlySharedRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/vehicles",
        "/api/drivers",
        "/api/employees",
        "/api/geofences",
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

        // Shared reference data: GET requests are allowed for any authenticated user
        // (vehicles, drivers, geofences are needed by many modules)
        // Write operations still go through full permission checks below
        if (context.Request.Method == "GET" && _readOnlySharedRoutes.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
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
                .Where(kv => path.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(kv => kv.Key.Length)
                .FirstOrDefault();

            if (matchedSub.Key != null)
            {
                var moduleEnabled = matchedSub.Value(subscriptionType);
                if (!moduleEnabled)
                {
                    // Distinguish between a blocked report type vs a blocked module
                    var isReportTypeBlock = matchedSub.Key.StartsWith("/api/reports/", StringComparison.OrdinalIgnoreCase);
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        message = isReportTypeBlock
                            ? "Ce type de rapport n'est pas inclus dans votre abonnement"
                            : "Cette fonctionnalité n'est pas incluse dans votre abonnement",
                        code = isReportTypeBlock
                            ? "SUBSCRIPTION_REPORT_BLOCKED"
                            : "SUBSCRIPTION_MODULE_BLOCKED",
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
                .Where(kv => path.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(kv => kv.Key.Length)
                .FirstOrDefault();

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
                    "CanFuel" => currentUser.CanFuel,
                    "CanDocuments" => currentUser.CanDocuments,
                    "CanAccidents" => currentUser.CanAccidents,
                    "CanSuppliers" => currentUser.CanSuppliers,
                    "CanFleetManagement" => currentUser.CanFleetManagement,
                    "CanTours" => currentUser.CanTours,
                    // Per-report permissions (require CanReports + specific report permission)
                    "CanReportTrips" => currentUser.CanReports && currentUser.CanReportTrips,
                    "CanReportFuel" => currentUser.CanReports && currentUser.CanReportFuel,
                    "CanReportSpeed" => currentUser.CanReports && currentUser.CanReportSpeed,
                    "CanReportStops" => currentUser.CanReports && currentUser.CanReportStops,
                    "CanReportMileage" => currentUser.CanReports && currentUser.CanReportMileage,
                    "CanReportCosts" => currentUser.CanReports && currentUser.CanReportCosts,
                    "CanReportMaintenance" => currentUser.CanReports && currentUser.CanReportMaintenance,
                    "CanReportDaily" => currentUser.CanReports && currentUser.CanReportDaily,
                    "CanReportMonthly" => currentUser.CanReports && currentUser.CanReportMonthly,
                    "CanReportMileagePeriod" => currentUser.CanReports && currentUser.CanReportMileagePeriod,
                    "CanReportSpeedInfraction" => currentUser.CanReports && currentUser.CanReportSpeedInfraction,
                    "CanReportDrivingBehavior" => currentUser.CanReports && currentUser.CanReportDrivingBehavior,
                    "CanReportMonthlyCosts" => currentUser.CanReports && currentUser.CanReportMonthlyCosts,
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
