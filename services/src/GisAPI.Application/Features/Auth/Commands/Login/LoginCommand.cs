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
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string? PermitNumber,
    int RoleId,
    string RoleName,
    bool IsCompanyAdmin,
    int CompanyId,
    string CompanyName,
    Dictionary<string, object>? Permissions
);



