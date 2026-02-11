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
        var schedules = await _context.VehicleMaintenanceSchedules
            .Where(s => !s.IsPaused)
            .ToListAsync(cancellationToken);

        return new MaintenanceStatsDto(
            schedules.Count,
            schedules.Count(s => s.Status == "overdue"),
            schedules.Count(s => s.Status == "due" || s.Status == "critical"),
            schedules.Count(s => s.Status == "upcoming"),
            schedules.Count(s => s.Status == "ok")
        );
    }
}

public class GetVehicleMaintenanceSchedulesQueryHandler : IRequestHandler<GetVehicleMaintenanceSchedulesQuery, List<VehicleScheduleDto>>
{
    private readonly IGisDbContext _context;
    private readonly GisAPI.Application.Services.IMaintenanceSchedulerService _scheduler;

    public GetVehicleMaintenanceSchedulesQueryHandler(IGisDbContext context, GisAPI.Application.Services.IMaintenanceSchedulerService scheduler)
    {
        _context = context;
        _scheduler = scheduler;
    }

    public async Task<List<VehicleScheduleDto>> Handle(GetVehicleMaintenanceSchedulesQuery request, CancellationToken cancellationToken)
    {
        var currentKm = await _scheduler.GetCurrentMileageAsync(request.VehicleId, cancellationToken);
        var today = DateTime.UtcNow.Date;

        var schedules = await _context.VehicleMaintenanceSchedules
            .Include(s => s.Template)
            .Where(s => s.VehicleId == request.VehicleId && s.Template!.IsActive)
            .OrderBy(s => s.Status == "overdue" ? 0 : s.Status == "critical" ? 1 : s.Status == "due" ? 2 : 3)
            .ThenBy(s => s.NextDueKm)
            .ToListAsync(cancellationToken);

        return schedules.Select(s => new VehicleScheduleDto(
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
            s.IsPaused,
            s.PausedReason,
            s.CustomIntervalKm,
            s.CustomIntervalMonths,
            s.NextDueKm.HasValue ? s.NextDueKm.Value - currentKm : null,
            s.NextDueDate.HasValue ? (int)(s.NextDueDate.Value - today).TotalDays : null,
            s.Template?.EstimatedCost
        )).ToList();
    }
}

public class GetMaintenanceNotificationsQueryHandler : IRequestHandler<GetMaintenanceNotificationsQuery, List<MaintenanceNotificationDto>>
{
    private readonly IGisDbContext _context;

    public GetMaintenanceNotificationsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<MaintenanceNotificationDto>> Handle(GetMaintenanceNotificationsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.MaintenanceNotifications
            .Include(n => n.Vehicle)
            .Include(n => n.Template)
            .AsQueryable();

        if (request.UnacknowledgedOnly)
        {
            query = query.Where(n => n.AcknowledgedAt == null);
        }

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return notifications.Select(n => new MaintenanceNotificationDto(
            n.Id,
            n.ScheduleId,
            n.VehicleId,
            n.Vehicle?.Name ?? "",
            n.Vehicle?.Plate,
            n.Template?.Name ?? "",
            n.NotificationType,
            n.TriggerReason,
            n.KmRemaining,
            n.DaysRemaining,
            n.CreatedAt,
            n.AcknowledgedAt
        )).ToList();
    }
}

public class GetTemplatePartsQueryHandler : IRequestHandler<GetTemplatePartsQuery, List<TemplatePartDto>>
{
    private readonly IGisDbContext _context;

    public GetTemplatePartsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<TemplatePartDto>> Handle(GetTemplatePartsQuery request, CancellationToken cancellationToken)
    {
        var parts = await _context.MaintenanceTemplateParts
            .Include(p => p.PreferredSupplier)
            .Where(p => p.TemplateId == request.TemplateId)
            .OrderBy(p => p.PartName)
            .ToListAsync(cancellationToken);

        return parts.Select(p => new TemplatePartDto(
            p.Id,
            p.PartName,
            p.PartNumber,
            p.Quantity,
            p.Unit,
            p.EstimatedUnitCost,
            p.IsRequired,
            p.PreferredSupplierId,
            p.PreferredSupplier?.Name
        )).ToList();
    }
}

public class GetMaintenanceLogsQueryHandler : IRequestHandler<GetMaintenanceLogsQuery, List<MaintenanceLogDto>>
{
    private readonly IGisDbContext _context;

    public GetMaintenanceLogsQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<List<MaintenanceLogDto>> Handle(GetMaintenanceLogsQuery request, CancellationToken cancellationToken)
    {
        var query = _context.MaintenanceLogs
            .Include(l => l.Template)
            .Include(l => l.Supplier)
            .Where(l => l.VehicleId == request.VehicleId);

        if (request.TemplateId.HasValue)
            query = query.Where(l => l.TemplateId == request.TemplateId.Value);

        var logs = await query
            .OrderByDescending(l => l.DoneDate)
            .Take(50)
            .ToListAsync(cancellationToken);

        return logs.Select(l => new MaintenanceLogDto(
            l.Id,
            l.VehicleId,
            l.TemplateId,
            l.Template?.Name ?? "",
            l.DoneDate,
            l.DoneKm,
            l.ActualCost,
            l.Supplier?.Name,
            l.Notes
        )).ToList();
    }
}

public class GetCurrentVehicleMileageQueryHandler : IRequestHandler<GetCurrentVehicleMileageQuery, VehicleMileageDto>
{
    private readonly IGisDbContext _context;
    private readonly GisAPI.Application.Services.IMaintenanceSchedulerService _scheduler;

    public GetCurrentVehicleMileageQueryHandler(IGisDbContext context, GisAPI.Application.Services.IMaintenanceSchedulerService scheduler)
    {
        _context = context;
        _scheduler = scheduler;
    }

    public async Task<VehicleMileageDto> Handle(GetCurrentVehicleMileageQuery request, CancellationToken cancellationToken)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

        if (vehicle == null)
            return new VehicleMileageDto(request.VehicleId, 0, "unknown", null);

        var currentKm = await _scheduler.GetCurrentMileageAsync(request.VehicleId, cancellationToken);
        
        DateTime? lastGpsUpdate = null;
        string source = "manual";

        if (vehicle.GpsDeviceId.HasValue)
        {
            var lastPosition = await _context.GpsPositions
                .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value && p.OdometerKm.HasValue && p.OdometerKm > 0)
                .OrderByDescending(p => p.RecordedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (lastPosition != null && lastPosition.OdometerKm > vehicle.Mileage)
            {
                source = "gps";
                lastGpsUpdate = lastPosition.RecordedAt;
            }
        }

        return new VehicleMileageDto(request.VehicleId, currentKm, source, lastGpsUpdate);
    }
}



