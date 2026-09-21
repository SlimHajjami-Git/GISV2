namespace GisAPI.Domain.Interfaces;

public interface ICurrentTenantService
{
    int? CompanyId { get; }
    int? UserId { get; }
    string? UserEmail { get; }
    string[] UserRoles { get; }
    string[] UserPermissions { get; }
    bool IsAuthenticated { get; }
    bool IsSystemAdmin { get; }

    /// <summary>
    /// Compte chauffeur (users.account_type = driver, claim « acct » du jeton) : application
    /// mobile seulement, tournées seulement. Faux pour tout compte ordinaire et pour un
    /// jeton émis avant la migration 050 (sans le claim).
    /// </summary>
    bool IsDriverAccount { get; }

    void SetTenant(int companyId, int userId, string email, string[] roles, string[] permissions, string? accountType = null);
    bool HasPermission(string permission);
}


