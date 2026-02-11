using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Admin.Users.Commands.CreateAdminUser;

public record CreateAdminUserCommand(
    string Name,
    string Email,
    string Password,
    string? Phone,
    int CompanyId,
    int RoleId,
    int[]? AssignedVehicleIds = null
) : ICommand<AdminUserDto>;
