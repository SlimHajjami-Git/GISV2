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
    public decimal YearlyPrice { get; set; }
    public int MonthlyDurationDays { get; set; }
    public int QuarterlyDurationDays { get; set; }
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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
