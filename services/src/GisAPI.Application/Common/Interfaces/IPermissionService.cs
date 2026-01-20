using GisAPI.Domain.Entities;

namespace GisAPI.Application.Common.Interfaces;

public interface IPermissionService
{
    /// <summary>
    /// Gets all permissions available for a subscription type
    /// </summary>
    Dictionary<string, object> GetSubscriptionPermissions(SubscriptionType subscriptionType);
    
    /// <summary>
    /// Gets the full permission template structure for UI display
    /// </summary>
    Dictionary<string, object> GetPermissionTemplate();
    
    /// <summary>
    /// Validates that role permissions don't exceed subscription permissions
    /// </summary>
    bool ValidateRolePermissions(Dictionary<string, object>? rolePermissions, SubscriptionType subscriptionType);
    
    /// <summary>
    /// Gets the list of permission keys that exceed subscription limits
    /// </summary>
    List<string> GetExceedingPermissions(Dictionary<string, object>? rolePermissions, SubscriptionType subscriptionType);
}
