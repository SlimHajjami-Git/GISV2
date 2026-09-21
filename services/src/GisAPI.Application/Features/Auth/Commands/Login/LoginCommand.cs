using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Auth.Commands.Login;

/// <param name="ClientType">Appelant déclaré : <see cref="LoginClients.Mobile"/> pour l'application, null pour le site.</param>
public record LoginCommand(string Email, string Password, string? IpAddress = null, string? UserAgent = null, string? ClientType = null) : ICommand<LoginResponse>;

/// <summary>
/// Appelants de la connexion. Un compte chauffeur (users.account_type = driver, migration
/// 050) n'obtient un jeton QUE depuis l'application mobile, qui se déclare par l'en-tête
/// X-Calypso-Client ou le champ « client » ; le site, lui, ne se déclare jamais.
/// </summary>
public static class LoginClients
{
    public const string Mobile = "mobile";

    public static bool IsMobile(string? clientType) =>
        string.Equals(clientType?.Trim(), Mobile, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Refus affiché sur le site ET dans une application trop ancienne pour se déclarer
    /// (jusqu'à la 1.1.1, qui n'envoie ni l'en-tête ni le champ « client ») : le chauffeur
    /// doit savoir qu'il lui faut la 1.2 ou plus récente, pas seulement « l'application ».
    /// Même texte côté web (DRIVER_WEB_LOGIN_REFUSED, auth.service.ts), qui repère le refus
    /// à son début : « Ce compte est réservé à l'application mobile ».
    /// </summary>
    public const string DriverWebLoginRefused =
        "Ce compte est réservé à l'application mobile Calypso, version 1.2 ou plus récente : installez-la ou mettez-la à jour sur votre téléphone pour vous connecter.";
}

/// <summary>
/// Durée des jetons de rafraîchissement. Un chauffeur ne saisit pas son mot de passe
/// tous les sept jours sur un téléphone de service : 90 jours glissants pour lui, la
/// désactivation du compte (relue à chaque appel par PermissionMiddleware et au
/// rafraîchissement) restant le vrai levier de révocation.
/// </summary>
public static class RefreshTokenLifetime
{
    public const int StaffDays = 7;
    public const int DriverDays = 90;

    public static int DaysFor(GisAPI.Domain.Entities.User user) => user.IsDriverAccount ? DriverDays : StaffDays;
}

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
)
{
    /// <summary>
    /// « staff » ou « driver » (migration 050). L'application mobile aiguille dessus :
    /// un chauffeur n'ouvre que « Mes tournées ». Propriété init : ce DTO est construit
    /// à plusieurs endroits, un paramètre positionnel de plus les casserait tous.
    /// </summary>
    public string AccountType { get; init; } = GisAPI.Domain.Entities.UserAccountTypes.Staff;
}

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
    bool CanReportMonthlyCosts = true,
    // Recette du 11/09/2026 : une case par rapport (migration 046). Paramètres en
    // FIN de liste avec défaut : chaque constructeur positionnel les passe quand même.
    bool CanReportOperatingCost = true,
    bool CanReportCostEvolution = true,
    bool CanReportCostRanking = true,
    bool CanReportRepairFrequency = true,
    bool CanReportMonthlyFuel = true,
    bool CanReportAiFleet = true,
    bool CanReportFuelEstimation = true,
    bool CanReportFuelComparison = true
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



