namespace GisAPI.Application.Features.Admin.SubscriptionTypes.Queries.GetSubscriptionTypes;

public class SubscriptionTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TargetCompanyType { get; set; } = "all";
    public decimal MonthlyPrice { get; set; }
    public decimal QuarterlyPrice { get; set; }
    // Cycle semestriel (6 mois) : un prix à 0 signifie « non vendable pour ce
    // plan » — la commande refuse un montant nul.
    public decimal SemiannualPrice { get; set; }
    public decimal YearlyPrice { get; set; }
    public int MonthlyDurationDays { get; set; }
    public int QuarterlyDurationDays { get; set; }
    public int SemiannualDurationDays { get; set; }
    public int YearlyDurationDays { get; set; }
    public int MaxVehicles { get; set; }
    public int MaxUsers { get; set; }
    public int MaxGpsDevices { get; set; }
    public int MaxGeofences { get; set; }
    public bool GpsTracking { get; set; }
    public bool GpsInstallation { get; set; }
    public bool ApiAccess { get; set; }
    public bool AdvancedReports { get; set; }
    public bool RealTimeAlerts { get; set; }
    public bool HistoryPlayback { get; set; }
    public bool FuelAnalysis { get; set; }
    public bool DrivingBehavior { get; set; }
    public int HistoryRetentionDays { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    
    // Module permissions
    public bool ModuleDashboard { get; set; }
    public bool ModuleMonitoring { get; set; }
    public bool ModuleVehicles { get; set; }
    public bool ModuleEmployees { get; set; }
    public bool ModuleGeofences { get; set; }
    public bool ModuleMaintenance { get; set; }
    public bool ModuleCosts { get; set; }
    public bool ModuleReports { get; set; }
    // Absents de l'API jusqu'ici alors qu'ils existent en base et valent true
    // par défaut : impossible donc d'éteindre les Tournées sur un plan « sans
    // GPS », ni de piloter le module Carburant.
    public bool ModuleFuel { get; set; }
    public bool ModuleTours { get; set; }
    public bool ModuleSettings { get; set; }
    public bool ModuleUsers { get; set; }
    public bool ModuleSuppliers { get; set; }
    public bool ModuleDocuments { get; set; }
    public bool ModuleAccidents { get; set; }
    public bool ModuleFleetManagement { get; set; }
    
    // Report permissions
    public bool ReportTrips { get; set; }
    public bool ReportFuel { get; set; }
    public bool ReportSpeed { get; set; }
    public bool ReportStops { get; set; }
    public bool ReportMileage { get; set; }
    public bool ReportCosts { get; set; }
    public bool ReportMaintenance { get; set; }
    public bool ReportDaily { get; set; }
    public bool ReportMonthly { get; set; }
    public bool ReportMileagePeriod { get; set; }
    public bool ReportSpeedInfraction { get; set; }
    public bool ReportDrivingBehavior { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}



