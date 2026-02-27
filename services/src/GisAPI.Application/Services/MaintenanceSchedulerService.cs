using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Services;

public interface IMaintenanceSchedulerService
{
    Task<int> UpdateAllScheduleStatusesAsync(int? companyId = null, CancellationToken ct = default);
    Task<int> UpdateVehicleScheduleStatusesAsync(int vehicleId, CancellationToken ct = default);
    Task<List<MaintenanceNotification>> GenerateNotificationsAsync(int? companyId = null, CancellationToken ct = default);
    Task<VehicleMaintenanceSchedule> CalculateNextMaintenanceAsync(int scheduleId, DateTime doneDate, int doneKm, CancellationToken ct = default);
    Task<int> GetCurrentMileageAsync(int vehicleId, CancellationToken ct = default);
}

public class MaintenanceSchedulerService : IMaintenanceSchedulerService
{
    private readonly IGisDbContext _context;
    private readonly ILogger<MaintenanceSchedulerService> _logger;

    public MaintenanceSchedulerService(IGisDbContext context, ILogger<MaintenanceSchedulerService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Récupère le kilométrage actuel d'un véhicule depuis GPS ou manuel
    /// </summary>
    public async Task<int> GetCurrentMileageAsync(int vehicleId, CancellationToken ct = default)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == vehicleId, ct);

        if (vehicle == null) return 0;

        // Si le véhicule a un GPS, chercher le dernier odometer_km
        if (vehicle.GpsDeviceId.HasValue)
        {
            var firmwareVersion = vehicle.GpsDevice?.FirmwareVersion;
            var isFirmwareL = !string.IsNullOrEmpty(firmwareVersion)
                && firmwareVersion.StartsWith("L", StringComparison.OrdinalIgnoreCase);

            var lastOdometer = await _context.GpsPositions
                .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value 
                         && p.OdometerKm.HasValue 
                         && p.OdometerKm > 0
                         && p.OdometerKm != 1048574)
                .OrderByDescending(p => p.RecordedAt)
                .Select(p => p.OdometerKm)
                .FirstOrDefaultAsync(ct);

