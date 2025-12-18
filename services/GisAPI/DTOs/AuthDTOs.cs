namespace GisAPI.DTOs;

public record LoginRequest(string Email, string Password);

public record RegisterRequest(
    string Name,
    string Email,
    string Password,
    string CompanyName,
    string? Phone = null
);

public record AuthResponse(
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

public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword
);
