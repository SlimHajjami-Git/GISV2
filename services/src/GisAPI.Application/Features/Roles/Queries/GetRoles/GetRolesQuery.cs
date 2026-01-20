using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Roles.Commands.CreateRole;

namespace GisAPI.Application.Features.Roles.Queries.GetRoles;

public record GetRolesQuery(bool IncludeSystemRoles = true) : IQuery<List<RoleDto>>;
