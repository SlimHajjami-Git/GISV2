using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Admin.Users.Commands.UpdateAdminUserRole;

public record UpdateAdminUserRoleCommand(int UserId, int RoleId) : ICommand;
