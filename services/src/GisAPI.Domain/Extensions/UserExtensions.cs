using GisAPI.Domain.Entities;

namespace GisAPI.Domain.Extensions;

public static class UserExtensions
{
    /// <summary>
    /// Checks if user has company admin privileges via their role
    /// </summary>
    public static bool IsAnyAdmin(this User? user)
    {
        if (user == null) return false;
        return user.Role?.IsCompanyAdmin ?? false;
    }

    /// <summary>
    /// Checks if user has a specific role name
    /// </summary>
    public static bool HasRole(this User? user, string roleName)
    {
        if (user == null || user.Role == null) return false;
        return user.Role.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if user has a specific permission via their role's permissions
    /// </summary>
    public static bool HasPermission(this User? user, string permission)
    {
        if (user == null || user.Role?.Permissions == null) return false;
        
        // Check in modules permissions
        if (user.Role.Permissions.TryGetValue("modules", out var modules) && modules is Dictionary<string, object> moduleDict)
        {
            if (moduleDict.TryGetValue(permission, out var value) && value is bool boolValue)
                return boolValue;
        }
        
        return false;
    }
}


