namespace GisAPI.DTOs;

// UserListDto is now in GisAPI.Application.Features.Users namespace

public record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Password,
    int RoleId,
    int[]? AssignedVehicleIds = null
);

public record UpdateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    int? RoleId,
    string? Status,
    int[]? AssignedVehicleIds = null
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
