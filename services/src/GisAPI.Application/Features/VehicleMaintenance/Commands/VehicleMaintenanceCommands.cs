using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.VehicleMaintenance.Commands;

public record AssignMaintenanceTemplateCommand(
    int VehicleId,
    int TemplateId
) : ICommand<int>;

public record RemoveMaintenanceScheduleCommand(int ScheduleId) : ICommand<bool>;

/// <summary>
/// Calypso 7 (P-maint-ter): re-anchors <c>NextDueKm</c> on a schedule whose
/// snapshot was taken with a stale or absent mileage source. Recomputes
/// <c>NextDueKm = currentMileage + intervalKm</c> using the smart mileage
/// resolver, refreshes <c>Status</c> in the same pass.
///
/// <para>Used when the operator notices that a schedule was created on a
/// vehicle whose tracker had no odometer (so <c>vehicle.Mileage</c> was 0
/// at assignment) and the existing <c>NextDueKm</c> is therefore wrong.
/// One-shot rebase ; no log entry is created (this is not a maintenance
/// event, just a correction).</para>
/// </summary>
public record RebaseMaintenanceScheduleCommand(int ScheduleId) : ICommand<bool>;

public record MarkMaintenanceDoneCommand(
    int VehicleId,
    int TemplateId,
    DateTime Date,
    int Mileage,
    decimal Cost,
    int? SupplierId,
    string? Notes,
    string? TechnicianName = null,
    string? WorkOrderNumber = null,
    decimal? LaborCost = null,
    decimal? PartsCost = null,
    List<ReplacedPartDto>? PartsReplaced = null,
    int? QualityRating = null,
    bool ApplyFreeBenefit = true
) : ICommand<int>;

public record DeclareFreeMaintenancesCommand(
    int VehicleId,
    int TemplateId,
    int Count,
    string? Source,
    DateTime? ExpiryDate,
    string? Notes
) : ICommand<int>;

public record UpdateFreeMaintenanceCommand(
    int ScheduleId,
    int FreeUsesTotal,
    int FreeUsesRemaining,
    string? Source,
    DateTime? ExpiryDate,
    string? Notes
) : ICommand<bool>;

public record ClearFreeMaintenanceCommand(int ScheduleId) : ICommand<bool>;

public record PauseMaintenanceScheduleCommand(
    int ScheduleId,
    string? Reason
) : ICommand<bool>;

public record ResumeMaintenanceScheduleCommand(int ScheduleId) : ICommand<bool>;

public record UpdateScheduleIntervalsCommand(
    int ScheduleId,
    int? CustomIntervalKm,
    int? CustomIntervalMonths,
    string? Notes
) : ICommand<bool>;

public record AddTemplatePartCommand(
    int TemplateId,
    string PartName,
    string? PartNumber,
    int Quantity,
    decimal? EstimatedUnitCost,
    bool IsRequired,
    int? PreferredSupplierId
) : ICommand<int>;

public record RemoveTemplatePartCommand(int PartId) : ICommand<bool>;

public record AcknowledgeMaintenanceNotificationCommand(int NotificationId) : ICommand<bool>;

public record ReplacedPartDto(string Name, string? PartNumber, int Quantity, decimal UnitCost);



