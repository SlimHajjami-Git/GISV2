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

    // Navigation
    public ICollection<VehicleMaintenanceSchedule> Schedules { get; set; } = new List<VehicleMaintenanceSchedule>();
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
    public string Status { get; set; } = "upcoming"; // ok, upcoming, due, overdue
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
    
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


