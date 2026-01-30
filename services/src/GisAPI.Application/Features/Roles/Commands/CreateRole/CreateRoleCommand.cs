using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Roles.Commands.CreateRole;

public record CreateRoleCommand(
    string Name,
    string? Description,
    bool IsCompanyAdmin,
    Dictionary<string, object>? Permissions
) : ICommand<RoleDto>;

public record RoleDto(
    int Id,
    string Name,
    string? Description,
    int? SocieteId,
    bool IsCompanyAdmin,
    bool IsSystemRole,
    Dictionary<string, object>? Permissions,
    int UsersCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);



