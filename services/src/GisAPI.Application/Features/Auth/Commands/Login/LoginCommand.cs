using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Auth.Commands.Login;

public record LoginCommand(string Email, string Password, string? IpAddress = null, string? UserAgent = null) : ICommand<LoginResponse>;

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
    int[]? AssignedVehicleIds = null,
    UserPermissionsDto? UserPermissions = null,
    string Currency = "TND",
    // Cette société gère-t-elle son abonnement elle-même ?
    //
    // Vrai pour les comptes en ESSAI (jamais réglé) et pour l'offre de gestion de
    // parc en libre-service. Faux pour les clients installés, dont l'abonnement
    // est négocié et facturé à la main : leur montrer un écran de paiement en
    // ligne — qui plus est inactif — n'a aucun sens et prête à confusion.
    //
    // La règle est calculée ICI, côté serveur, et pas déduite dans l'écran : elle
    // doit rester unique et vérifiable.
    bool SelfServiceSubscription = false
);

public record UserPermissionsDto(
    string AccessLevel,
    bool CanMonitoring,
    bool CanVehicles,
    bool CanDrivers,
    bool CanReports,
    bool CanGeofences,
    bool CanMaintenance,
    bool CanCosts,
    bool CanFuel,
    bool CanDocuments,
    bool CanAccidents,
    bool CanUsers,
    bool CanSettings,
    bool CanSuppliers,
    bool CanFleetManagement,
    bool CanTours,
    bool CanPlayback,
    // Per-report permissions
    bool CanReportTrips = true,
    bool CanReportFuel = true,
    bool CanReportSpeed = true,
    bool CanReportStops = true,
    bool CanReportMileage = true,
    bool CanReportCosts = true,
    bool CanReportMaintenance = true,
    bool CanReportDaily = true,
    bool CanReportMonthly = true,
    bool CanReportMileagePeriod = true,
    bool CanReportSpeedInfraction = true,
    bool CanReportDrivingBehavior = true,
    bool CanReportMonthlyCosts = true
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
    bool ModuleFuel,
    bool ModuleReports,
    bool ModuleSettings,
    bool ModuleUsers,
    bool ModuleSuppliers,
    bool ModuleDocuments,
    bool ModuleAccidents,
    bool ModuleFleetManagement,
    bool ModuleTours,
    // Report permissions
    bool ReportTrips,
    bool ReportFuel,
    bool ReportSpeed,
    bool ReportStops,
    bool ReportMileage,
    bool ReportCosts,
    bool ReportMaintenance,
    bool ReportDaily,
    bool ReportMonthly,
    bool ReportMileagePeriod,
    bool ReportSpeedInfraction,
    bool ReportDrivingBehavior,
    bool ReportMonthlyCosts,
    // Limits
    int MaxVehicles,
    int MaxUsers,
    int MaxGpsDevices,
    int MaxGeofences,
    int HistoryRetentionDays
);



