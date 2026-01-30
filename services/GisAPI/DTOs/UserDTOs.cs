namespace GisAPI.DTOs;

public record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Password,
    int RoleId
);

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    int? RoleId,
    string? Status
);

public record UserListDto(
    int Id,
    string Name,
    string Email,
    string? Phone,
    int RoleId,
    string? RoleName,
    bool IsCompanyAdmin,
    string Status,
    DateTime CreatedAt,
    DateTime? LastLoginAt
);

public record UserSettingsDto(
    string Language,
    string Timezone,
    string Currency,
    string DateFormat,
    string DistanceUnit,
    string SpeedUnit,
    string VolumeUnit,
    string TemperatureUnit
);
