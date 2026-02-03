using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.VehicleMaintenance.Queries;

public record GetMaintenanceAlertsQuery() : IQuery<List<MaintenanceItemDto>>;

public record GetMaintenanceStatsQuery() : IQuery<MaintenanceStatsDto>;

public record GetVehicleMaintenanceSchedulesQuery(int VehicleId) : IQuery<List<VehicleScheduleDto>>;

public record GetMaintenanceNotificationsQuery(bool UnacknowledgedOnly = true) : IQuery<List<MaintenanceNotificationDto>>;

public record GetTemplatePartsQuery(int TemplateId) : IQuery<List<TemplatePartDto>>;

public record GetCurrentVehicleMileageQuery(int VehicleId) : IQuery<VehicleMileageDto>;

// DTOs
public record VehicleScheduleDto(
    int Id,
    int TemplateId,
    string TemplateName,
    string Category,
    string Priority,
    DateTime? LastDoneDate,
    int? LastDoneKm,
    DateTime? NextDueDate,
    int? NextDueKm,
    string Status,
    bool IsPaused,
    string? PausedReason,
    int? CustomIntervalKm,
    int? CustomIntervalMonths,
    int? KmRemaining,
    int? DaysRemaining,
    decimal? EstimatedCost
);

public record MaintenanceNotificationDto(
    long Id,
    int ScheduleId,
    int VehicleId,
    string VehicleName,
    string? VehiclePlate,
    string MaintenanceName,
    string NotificationType,
    string TriggerReason,
    int? KmRemaining,
    int? DaysRemaining,
    DateTime CreatedAt,
    DateTime? AcknowledgedAt
);

public record TemplatePartDto(
    int Id,
    string PartName,
    string? PartNumber,
    int Quantity,
    string Unit,
    decimal? EstimatedUnitCost,
    bool IsRequired,
    int? PreferredSupplierId,
    string? SupplierName
);

public record VehicleMileageDto(
    int VehicleId,
    int CurrentKm,
    string Source,
    DateTime? LastGpsUpdate
);



