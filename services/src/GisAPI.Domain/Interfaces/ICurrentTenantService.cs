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
    
    void SetTenant(int companyId, int userId, string email, string[] roles, string[] permissions);
    bool HasPermission(string permission);
}


