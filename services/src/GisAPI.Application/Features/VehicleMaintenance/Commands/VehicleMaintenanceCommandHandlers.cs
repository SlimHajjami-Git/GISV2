using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Services;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.VehicleMaintenance.Commands;

public class AssignMaintenanceTemplateCommandHandler : IRequestHandler<AssignMaintenanceTemplateCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly IMaintenanceSchedulerService _scheduler;

    public AssignMaintenanceTemplateCommandHandler(IGisDbContext context, IMaintenanceSchedulerService scheduler)
    {
        _context = context;
        _scheduler = scheduler;
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

        // Calypso 7 (P-maint-bis): use the smart mileage resolver instead of
        // raw vehicle.Mileage. The resolver cascades GPS odometer → manual
        // mileage → trips total, which is the correct snapshot when the
        // tracker has no FMS odometer wired (CAN bus disconnected).
        // Without this, NextDueKm was anchored on vehicle.Mileage = 0 and the
        // schedule was effectively dead-on-arrival.
        var currentMileage = await _scheduler.GetCurrentMileageAsync(request.VehicleId, cancellationToken);

        var schedule = new VehicleMaintenanceSchedule
        {
            VehicleId = request.VehicleId,
            TemplateId = request.TemplateId,
            NextDueKm = template.IntervalKm.HasValue ? currentMileage + template.IntervalKm.Value : null,
            NextDueDate = template.IntervalMonths.HasValue ? DateTime.UtcNow.AddMonths(template.IntervalMonths.Value) : null,
            Status = "upcoming"
        };

        _context.VehicleMaintenanceSchedules.Add(schedule);
        await _context.SaveChangesAsync(cancellationToken);

        return schedule.Id;
    }
}

