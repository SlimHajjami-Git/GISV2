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
    string FirstName,
    string LastName,
    string Email,
    string? Phone,
    int RoleId,
    string RoleName,
    bool IsCompanyAdmin,
    bool IsSystemAdmin,
    int CompanyId,
    string CompanyName,
    Dictionary<string, object>? Permissions,
    SubscriptionFeaturesDto? SubscriptionFeatures
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

public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword
);
