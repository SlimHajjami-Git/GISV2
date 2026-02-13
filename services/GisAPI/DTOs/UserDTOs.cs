namespace GisAPI.DTOs;

// UserListDto is now in GisAPI.Application.Features.Users namespace

public record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    string Password,
    int RoleId,
    int[]? AssignedVehicleIds = null,
    string? EmployeeRole = null,
    string? PermitNumber = null,
    string? PermitType = null,
    DateTime? PermitExpiry = null,
    string? CIN = null,
    DateTime? DateOfBirth = null,
    DateTime? HireDate = null,
    string? AccessLevel = "user",
    bool CanMonitoring = true,
    bool CanVehicles = true,
    bool CanDrivers = false,
    bool CanReports = false,
    bool CanGeofences = false,
    bool CanMaintenance = false,
    bool CanCosts = false,
    bool CanDocuments = false,
    bool CanAccidents = false,
    bool CanUsers = false,
    bool CanSettings = false,
    bool CanSuppliers = false,
    bool CanFleetManagement = false
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
