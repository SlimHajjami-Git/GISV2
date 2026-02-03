using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class MaintenanceTemplate : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = string.Empty; // Moteur, Freinage, Transmission, Filtres, Électrique, Suspension
    public string Priority { get; set; } = "medium"; // low, medium, high, critical
    public int? IntervalKm { get; set; }
    public int? IntervalMonths { get; set; }
    public decimal? EstimatedCost { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Seuils d'alerte
    public int WarningKm { get; set; } = 1000;
    public int WarningDays { get; set; } = 30;
    public int CriticalKm { get; set; } = 0;
    public int CriticalDays { get; set; } = 0;
    
    // Détails supplémentaires
    public int? EstimatedDurationMinutes { get; set; }
    public string? Instructions { get; set; }
    public string[] AppliesToVehicleTypes { get; set; } = Array.Empty<string>();
    public string Icon { get; set; } = "wrench";

    // Navigation
    public ICollection<VehicleMaintenanceSchedule> Schedules { get; set; } = new List<VehicleMaintenanceSchedule>();
    public ICollection<MaintenanceTemplatePart> Parts { get; set; } = new List<MaintenanceTemplatePart>();
}

public class VehicleMaintenanceSchedule : AuditableEntity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    
    public int TemplateId { get; set; }
    public MaintenanceTemplate? Template { get; set; }
    
    public DateTime? LastDoneDate { get; set; }
    public int? LastDoneKm { get; set; }
    public DateTime? NextDueDate { get; set; }
    public int? NextDueKm { get; set; }
    public string Status { get; set; } = "upcoming"; // ok, upcoming, due, overdue, critical
    
    // Pause et personnalisation
    public bool IsPaused { get; set; } = false;
    public DateTime? PausedAt { get; set; }
    public string? PausedReason { get; set; }
    public int? CustomIntervalKm { get; set; }
    public int? CustomIntervalMonths { get; set; }
    
    // Notifications
    public DateTime? LastNotificationAt { get; set; }
    public int NotificationCount { get; set; } = 0;
    public string? Notes { get; set; }
    
    // Navigation
    public ICollection<MaintenanceNotification> Notifications { get; set; } = new List<MaintenanceNotification>();
}

public class MaintenanceLog : Entity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    
    public int TemplateId { get; set; }
    public MaintenanceTemplate? Template { get; set; }
    
    public int? ScheduleId { get; set; }
    public VehicleMaintenanceSchedule? Schedule { get; set; }
    
    public int? CostId { get; set; }
    public VehicleCost? Cost { get; set; }
    
    public DateTime DoneDate { get; set; }
    public int DoneKm { get; set; }
    public decimal ActualCost { get; set; }
    
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    
    // Détails intervention
    public string? TechnicianName { get; set; }
    public string? WorkOrderNumber { get; set; }
    public List<ReplacedPart>? PartsReplaced { get; set; }
    public decimal? LaborHours { get; set; }
    public decimal? LaborCost { get; set; }
    public decimal? PartsCost { get; set; }
    public int? QualityRating { get; set; }
    public string[]? Photos { get; set; }
    
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ReplacedPart
{
    public string Name { get; set; } = string.Empty;
    public string? PartNumber { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
}

public class MaintenanceTemplatePart : Entity
{
    public int TemplateId { get; set; }
    public MaintenanceTemplate? Template { get; set; }
    
    public string PartName { get; set; } = string.Empty;
    public string? PartNumber { get; set; }
    public int Quantity { get; set; } = 1;
    public string Unit { get; set; } = "unit";
    public decimal? EstimatedUnitCost { get; set; }
    public bool IsRequired { get; set; } = true;
    
    public int? PreferredSupplierId { get; set; }
    public Supplier? PreferredSupplier { get; set; }
    
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MaintenanceNotification : Entity
{
    public new long Id { get; set; }
    
    public int ScheduleId { get; set; }
    public VehicleMaintenanceSchedule? Schedule { get; set; }
    
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    
    public int TemplateId { get; set; }
    public MaintenanceTemplate? Template { get; set; }
    
    public int CompanyId { get; set; }
    
    public string NotificationType { get; set; } = "upcoming"; // upcoming, due, overdue, critical
    public string TriggerReason { get; set; } = "km"; // km, date, both
    
    public int? CurrentKm { get; set; }
    public int? KmRemaining { get; set; }
    public int? DaysRemaining { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public string[] SentChannels { get; set; } = Array.Empty<string>();
    public DateTime? AcknowledgedAt { get; set; }
    public int? AcknowledgedBy { get; set; }
    public DateTime? DismissedAt { get; set; }
}

public class MaintenanceAlertSettings : Entity
{
    public int CompanyId { get; set; }
    
    // Canaux de notification
    public bool EnablePush { get; set; } = true;
    public bool EnableEmail { get; set; } = true;
    public bool EnableSms { get; set; } = false;
    
    // Destinataires
    public bool NotifyDriver { get; set; } = true;
    public bool NotifySupervisor { get; set; } = true;
    public bool NotifyFleetManager { get; set; } = true;
    public string[] AdditionalEmails { get; set; } = Array.Empty<string>();
    public string[] AdditionalPhones { get; set; } = Array.Empty<string>();
    
    // Fréquence
    public int ReminderFrequencyDays { get; set; } = 7;
    public int MaxReminders { get; set; } = 3;
    
    // Heures calmes
    public TimeSpan? QuietHoursStart { get; set; }
    public TimeSpan? QuietHoursEnd { get; set; }
}


