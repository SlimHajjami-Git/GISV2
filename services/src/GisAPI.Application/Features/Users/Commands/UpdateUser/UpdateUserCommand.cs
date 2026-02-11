using GisAPI.Application.Common.Interfaces;
using MediatR;

namespace GisAPI.Application.Features.Users.Commands.UpdateUser;

public record UpdateUserCommand(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    int? RoleId,
    string? Status,
    int[]? AssignedVehicleIds = null
) : ICommand;
