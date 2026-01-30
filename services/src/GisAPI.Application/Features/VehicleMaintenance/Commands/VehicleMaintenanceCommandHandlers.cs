using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.VehicleMaintenance.Commands;

public class AssignMaintenanceTemplateCommandHandler : IRequestHandler<AssignMaintenanceTemplateCommand, int>
{
    private readonly IGisDbContext _context;

    public AssignMaintenanceTemplateCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<int> Handle(AssignMaintenanceTemplateCommand request, CancellationToken cancellationToken)
    {
        // Check if already assigned
        var existing = await _context.VehicleMaintenanceSchedules
            .FirstOrDefaultAsync(s => s.VehicleId == request.VehicleId && s.TemplateId == request.TemplateId, cancellationToken);

        if (existing != null)
            return existing.Id;

        var template = await _context.MaintenanceTemplates
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, cancellationToken);

        if (template == null)
            throw new InvalidOperationException($"Template not found: {request.TemplateId}");

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

        if (vehicle == null)
            throw new InvalidOperationException($"Vehicle not found: {request.VehicleId}");

        // Calculate next due based on current mileage and date
        var schedule = new VehicleMaintenanceSchedule
        {
            VehicleId = request.VehicleId,
            TemplateId = request.TemplateId,
            NextDueKm = template.IntervalKm.HasValue ? vehicle.Mileage + template.IntervalKm.Value : null,
            NextDueDate = template.IntervalMonths.HasValue ? DateTime.UtcNow.AddMonths(template.IntervalMonths.Value) : null,
            Status = "upcoming"
        };

        _context.VehicleMaintenanceSchedules.Add(schedule);
        await _context.SaveChangesAsync(cancellationToken);

        return schedule.Id;
    }
}

public class RemoveMaintenanceScheduleCommandHandler : IRequestHandler<RemoveMaintenanceScheduleCommand, bool>
{
    private readonly IGisDbContext _context;

    public RemoveMaintenanceScheduleCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(RemoveMaintenanceScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await _context.VehicleMaintenanceSchedules
            .FirstOrDefaultAsync(s => s.Id == request.ScheduleId, cancellationToken);

        if (schedule == null) return false;

        _context.VehicleMaintenanceSchedules.Remove(schedule);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}

public class MarkMaintenanceDoneCommandHandler : IRequestHandler<MarkMaintenanceDoneCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public MarkMaintenanceDoneCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<int> Handle(MarkMaintenanceDoneCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var template = await _context.MaintenanceTemplates
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, cancellationToken);

        if (template == null)
            throw new InvalidOperationException($"Template not found: {request.TemplateId}");

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

        if (vehicle == null)
            throw new InvalidOperationException($"Vehicle not found: {request.VehicleId}");

        // Create VehicleCost
        var cost = new VehicleCost
        {
            VehicleId = request.VehicleId,
            Type = "maintenance",
            Description = $"Entretien: {template.Name}",
            Amount = request.Cost,
            Date = request.Date,
            Mileage = request.Mileage,
            CompanyId = companyId
        };
        _context.VehicleCosts.Add(cost);
        await _context.SaveChangesAsync(cancellationToken);

        // Get or create schedule
        var schedule = await _context.VehicleMaintenanceSchedules
            .FirstOrDefaultAsync(s => s.VehicleId == request.VehicleId && s.TemplateId == request.TemplateId, cancellationToken);

        if (schedule == null)
        {
            schedule = new VehicleMaintenanceSchedule
            {
                VehicleId = request.VehicleId,
                TemplateId = request.TemplateId
            };
            _context.VehicleMaintenanceSchedules.Add(schedule);
        }

        // Update schedule
        schedule.LastDoneDate = request.Date;
        schedule.LastDoneKm = request.Mileage;
        schedule.NextDueKm = template.IntervalKm.HasValue ? request.Mileage + template.IntervalKm.Value : null;
        schedule.NextDueDate = template.IntervalMonths.HasValue ? request.Date.AddMonths(template.IntervalMonths.Value) : null;
        schedule.Status = CalculateStatus(schedule, vehicle.Mileage);
        schedule.UpdatedAt = DateTime.UtcNow;

        // Create MaintenanceLog
        var log = new MaintenanceLog
        {
            VehicleId = request.VehicleId,
            TemplateId = request.TemplateId,
            ScheduleId = schedule.Id,
            CostId = cost.Id,
            DoneDate = request.Date,
            DoneKm = request.Mileage,
            ActualCost = request.Cost,
            SupplierId = request.SupplierId,
            Notes = request.Notes
        };
        _context.MaintenanceLogs.Add(log);

        // Update vehicle mileage if higher
        if (request.Mileage > vehicle.Mileage)
        {
            vehicle.Mileage = request.Mileage;
            vehicle.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return log.Id;
    }

    private static string CalculateStatus(VehicleMaintenanceSchedule schedule, int currentMileage)
    {
        var today = DateTime.UtcNow.Date;

        // Check overdue
        if (schedule.NextDueKm.HasValue && currentMileage > schedule.NextDueKm.Value)
            return "overdue";
        if (schedule.NextDueDate.HasValue && today > schedule.NextDueDate.Value)
            return "overdue";

        // Check due (< 1000 km or < 30 days)
        if (schedule.NextDueKm.HasValue && schedule.NextDueKm.Value - currentMileage < 1000)
            return "due";
        if (schedule.NextDueDate.HasValue && (schedule.NextDueDate.Value - today).TotalDays < 30)
            return "due";

        // Check upcoming (< 5000 km or < 90 days)
        if (schedule.NextDueKm.HasValue && schedule.NextDueKm.Value - currentMileage < 5000)
            return "upcoming";
        if (schedule.NextDueDate.HasValue && (schedule.NextDueDate.Value - today).TotalDays < 90)
            return "upcoming";

        return "ok";
    }
}



