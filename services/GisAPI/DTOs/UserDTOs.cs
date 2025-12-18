namespace GisAPI.DTOs;

public record CreateUserRequest(
    string Name,
    string Email,
    string? Phone,
    string Password,
    string[] Roles,
    string[] Permissions,
    int[] AssignedVehicleIds
);

public record UpdateUserRequest(
    string Name,
    string Email,
    string? Phone,
    string[] Roles,
    string[] Permissions,
    int[] AssignedVehicleIds,
    string Status
);

public record UserListDto(
    int Id,
    string Name,
    string Email,
    string? Phone,
    string[] Roles,
    string[] Permissions,
    int[] AssignedVehicleIds,
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
