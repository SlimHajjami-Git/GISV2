using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;

namespace GisAPI.Application.Features.MaintenanceTemplates.Queries;

public record GetMaintenanceTemplatesQuery(
    string? Category = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 50
) : IQuery<PaginatedList<MaintenanceTemplateDto>>;

public record MaintenanceTemplateDto(
    int Id,
    string Name,
    string? Description,
    string Category,
    string Priority,
    int? IntervalKm,
    int? IntervalMonths,
    decimal? EstimatedCost,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int WarningKm = 1000,
    int WarningDays = 30,
    int CriticalKm = 0,
    int CriticalDays = 0
);

public static class MaintenanceCategories
{
    public static readonly string[] All = { "Moteur", "Freinage", "Transmission", "Filtres", "Électrique", "Suspension", "Carrosserie", "Autre" };
}