/// <summary>
/// Calypso 7 (P-maint-ter): one-shot rebase for a schedule whose
/// <c>NextDueKm</c> was anchored on a stale mileage source (typically a
/// tracker with no FMS odometer that left <c>vehicle.Mileage = 0</c> at
/// assignment time). Reads the current mileage via the smart resolver
/// (GPS odometer → manual → trips total) and re-snaps <c>NextDueKm</c>
/// to <c>currentMileage + intervalKm</c>. Also refreshes the persisted
/// <c>Status</c> so the alerts pipeline immediately sees the correct
/// state on the next read.
///
/// <para>Idempotent: rebasing twice in a row is harmless if mileage
/// hasn't moved. Does NOT touch <c>LastDoneKm/Date</c> — this is a
/// correction, not a maintenance event.</para>
/// </summary>
public class RebaseMaintenanceScheduleCommandHandler : IRequestHandler<RebaseMaintenanceScheduleCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly IMaintenanceSchedulerService _scheduler;

    public RebaseMaintenanceScheduleCommandHandler(IGisDbContext context, IMaintenanceSchedulerService scheduler)
    {
        _context = context;
        _scheduler = scheduler;
    }

    public async Task<bool> Handle(RebaseMaintenanceScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await _context.VehicleMaintenanceSchedules
            .Include(s => s.Template)
            .FirstOrDefaultAsync(s => s.Id == request.ScheduleId, cancellationToken);

        if (schedule == null) return false;
        if (schedule.Template == null) return false;

        var currentMileage = await _scheduler.GetCurrentMileageAsync(schedule.VehicleId, cancellationToken);

        var intervalKm = schedule.CustomIntervalKm ?? schedule.Template.IntervalKm;

        // Anchor on the later of "current mileage" and the existing baseline,
        // so rebasing a schedule whose owner already drove past the original
        // anchor doesn't accidentally pull NextDueKm backwards.
        var anchor = Math.Max(currentMileage, schedule.LastDoneKm ?? 0);

        if (intervalKm.HasValue)
        {
            schedule.NextDueKm = anchor + intervalKm.Value;
        }

        // The date schedule is left untouched on rebase — date intervals are
        // typically fine, the bug we're patching is purely on the km axis.
        // Reset the notification dedup so the operator gets a fresh ping if
        // the rebased schedule is now in a "due" state.
        schedule.NotificationCount = 0;
        schedule.LastNotificationAt = null;
        schedule.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        // Recompute persisted Status against the new NextDueKm so the alerts
        // pipeline reflects reality without waiting for the background job.
        await _scheduler.UpdateVehicleScheduleStatusesAsync(schedule.VehicleId, cancellationToken);

        return true;
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

        // Get or create schedule FIRST (we need to know if a free benefit applies)
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

        // Free benefit consumption
        // A benefit is applied if: caller requested it, counter > 0, and (no expiry OR expiry is in future)
        var today = DateTime.UtcNow.Date;
        var benefitIsValid =
            schedule.FreeUsesRemaining > 0
            && (!schedule.FreeExpiryDate.HasValue || schedule.FreeExpiryDate.Value >= today);

        var applyBenefit = request.ApplyFreeBenefit && benefitIsValid;
        var actualCost = applyBenefit ? 0m : request.Cost;
        var actualLaborCost = applyBenefit ? (decimal?)0m : request.LaborCost;
        var actualPartsCost = applyBenefit ? (decimal?)0m : request.PartsCost;

        // Create VehicleCost (always, even if 0 — keeps history consistent)
        var cost = new VehicleCost
        {
            VehicleId = request.VehicleId,
            Type = "maintenance",
            Description = applyBenefit
                ? $"Entretien: {template.Name} (gratuit — {schedule.FreeSource ?? "avantage"})"
                : $"Entretien: {template.Name}",
            Amount = actualCost,
            Date = request.Date,
            Mileage = request.Mileage,
            CompanyId = companyId
        };
        _context.VehicleCosts.Add(cost);
        await _context.SaveChangesAsync(cancellationToken);

        // Update schedule
        schedule.LastDoneDate = request.Date;
        schedule.LastDoneKm = request.Mileage;
        schedule.NextDueKm = template.IntervalKm.HasValue ? request.Mileage + template.IntervalKm.Value : null;
        schedule.NextDueDate = template.IntervalMonths.HasValue ? request.Date.AddMonths(template.IntervalMonths.Value) : null;
        // Calypso 6 (P7.1): use the freshly bumped vehicle mileage AND the
        // already-loaded template (the schedule was fetched without Include
        // so schedule.Template was null inside CalculateStatus, falling back
        // to default warning thresholds — that made the new "next due" appear
        // already "imminent" right after marking done).
        var newMileage = request.Mileage > vehicle.Mileage ? request.Mileage : vehicle.Mileage;
        schedule.Status = CalculateStatus(schedule, newMileage, template);
        schedule.UpdatedAt = DateTime.UtcNow;

        // Decrement free uses counter if benefit was applied
        if (applyBenefit)
        {
            schedule.FreeUsesRemaining = Math.Max(0, schedule.FreeUsesRemaining - 1);
        }

        // Create MaintenanceLog
        var log = new MaintenanceLog
        {
            // MaintenanceLog n'implémente pas ITenantEntity : le stamping
            // automatique de SaveChangesAsync ne le renseigne pas, et la colonne
            // vaut donc 0 sur tout l'historique (constaté le 03/09/2026). On
            // l'écrit explicitement pour que les nouvelles lignes soient au moins
            // rattachables à leur société.
            CompanyId = companyId,
            VehicleId = request.VehicleId,
            TemplateId = request.TemplateId,
            ScheduleId = schedule.Id,
            CostId = cost.Id,
            DoneDate = request.Date,
            DoneKm = request.Mileage,
            ActualCost = actualCost,
            LaborCost = actualLaborCost,
            PartsCost = actualPartsCost,
            SupplierId = request.SupplierId,
            Notes = applyBenefit
                ? (string.IsNullOrWhiteSpace(request.Notes)
                    ? $"Gratuit — {schedule.FreeSource ?? "avantage entretien"}"
                    : $"{request.Notes} [Gratuit — {schedule.FreeSource ?? "avantage entretien"}]")
                : request.Notes,
            WasFree = applyBenefit
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

    private static string CalculateStatus(VehicleMaintenanceSchedule schedule, int currentMileage, MaintenanceTemplate? template = null)
    {
        var today = DateTime.UtcNow.Date;
        // Calypso 6 (P7.1): callers that loaded the template separately can pass
        // it in. The fallback to schedule.Template stays for queries that DID
        // include it (so older code keeps working).
        template ??= schedule.Template;

        var warningKm = template?.WarningKm ?? 1000;
        var warningDays = template?.WarningDays ?? 30;
        var criticalKm = template?.CriticalKm ?? 0;
        var criticalDays = template?.CriticalDays ?? 0;

        // Check overdue
        if (schedule.NextDueKm.HasValue && currentMileage > schedule.NextDueKm.Value)
            return "overdue";
        if (schedule.NextDueDate.HasValue && today > schedule.NextDueDate.Value)
            return "overdue";

        // Check critical
        if (criticalKm > 0 && schedule.NextDueKm.HasValue && schedule.NextDueKm.Value - currentMileage <= criticalKm)
            return "critical";
        if (criticalDays > 0 && schedule.NextDueDate.HasValue && (schedule.NextDueDate.Value - today).TotalDays <= criticalDays)
            return "critical";

        // Check due (warning threshold)
        if (schedule.NextDueKm.HasValue && schedule.NextDueKm.Value - currentMileage <= warningKm)
            return "due";
        if (schedule.NextDueDate.HasValue && (schedule.NextDueDate.Value - today).TotalDays <= warningDays)
            return "due";

        // Check upcoming (< 5000 km or < 90 days)
        if (schedule.NextDueKm.HasValue && schedule.NextDueKm.Value - currentMileage < 5000)
            return "upcoming";
        if (schedule.NextDueDate.HasValue && (schedule.NextDueDate.Value - today).TotalDays < 90)
            return "upcoming";

        return "ok";
    }
}

public class PauseMaintenanceScheduleCommandHandler : IRequestHandler<PauseMaintenanceScheduleCommand, bool>
{
    private readonly IGisDbContext _context;

    public PauseMaintenanceScheduleCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(PauseMaintenanceScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await _context.VehicleMaintenanceSchedules
            .FirstOrDefaultAsync(s => s.Id == request.ScheduleId, cancellationToken);

        if (schedule == null) return false;

        schedule.IsPaused = true;
        schedule.PausedAt = DateTime.UtcNow;
        schedule.PausedReason = request.Reason;
        schedule.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class ResumeMaintenanceScheduleCommandHandler : IRequestHandler<ResumeMaintenanceScheduleCommand, bool>
{
    private readonly IGisDbContext _context;

    public ResumeMaintenanceScheduleCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(ResumeMaintenanceScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await _context.VehicleMaintenanceSchedules
            .FirstOrDefaultAsync(s => s.Id == request.ScheduleId, cancellationToken);

        if (schedule == null) return false;

        schedule.IsPaused = false;
        schedule.PausedAt = null;
        schedule.PausedReason = null;
        schedule.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class UpdateScheduleIntervalsCommandHandler : IRequestHandler<UpdateScheduleIntervalsCommand, bool>
{
    private readonly IGisDbContext _context;

    public UpdateScheduleIntervalsCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(UpdateScheduleIntervalsCommand request, CancellationToken cancellationToken)
    {
        var schedule = await _context.VehicleMaintenanceSchedules
            .Include(s => s.Template)
            .FirstOrDefaultAsync(s => s.Id == request.ScheduleId, cancellationToken);

        if (schedule == null) return false;

        schedule.CustomIntervalKm = request.CustomIntervalKm;
        schedule.CustomIntervalMonths = request.CustomIntervalMonths;
        schedule.Notes = request.Notes;
        schedule.UpdatedAt = DateTime.UtcNow;

        // Recalculate next due based on custom intervals
        if (schedule.LastDoneDate.HasValue && schedule.LastDoneKm.HasValue)
        {
            var intervalKm = request.CustomIntervalKm ?? schedule.Template?.IntervalKm;
            var intervalMonths = request.CustomIntervalMonths ?? schedule.Template?.IntervalMonths;

            schedule.NextDueKm = intervalKm.HasValue ? schedule.LastDoneKm.Value + intervalKm.Value : null;
            schedule.NextDueDate = intervalMonths.HasValue ? schedule.LastDoneDate.Value.AddMonths(intervalMonths.Value) : null;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class AddTemplatePartCommandHandler : IRequestHandler<AddTemplatePartCommand, int>
{
    private readonly IGisDbContext _context;

    public AddTemplatePartCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<int> Handle(AddTemplatePartCommand request, CancellationToken cancellationToken)
    {
        var part = new MaintenanceTemplatePart
        {
            TemplateId = request.TemplateId,
            PartName = request.PartName,
            PartNumber = request.PartNumber,
            Quantity = request.Quantity,
            EstimatedUnitCost = request.EstimatedUnitCost,
            IsRequired = request.IsRequired,
            PreferredSupplierId = request.PreferredSupplierId
        };

        _context.MaintenanceTemplateParts.Add(part);
        await _context.SaveChangesAsync(cancellationToken);

        return part.Id;
    }
}

public class RemoveTemplatePartCommandHandler : IRequestHandler<RemoveTemplatePartCommand, bool>
{
    private readonly IGisDbContext _context;

    public RemoveTemplatePartCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(RemoveTemplatePartCommand request, CancellationToken cancellationToken)
    {
        var part = await _context.MaintenanceTemplateParts
            .FirstOrDefaultAsync(p => p.Id == request.PartId, cancellationToken);

        if (part == null) return false;

        _context.MaintenanceTemplateParts.Remove(part);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}

public class AcknowledgeMaintenanceNotificationCommandHandler : IRequestHandler<AcknowledgeMaintenanceNotificationCommand, bool>
{
    private readonly IGisDbContext _context;

    public AcknowledgeMaintenanceNotificationCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(AcknowledgeMaintenanceNotificationCommand request, CancellationToken cancellationToken)
    {
        var notification = await _context.MaintenanceNotifications
            .FirstOrDefaultAsync(n => n.Id == request.NotificationId, cancellationToken);

        if (notification == null) return false;

        notification.AcknowledgedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class DeclareFreeMaintenancesCommandHandler : IRequestHandler<DeclareFreeMaintenancesCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly IMaintenanceSchedulerService _scheduler;

    public DeclareFreeMaintenancesCommandHandler(IGisDbContext context, IMaintenanceSchedulerService scheduler)
    {
        _context = context;
        _scheduler = scheduler;
    }

    public async Task<int> Handle(DeclareFreeMaintenancesCommand request, CancellationToken cancellationToken)
    {
        if (request.Count <= 0)
            throw new InvalidOperationException("Le nombre d'entretiens gratuits doit être supérieur à zéro.");

        var template = await _context.MaintenanceTemplates
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, cancellationToken);

        if (template == null)
            throw new InvalidOperationException($"Template not found: {request.TemplateId}");

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);

        if (vehicle == null)
            throw new InvalidOperationException($"Vehicle not found: {request.VehicleId}");

        // Find existing schedule OR create a new one
        var schedule = await _context.VehicleMaintenanceSchedules
            .FirstOrDefaultAsync(s => s.VehicleId == request.VehicleId && s.TemplateId == request.TemplateId, cancellationToken);

        if (schedule == null)
        {
            // Calypso 7 (P-maint-bis): use the smart mileage resolver, same
            // reason as AssignMaintenanceTemplateCommandHandler — keep
            // NextDueKm consistent with the trips fallback when the tracker
            // has no FMS odometer wired.
            var currentMileage = await _scheduler.GetCurrentMileageAsync(request.VehicleId, cancellationToken);

            // Create schedule with initial due calculation (same logic as AssignMaintenanceTemplateCommandHandler)
            schedule = new VehicleMaintenanceSchedule
            {
                VehicleId = request.VehicleId,
                TemplateId = request.TemplateId,
                NextDueKm = template.IntervalKm.HasValue ? currentMileage + template.IntervalKm.Value : null,
                NextDueDate = template.IntervalMonths.HasValue ? DateTime.UtcNow.AddMonths(template.IntervalMonths.Value) : null,
                Status = "upcoming",
                FreeUsesTotal = request.Count,
                FreeUsesRemaining = request.Count,
                FreeSource = request.Source,
                FreeExpiryDate = request.ExpiryDate,
                FreeNotes = request.Notes
            };
            _context.VehicleMaintenanceSchedules.Add(schedule);
        }
        else
        {
            // Accumulate counters — existing benefits stay, new ones are added on top
            schedule.FreeUsesTotal += request.Count;
            schedule.FreeUsesRemaining += request.Count;

            // Update metadata if provided (caller's values win over previous ones)
            if (!string.IsNullOrWhiteSpace(request.Source))
                schedule.FreeSource = request.Source;

            if (request.ExpiryDate.HasValue)
                schedule.FreeExpiryDate = request.ExpiryDate;

            if (!string.IsNullOrWhiteSpace(request.Notes))
                schedule.FreeNotes = request.Notes;

            schedule.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return schedule.Id;
    }
}

public class UpdateFreeMaintenanceCommandHandler : IRequestHandler<UpdateFreeMaintenanceCommand, bool>
{
    private readonly IGisDbContext _context;

    public UpdateFreeMaintenanceCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(UpdateFreeMaintenanceCommand request, CancellationToken cancellationToken)
    {
        var schedule = await _context.VehicleMaintenanceSchedules
            .FirstOrDefaultAsync(s => s.Id == request.ScheduleId, cancellationToken);

        if (schedule == null) return false;

        if (request.FreeUsesTotal < 0 || request.FreeUsesRemaining < 0)
            throw new InvalidOperationException("Les compteurs d'entretiens gratuits ne peuvent pas être négatifs.");

        if (request.FreeUsesRemaining > request.FreeUsesTotal)
            throw new InvalidOperationException("Le nombre d'entretiens gratuits restants ne peut pas dépasser le total.");

        schedule.FreeUsesTotal = request.FreeUsesTotal;
        schedule.FreeUsesRemaining = request.FreeUsesRemaining;
        schedule.FreeSource = request.Source;
        schedule.FreeExpiryDate = request.ExpiryDate;
        schedule.FreeNotes = request.Notes;
        schedule.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class ClearFreeMaintenanceCommandHandler : IRequestHandler<ClearFreeMaintenanceCommand, bool>
{
    private readonly IGisDbContext _context;

    public ClearFreeMaintenanceCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(ClearFreeMaintenanceCommand request, CancellationToken cancellationToken)
    {
        var schedule = await _context.VehicleMaintenanceSchedules
            .FirstOrDefaultAsync(s => s.Id == request.ScheduleId, cancellationToken);

        if (schedule == null) return false;

        schedule.FreeUsesTotal = 0;
        schedule.FreeUsesRemaining = 0;
        schedule.FreeSource = null;
        schedule.FreeExpiryDate = null;
        schedule.FreeNotes = null;
        schedule.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}