            if (lastOdometer.HasValue && lastOdometer.Value > 0)
            {
                if (isFirmwareL)
                {
                    // Firmware "L": odometer_km is reliable
                    return (int)lastOdometer.Value;
                }
                else if (lastOdometer.Value > vehicle.Mileage)
                {
                    return (int)lastOdometer.Value;
                }
            }
        }

        return vehicle.Mileage;
    }

    /// <summary>
    /// Met à jour le statut de tous les schedules d'une société ou de toutes les sociétés
    /// </summary>
    public async Task<int> UpdateAllScheduleStatusesAsync(int? companyId = null, CancellationToken ct = default)
    {
        var query = _context.VehicleMaintenanceSchedules
            .Include(s => s.Template)
            .Include(s => s.Vehicle)
            .Where(s => !s.IsPaused && s.Template!.IsActive);

        if (companyId.HasValue)
        {
            query = query.Where(s => s.Vehicle!.CompanyId == companyId.Value);
        }

        var schedules = await query.ToListAsync(ct);
        var updatedCount = 0;

        foreach (var schedule in schedules)
        {
            var currentKm = await GetCurrentMileageAsync(schedule.VehicleId, ct);
            var newStatus = CalculateStatus(schedule, currentKm);

            if (schedule.Status != newStatus)
            {
                schedule.Status = newStatus;
                schedule.UpdatedAt = DateTime.UtcNow;
                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Updated {Count} maintenance schedule statuses", updatedCount);
        }

        return updatedCount;
    }

    /// <summary>
    /// Met à jour le statut des schedules d'un véhicule spécifique
    /// </summary>
    public async Task<int> UpdateVehicleScheduleStatusesAsync(int vehicleId, CancellationToken ct = default)
    {
        var currentKm = await GetCurrentMileageAsync(vehicleId, ct);
        
        var schedules = await _context.VehicleMaintenanceSchedules
            .Include(s => s.Template)
            .Where(s => s.VehicleId == vehicleId && !s.IsPaused && s.Template!.IsActive)
            .ToListAsync(ct);

        var updatedCount = 0;

        foreach (var schedule in schedules)
        {
            var newStatus = CalculateStatus(schedule, currentKm);

            if (schedule.Status != newStatus)
            {
                schedule.Status = newStatus;
                schedule.UpdatedAt = DateTime.UtcNow;
                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return updatedCount;
    }

    /// <summary>
    /// Génère les notifications pour les entretiens à venir/dus/en retard
    /// </summary>
    public async Task<List<MaintenanceNotification>> GenerateNotificationsAsync(int? companyId = null, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var notifications = new List<MaintenanceNotification>();

        var query = _context.VehicleMaintenanceSchedules
            .Include(s => s.Template)
            .Include(s => s.Vehicle)
            .Where(s => !s.IsPaused 
                     && s.Template!.IsActive
                     && (s.Status == "due" || s.Status == "overdue" || s.Status == "critical"));

        if (companyId.HasValue)
        {
            query = query.Where(s => s.Vehicle!.CompanyId == companyId.Value);
        }

        var schedules = await query.ToListAsync(ct);

        foreach (var schedule in schedules)
        {
            // Vérifier si une notification a déjà été envoyée aujourd'hui
            var existingToday = await _context.MaintenanceNotifications
                .AnyAsync(n => n.ScheduleId == schedule.Id 
                            && n.NotificationType == schedule.Status
                            && n.CreatedAt.Date == today, ct);

            if (existingToday) continue;

            // Vérifier la fréquence de rappel
            var settings = await _context.MaintenanceAlertSettings
                .FirstOrDefaultAsync(s => s.CompanyId == schedule.Vehicle!.CompanyId, ct);

            var reminderDays = settings?.ReminderFrequencyDays ?? 7;
            var maxReminders = settings?.MaxReminders ?? 3;

            if (schedule.NotificationCount >= maxReminders) continue;
            if (schedule.LastNotificationAt.HasValue 
                && (today - schedule.LastNotificationAt.Value.Date).TotalDays < reminderDays)
                continue;

            var currentKm = await GetCurrentMileageAsync(schedule.VehicleId, ct);

            var notification = new MaintenanceNotification
            {
                ScheduleId = schedule.Id,
                VehicleId = schedule.VehicleId,
                TemplateId = schedule.TemplateId,
                CompanyId = schedule.Vehicle!.CompanyId,
                NotificationType = schedule.Status,
                TriggerReason = DetermineTriggerReason(schedule, currentKm),
                CurrentKm = currentKm,
                KmRemaining = schedule.NextDueKm.HasValue ? schedule.NextDueKm.Value - currentKm : null,
                DaysRemaining = schedule.NextDueDate.HasValue ? (int)(schedule.NextDueDate.Value - today).TotalDays : null
            };

            _context.MaintenanceNotifications.Add(notification);
            
            schedule.LastNotificationAt = DateTime.UtcNow;
            schedule.NotificationCount++;

            notifications.Add(notification);
        }

        if (notifications.Count > 0)
        {
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("Generated {Count} maintenance notifications", notifications.Count);
        }

        return notifications;
    }

    /// <summary>
    /// Calcule la prochaine échéance après réalisation d'un entretien
    /// </summary>
    public async Task<VehicleMaintenanceSchedule> CalculateNextMaintenanceAsync(
        int scheduleId, 
        DateTime doneDate, 
        int doneKm, 
        CancellationToken ct = default)
    {
        var schedule = await _context.VehicleMaintenanceSchedules
            .Include(s => s.Template)
            .FirstOrDefaultAsync(s => s.Id == scheduleId, ct);

        if (schedule == null)
            throw new InvalidOperationException($"Schedule not found: {scheduleId}");

        var intervalKm = schedule.CustomIntervalKm ?? schedule.Template?.IntervalKm;
        var intervalMonths = schedule.CustomIntervalMonths ?? schedule.Template?.IntervalMonths;

        schedule.LastDoneDate = doneDate;
        schedule.LastDoneKm = doneKm;
        schedule.NextDueKm = intervalKm.HasValue ? doneKm + intervalKm.Value : null;
        schedule.NextDueDate = intervalMonths.HasValue ? doneDate.AddMonths(intervalMonths.Value) : null;
        schedule.NotificationCount = 0;
        schedule.LastNotificationAt = null;
        schedule.Status = "ok";
        schedule.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return schedule;
    }

    /// <summary>
    /// Calcule le statut d'un schedule en fonction du kilométrage et de la date
    /// </summary>
    private static string CalculateStatus(VehicleMaintenanceSchedule schedule, int currentKm)
    {
        var today = DateTime.UtcNow.Date;
        var template = schedule.Template;

        if (template == null) return "ok";

        var warningKm = template.WarningKm > 0 ? template.WarningKm : 1000;
        var warningDays = template.WarningDays > 0 ? template.WarningDays : 30;
        var criticalKm = template.CriticalKm;
        var criticalDays = template.CriticalDays;

        // Vérifier overdue
        if (schedule.NextDueKm.HasValue && currentKm > schedule.NextDueKm.Value)
            return "overdue";
        if (schedule.NextDueDate.HasValue && today > schedule.NextDueDate.Value.Date)
            return "overdue";

        // Vérifier critical
        if (criticalKm > 0 && schedule.NextDueKm.HasValue 
            && schedule.NextDueKm.Value - currentKm <= criticalKm)
            return "critical";
        if (criticalDays > 0 && schedule.NextDueDate.HasValue 
            && (schedule.NextDueDate.Value.Date - today).TotalDays <= criticalDays)
            return "critical";

        // Vérifier due (seuil warning)
        if (schedule.NextDueKm.HasValue && schedule.NextDueKm.Value - currentKm <= warningKm)
            return "due";
        if (schedule.NextDueDate.HasValue && (schedule.NextDueDate.Value.Date - today).TotalDays <= warningDays)
            return "due";

        // Vérifier upcoming (< 5000 km ou < 90 jours)
        if (schedule.NextDueKm.HasValue && schedule.NextDueKm.Value - currentKm <= 5000)
            return "upcoming";
        if (schedule.NextDueDate.HasValue && (schedule.NextDueDate.Value.Date - today).TotalDays <= 90)
            return "upcoming";

        return "ok";
    }

    /// <summary>
    /// Détermine la raison du déclenchement (km, date ou les deux)
    /// </summary>
    private static string DetermineTriggerReason(VehicleMaintenanceSchedule schedule, int currentKm)
    {
        var today = DateTime.UtcNow.Date;
        var kmTriggered = schedule.NextDueKm.HasValue;
        var dateTriggered = schedule.NextDueDate.HasValue;

        if (kmTriggered && dateTriggered)
        {
            var kmNear = schedule.NextDueKm!.Value - currentKm <= (schedule.Template?.WarningKm ?? 1000);
            var dateNear = (schedule.NextDueDate!.Value.Date - today).TotalDays <= (schedule.Template?.WarningDays ?? 30);

            if (kmNear && dateNear) return "both";
            if (kmNear) return "km";
            return "date";
        }

        return kmTriggered ? "km" : "date";
    }
}
