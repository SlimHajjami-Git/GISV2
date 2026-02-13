using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Users.Commands.CreateUser;

public record CreateUserCommand(
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
) : ICommand<UserListDto>;
