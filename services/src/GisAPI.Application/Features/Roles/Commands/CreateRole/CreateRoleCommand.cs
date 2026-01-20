using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Roles.Commands.CreateRole;

public record CreateRoleCommand(
    string Name,
    string? Description,
    string RoleType,
    Dictionary<string, object>? Permissions,
    bool IsDefault = false
) : ICommand<RoleDto>;

public record RoleDto(
    int Id,
    string Name,
    string? Description,
    string RoleType,
    Dictionary<string, object>? Permissions,
    int? SocieteId,
    bool IsSystem,
    bool IsDefault,
    int UsersCount,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
