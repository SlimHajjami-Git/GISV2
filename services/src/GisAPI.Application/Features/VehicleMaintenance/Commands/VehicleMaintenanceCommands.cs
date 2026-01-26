using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.VehicleMaintenance.Commands;

public record AssignMaintenanceTemplateCommand(
    int VehicleId,
    int TemplateId
) : ICommand<int>;

public record RemoveMaintenanceScheduleCommand(int ScheduleId) : ICommand<bool>;

public record MarkMaintenanceDoneCommand(
    int VehicleId,
    int TemplateId,
    DateTime Date,
    int Mileage,
    decimal Cost,
    int? SupplierId,
    string? Notes
) : ICommand<int>;
