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
    /// Récupère le kilométrage "actuel" d'un véhicule en cascadant trois sources
    /// dans l'ordre de fiabilité décroissante :
    ///
    /// <list type="number">
    ///   <item>
    ///     <description><b>Odomètre FMS du tracker GPS</b> — la valeur la plus
    ///     précise quand elle existe (CAN bus du véhicule câblé au tracker).
    ///     C'est ce que les NEMS L V3 remontent dans <c>gps_positions.odometer_km</c>.</description>
    ///   </item>
    ///   <item>
    ///     <description><b>Compteur manuel</b> — la colonne <c>vehicles.mileage</c>
    ///     entrée par l'admin à la création du véhicule, ou mise à jour
    ///     incrémentalement par l'accumulateur Haversine du Rust ingest pour
    ///     les trackers non-FMS (NEMS S / Noron).</description>
    ///   </item>
    ///   <item>
    ///     <description><b>Somme des trips</b> — fallback ultime quand le tracker
    ///     est en panne odomètre <i>et</i> que le compteur manuel n'a jamais été
    ///     renseigné. Les trips sont calculés par le <c>trip_detector</c> Rust
    ///     à partir des positions GPS — la distance est moins précise que
    ///     l'odomètre CAN bus mais reflète bien la réalité (validé sur le
    ///     257 TU 6114 : 5393 km de trips ↔ 5448 km Haversine, écart 1 %).</description>
    ///   </item>
    /// </list>
    ///
    /// <para>Le 3ᵉ étage corrige le bug Calypso 7 du 257 TU 6114 où un véhicule
    /// NEMS L avec CAN bus débranché restait à <c>vehicle.mileage = 0</c>
    /// indéfiniment, ce qui figeait le statut de tous ses entretiens
    /// programmables sur "upcoming" et masquait silencieusement les alertes.</para>
    /// </summary>
    public async Task<int> GetCurrentMileageAsync(int vehicleId, CancellationToken ct = default)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == vehicleId, ct);

        if (vehicle == null) return 0;

        // ─── Source 1 : odomètre FMS du GPS, GATÉ par une fenêtre de fraîcheur ─
        //
        // Un odomètre vieux d'un mois (cas observé sur 257 TU 6112 :
        // dernière valeur valide il y a 33 jours, CAN bus intermittent)
        // n'est PAS un signal fiable. La cascade conditionnelle précédente
        // se verrouillait dessus parce qu'il était > 0, et le compteur
        // restait gelé même si le camion roulait. Maintenant on n'utilise
        // l'odo GPS QUE s'il date de moins de FreshnessWindow.
        long gpsOdometer = 0;
        if (vehicle.GpsDeviceId.HasValue)
        {
            var lastOdoRow = await _context.GpsPositions
                .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value
                         && p.OdometerKm.HasValue
                         && p.OdometerKm > 0
                         && p.OdometerKm != 1048574)
                .OrderByDescending(p => p.RecordedAt)
                .Select(p => new { p.OdometerKm, p.RecordedAt })
                .FirstOrDefaultAsync(ct);

            if (lastOdoRow != null
                && lastOdoRow.OdometerKm.HasValue
                && (DateTime.UtcNow - lastOdoRow.RecordedAt) < OdometerFreshnessWindow)
            {
                gpsOdometer = lastOdoRow.OdometerKm.Value;
            }
        }

        // ─── Source 2 : compteur manuel / Haversine non-FMS ──────────────────
        var manualMileage = vehicle.Mileage;

        // ─── Source 3 : somme des trips fermés ───────────────────────────────
        var tripsKm = await _context.Trips
            .Where(t => t.VehicleId == vehicleId && t.EndTime != null)
            .SumAsync(t => (double?)t.DistanceKm, ct) ?? 0;
        var tripsMileage = (int)Math.Round(tripsKm);

        // Calypso 7 (P-maint-couche1, follow-up #4): MAX-cascade des trois
        // sources, où la Source 1 a déjà été nettoyée par la fenêtre de
        // fraîcheur. Trois cas couverts proprement :
        //
        //   - CAN bus actif (gpsOdo frais)  : MAX dominé par gpsOdo, pas de
        //                                     régression vs comportement OLD.
        //   - CAN bus intermittent (gpsOdo  : gpsOdometer = 0, MAX(manuel, trips)
        //     existe mais date > 48h)         → trips reprennent la main.
        //   - CAN bus jamais (gpsOdo NULL)  : idem cas intermittent.
        return (int)Math.Max(Math.Max(gpsOdometer, manualMileage), tripsMileage);
    }

    /// <summary>
    /// Au-delà de cette fenêtre, l'odomètre GPS est considéré comme stale
    /// et ignoré au profit de la cascade trips. 48h couvre confortablement
    /// un week-end de stationnement (le tracker peut envoyer des trames
    /// idle sans odomètre frais sur 2-3 jours sans qu'on bascule sur
    /// trips). Au-delà, c'est anormal et on traite comme une panne CAN bus.
    /// </summary>
    private static readonly TimeSpan OdometerFreshnessWindow = TimeSpan.FromHours(48);

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
