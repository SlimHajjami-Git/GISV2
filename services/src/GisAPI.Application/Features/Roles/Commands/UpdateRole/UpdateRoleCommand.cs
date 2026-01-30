using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Roles.Commands.CreateRole;

namespace GisAPI.Application.Features.Roles.Commands.UpdateRole;

public record UpdateRoleCommand(
    int Id,
    string? Name,
    string? Description,
    bool? IsCompanyAdmin,
    Dictionary<string, object>? Permissions
) : ICommand<RoleDto>;



