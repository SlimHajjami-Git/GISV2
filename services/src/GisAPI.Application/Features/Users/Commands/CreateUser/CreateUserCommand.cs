using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Users.Commands.CreateUser;

public record CreateUserCommand(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Password,
    int RoleId,
    int[]? AssignedVehicleIds = null
) : ICommand<UserListDto>;
