using System.Text.Json.Serialization;
using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class SubscriptionType : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TargetCompanyType { get; set; } = "all";

    // Pricing
    public decimal MonthlyPrice { get; set; }
    public decimal QuarterlyPrice { get; set; }
    /// <summary>
    /// Prix du cycle SEMESTRIEL (6 mois). 0 = cycle non vendable pour ce plan :
    /// un tarif absent n'est pas un cadeau, la commande le refuse. Tarif
    /// commercial à part entière, jamais déduit de l'annuel (offre GPA :
    /// 4 €/véhicule/mois × 6 = 24, là où l'annuel descend à 3 €/mois).
    /// </summary>
    public decimal SemiannualPrice { get; set; }
    public decimal YearlyPrice { get; set; }

    /// <summary>
    /// Tarification PAR VÉHICULE : les prix des cycles ci-dessus s'entendent par
    /// véhicule, et le montant dû = prix du cycle × nombre de véhicules de la
    /// société (au moins 1). false = forfait classique. Porté par l'offre GPA
    /// européenne : 3 €/véhicule/mois en engagement annuel (recette 01/09/2026 —
    /// « 3 € × 2 véhicules × 12 mois = 72 € », pas un forfait figé).
    /// </summary>
    public bool PricePerVehicle { get; set; } = false;

    // Duration in days for each billing cycle
    public int MonthlyDurationDays { get; set; } = 30;
    public int QuarterlyDurationDays { get; set; } = 90;
    public int SemiannualDurationDays { get; set; } = 180;
    public int YearlyDurationDays { get; set; } = 365;

    // Limits
    public int MaxVehicles { get; set; } = 10;
    public int MaxUsers { get; set; } = 5;
    public int MaxGpsDevices { get; set; } = 10;
    public int MaxGeofences { get; set; } = 20;

    // Features
    public bool GpsTracking { get; set; }
    public bool GpsInstallation { get; set; }
    public bool ApiAccess { get; set; }
    public bool AdvancedReports { get; set; }
    public bool RealTimeAlerts { get; set; } = true;
    public bool HistoryPlayback { get; set; } = true;
    public bool FuelAnalysis { get; set; }
    public bool DrivingBehavior { get; set; }

    public int HistoryRetentionDays { get; set; } = 30;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Access rights for features (JSONB in PostgreSQL)
    public Dictionary<string, object>? AccessRights { get; set; }

    // Granular Report Permissions
    public bool ReportTrips { get; set; } = true;
    public bool ReportFuel { get; set; } = false;
    public bool ReportSpeed { get; set; } = true;
    public bool ReportStops { get; set; } = true;
    public bool ReportMileage { get; set; } = true;
    public bool ReportCosts { get; set; } = true;
    public bool ReportMaintenance { get; set; } = true;
    public bool ReportDaily { get; set; } = true;
    public bool ReportMonthly { get; set; } = false;
    public bool ReportMileagePeriod { get; set; } = false;
    public bool ReportSpeedInfraction { get; set; } = true;
    public bool ReportDrivingBehavior { get; set; } = false;
    public bool ReportMonthlyCosts { get; set; } = true;

    // Module Permissions
    public bool ModuleDashboard { get; set; } = true;
    public bool ModuleMonitoring { get; set; } = false;
    public bool ModuleVehicles { get; set; } = true;
    public bool ModuleEmployees { get; set; } = true;
    public bool ModuleGeofences { get; set; } = false;
    public bool ModuleMaintenance { get; set; } = true;
    public bool ModuleCosts { get; set; } = true;
    public bool ModuleFuel { get; set; } = true;
    public bool ModuleReports { get; set; } = true;
    public bool ModuleSettings { get; set; } = true;
    public bool ModuleUsers { get; set; } = true;
    public bool ModuleSuppliers { get; set; } = true;
    public bool ModuleDocuments { get; set; } = true;
    public bool ModuleAccidents { get; set; } = true;
    public bool ModuleFleetManagement { get; set; } = false;
    public bool ModuleTours { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [JsonIgnore]
    public ICollection<Societe> Societes { get; set; } = new List<Societe>();
}


