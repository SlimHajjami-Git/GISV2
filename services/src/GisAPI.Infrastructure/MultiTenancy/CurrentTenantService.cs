using GisAPI.Domain.Interfaces;

namespace GisAPI.Infrastructure.MultiTenancy;

public class CurrentTenantService : ICurrentTenantService
{
    private int? _companyId;
    private int? _userId;
    private string? _userEmail;
    private string[] _userRoles = [];
    private string[] _permissions = [];

    public int? CompanyId => _companyId;
    public int? UserId => _userId;
    public string? UserEmail => _userEmail;
    public string[] UserRoles => _userRoles;
    public string[] UserPermissions => _permissions;
    public bool IsAuthenticated => _userId.HasValue;

    public void SetTenant(int companyId, int userId, string email, string[] roles, string[] permissions)
    {
        _companyId = companyId;
        _userId = userId;
        _userEmail = email;
        _userRoles = roles;
        _permissions = permissions;
    }

    public bool HasPermission(string permission) =>
        _permissions.Contains(permission);
}


