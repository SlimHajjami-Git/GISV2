using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _permission;
    private readonly string[] _allowedRoles;

    public RequirePermissionAttribute(string permission, params string[] allowedRoles)
    {
        _permission = permission;
        _allowedRoles = allowedRoles;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var dbContext = context.HttpContext.RequestServices.GetRequiredService<GisDbContext>();
        var userIdClaim = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!int.TryParse(userIdClaim, out var userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var user = await dbContext.Users
            .Include(u => u.Role)
            .Include(u => u.Societe)
            .ThenInclude(s => s!.SubscriptionType)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // System admin has all permissions - absolute power
        if (user.IsSystemAdmin)
            return;

        // Check role-based access
        if (_allowedRoles.Length > 0)
        {
            var hasRole = _allowedRoles.Any(r => 
                user.Roles?.Contains(r) == true || 
                user.Role?.Name == r ||
                (r == "company_admin" && user.IsCompanyAdmin));

            if (!hasRole)
            {
                context.Result = new ForbidResult();
                return;
            }
        }

        // Check specific permission
        if (!string.IsNullOrEmpty(_permission))
        {
            var hasPermission = false;

            // Check user permissions
            if (user.Permissions?.Contains(_permission) == true || user.Permissions?.Contains("all") == true)
                hasPermission = true;

            // Check role permissions
            if (!hasPermission && user.Role?.Permissions != null)
            {
                hasPermission = user.Role.Permissions.ContainsKey(_permission) && 
                               (bool)(user.Role.Permissions[_permission] ?? false);
                
                if (!hasPermission)
                    hasPermission = user.Role.Permissions.ContainsKey("all") && 
                                   (bool)(user.Role.Permissions["all"] ?? false);
            }

            // Check subscription access rights
            if (!hasPermission && user.Societe?.SubscriptionType?.AccessRights != null)
            {
                var accessRights = user.Societe.SubscriptionType.AccessRights;
                hasPermission = accessRights.ContainsKey(_permission) && 
                               (bool)(accessRights[_permission] ?? false);
            }

            if (!hasPermission)
            {
                context.Result = new ObjectResult(new { message = $"Permission '{_permission}' requise" })
                {
                    StatusCode = 403
                };
                return;
            }
        }
    }
}

// Shortcut attributes
// RequireAdmin: Only system_admin/platform_admin can access (for /api/admin routes)
public class RequireAdminAttribute : RequirePermissionAttribute
{
    public RequireAdminAttribute() : base("", "system_admin", "platform_admin") { }
}

// RequireCompanyAdmin: company_admin can access company-level management features
public class RequireCompanyAdminAttribute : RequirePermissionAttribute
{
    public RequireCompanyAdminAttribute() : base("", "system_admin", "platform_admin", "company_admin") { }
}
