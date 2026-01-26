using GisAPI.Domain.Entities;

namespace GisAPI.Domain.Extensions;

public static class UserExtensions
{
    /// <summary>
    /// Checks if user is a system-level administrator (system_admin or platform_admin)
    /// </summary>
    public static bool IsSystemAdmin(this User? user)
    {
        if (user == null) return false;
        return user.UserType == "system_admin" || user.UserType == "platform_admin";
    }

    /// <summary>
    /// Checks if user has any admin privileges (system admin or company admin)
    /// </summary>
    public static bool IsAnyAdmin(this User? user)
    {
        if (user == null) return false;
        return user.IsSystemAdmin() || user.IsCompanyAdmin;
    }

    /// <summary>
    /// Checks if user has a specific role in their Roles array
    /// </summary>
    public static bool HasRole(this User? user, string role)
    {
        if (user == null || user.Roles == null) return false;
        return user.Roles.Contains(role);
    }

    /// <summary>
    /// Checks if user has a specific permission in their Permissions array
    /// </summary>
    public static bool HasPermission(this User? user, string permission)
    {
        if (user == null || user.Permissions == null) return false;
        return user.Permissions.Contains(permission) || user.Permissions.Contains("all");
    }
}
