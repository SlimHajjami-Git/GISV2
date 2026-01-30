using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;

namespace GisAPI.Application.Features.VehicleMaintenance.Queries;

public record GetVehicleMaintenanceQuery(
    int? VehicleId = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 100
) : IQuery<PaginatedList<VehicleMaintenanceStatusDto>>;

public record VehicleMaintenanceStatusDto(
    int VehicleId,
    string VehicleName,
    string? VehiclePlate,
    int CurrentMileage,
    List<MaintenanceItemDto> MaintenanceItems
);

public record MaintenanceItemDto(
    int ScheduleId,
    int TemplateId,
    string TemplateName,
    string Category,
    string Priority,
    DateTime? LastDoneDate,
    int? LastDoneKm,
    DateTime? NextDueDate,
    int? NextDueKm,
    string Status,
    int? KmUntilDue,
    int? DaysUntilDue
);

public record MaintenanceStatsDto(
    int TotalSchedules,
    int OverdueCount,
    int DueCount,
    int UpcomingCount,
    int OkCount
);



