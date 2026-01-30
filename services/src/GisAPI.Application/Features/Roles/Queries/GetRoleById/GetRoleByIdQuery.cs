using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Roles.Commands.CreateRole;

namespace GisAPI.Application.Features.Roles.Queries.GetRoleById;

public record GetRoleByIdQuery(int Id) : IQuery<RoleDto>;



