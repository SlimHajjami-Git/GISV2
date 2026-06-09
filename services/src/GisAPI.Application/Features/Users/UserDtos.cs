using GisAPI.Application.Features.Auth.Commands.Login;

namespace GisAPI.Application.Features.Users;

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
    DateTime? LastLoginAt,
    int[]? AssignedVehicleIds = null,
    UserPermissionsDto? UserPermissions = null,
    bool AlertAssurance = false,
    bool AlertTaxeCirculation = false,
    bool AlertVisiteTechnique = false,
    bool AlertEntretien = false,
    bool DailyReportEmailEnabled = false
);
