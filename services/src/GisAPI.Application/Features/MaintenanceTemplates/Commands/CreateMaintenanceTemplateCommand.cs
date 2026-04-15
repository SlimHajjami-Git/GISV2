using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.MaintenanceTemplates.Commands;

public record CreateMaintenanceTemplateCommand(
    string Name,
    string? Description,
    string Category,
    string Priority,
    int? IntervalKm,
    int? IntervalMonths,
    decimal? EstimatedCost,
    bool IsActive = true,
    int? WarningKm = null,
    int? WarningDays = null,
    int? CriticalKm = null,
    int? CriticalDays = null
) : ICommand<int>;

public record UpdateMaintenanceTemplateCommand(
    int Id,
    string? Name,
    string? Description,
    string? Category,
    string? Priority,
    int? IntervalKm,
    int? IntervalMonths,
    decimal? EstimatedCost,
    bool? IsActive,
    int? WarningKm = null,
    int? WarningDays = null,
    int? CriticalKm = null,
    int? CriticalDays = null
) : ICommand<bool>;

public record DeleteMaintenanceTemplateCommand(int Id) : ICommand<bool>;



