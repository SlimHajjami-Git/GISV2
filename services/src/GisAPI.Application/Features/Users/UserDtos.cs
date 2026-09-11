using GisAPI.Application.Features.Auth.Commands.Login;

namespace GisAPI.Application.Features.Users;

public record UserListDto(
    int Id,
    string Name,
    string Email,
    string? Phone,
    int RoleId,
    string? RoleName,
    bool IsCompanyAdmin,
    string Status,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    int[]? AssignedVehicleIds = null,
    UserPermissionsDto? UserPermissions = null,
    bool AlertAssurance = false,
    bool AlertTaxeCirculation = false,
    bool AlertVisiteTechnique = false,
    bool AlertEntretien = false,
    bool DailyReportEmailEnabled = false
)
{
    // Heures silencieuses (migration 045, recette client du 11/09/2026).
    // Propriétés init plutôt que nouveaux paramètres optionnels du constructeur :
    // GetUsers / GetUserById construisent ce DTO dans un Select EF (arbre
    // d'expression), où un appel qui omet un argument optionnel ne compile pas
    // (CS0854). Seul /users/me les renseigne. Les heures sortent en « HH:mm:ss ».
    public bool QuietHoursEnabled { get; init; }
    public TimeSpan? QuietHoursStart { get; init; }
    public TimeSpan? QuietHoursEnd { get; init; }
}
