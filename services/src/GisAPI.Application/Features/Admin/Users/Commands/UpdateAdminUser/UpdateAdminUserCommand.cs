using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Admin.Users.Commands.UpdateAdminUser;

public record UpdateAdminUserCommand(
    int Id,
    string? Name,
    string? Email,
    string? Phone,
    string? Password
) : ICommand<AdminUserDto>;
