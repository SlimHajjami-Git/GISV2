using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Auth.Commands.Login;

public record LoginCommand(string Email, string Password) : ICommand<LoginResponse>;

public record LoginResponse(
    string Token,
    string RefreshToken,
    UserDto User
);

public record UserDto(
    int Id,
    string Name,
    string Email,
    string? Phone,
    string[] Roles,
    string[] Permissions,
    int CompanyId,
    string CompanyName
);
