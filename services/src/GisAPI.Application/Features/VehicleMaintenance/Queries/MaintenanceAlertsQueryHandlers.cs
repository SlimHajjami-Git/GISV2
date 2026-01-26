using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.VehicleMaintenance.Queries;

public class GetMaintenanceAlertsQueryHandler : IRequestHandler<GetMaintenanceAlertsQuery, List<MaintenanceItemDto>>
{
    private readonly IGisDbContext _context;

    public GetMaintenanceAlertsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<MaintenanceItemDto>> Handle(GetMaintenanceAlertsQuery request, CancellationToken cancellationToken)
    {
        var schedules = await _context.VehicleMaintenanceSchedules
            .Include(s => s.Template)
            .Include(s => s.Vehicle)
            .Where(s => s.Status == "overdue" || s.Status == "due")
            .ToListAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;

        return schedules.Select(s =>
        {
            var kmUntilDue = s.NextDueKm.HasValue && s.Vehicle != null 
                ? s.NextDueKm.Value - s.Vehicle.Mileage 
                : (int?)null;
            var daysUntilDue = s.NextDueDate.HasValue 
                ? (int)(s.NextDueDate.Value - today).TotalDays 
                : (int?)null;

            return new MaintenanceItemDto(
                s.Id,
                s.TemplateId,
                s.Template?.Name ?? "",
                s.Template?.Category ?? "",
                s.Template?.Priority ?? "medium",
                s.LastDoneDate,
                s.LastDoneKm,
                s.NextDueDate,
                s.NextDueKm,
                s.Status,
                kmUntilDue,
                daysUntilDue
            );
        })
        .OrderBy(i => i.Status == "overdue" ? 0 : 1)
        .ThenBy(i => i.DaysUntilDue ?? int.MaxValue)
        .ToList();
    }
}

public class GetMaintenanceStatsQueryHandler : IRequestHandler<GetMaintenanceStatsQuery, MaintenanceStatsDto>
{
    private readonly IGisDbContext _context;

    public GetMaintenanceStatsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<MaintenanceStatsDto> Handle(GetMaintenanceStatsQuery request, CancellationToken cancellationToken)
    {
        var schedules = await _context.VehicleMaintenanceSchedules.ToListAsync(cancellationToken);

        return new MaintenanceStatsDto(
            schedules.Count,
            schedules.Count(s => s.Status == "overdue"),
            schedules.Count(s => s.Status == "due"),
            schedules.Count(s => s.Status == "upcoming"),
            schedules.Count(s => s.Status == "ok")
        );
    }
}
