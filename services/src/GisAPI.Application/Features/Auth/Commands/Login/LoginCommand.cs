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
    bool IsSystemAdmin,
    int CompanyId,
    string CompanyName,
    string? CompanyType,
    Dictionary<string, object>? Permissions,
    SubscriptionFeaturesDto? SubscriptionFeatures,
    int[]? AssignedVehicleIds = null
);

public record SubscriptionFeaturesDto(
    // Core features
    bool GpsTracking,
    bool GpsInstallation,
    bool ApiAccess,
    bool AdvancedReports,
    bool RealTimeAlerts,
    bool HistoryPlayback,
    bool FuelAnalysis,
    bool DrivingBehavior,
    // Module access
    bool ModuleDashboard,
    bool ModuleMonitoring,
    bool ModuleVehicles,
    bool ModuleEmployees,
    bool ModuleGeofences,
    bool ModuleMaintenance,
    bool ModuleCosts,
    bool ModuleReports,
    bool ModuleSettings,
    bool ModuleUsers,
    bool ModuleSuppliers,
    bool ModuleDocuments,
    bool ModuleAccidents,
    bool ModuleFleetManagement,
    // Limits
    int MaxVehicles,
    int MaxUsers,
    int MaxGpsDevices,
    int MaxGeofences,
    int HistoryRetentionDays
);



