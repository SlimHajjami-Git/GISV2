using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Notifications.Events;

public class MaintenanceDueNotificationHandler : INotificationHandler<MaintenanceDueNotificationEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IGisDbContext _context;
    private readonly ILogger<MaintenanceDueNotificationHandler> _logger;

    public MaintenanceDueNotificationHandler(
        INotificationService notificationService,
        IGisDbContext context,
        ILogger<MaintenanceDueNotificationHandler> logger)
    {
        _notificationService = notificationService;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Dedup window for maintenance_due notifications. Calypso 6 (P2): users
    /// were seeing 4 identical bell entries for the same template/vehicle —
    /// the predictive watcher fires every 6 h, so we suppress a re-fire if
    /// any maintenance_due notification for the same schedule landed within
    /// this window.
    /// </summary>
    private static readonly TimeSpan DedupWindow = TimeSpan.FromHours(6);

    public async Task Handle(MaintenanceDueNotificationEvent e, CancellationToken ct)
    {
        try
        {
            // Calypso 6 (P2): dedup at the schedule level so retries / cron
            // re-runs / handler reentry do not produce duplicate bell entries.
            // Keyed on (companyId, type=maintenance_due, scheduleId, last 6h).
            var cutoff = DateTime.UtcNow - DedupWindow;
            var alreadySent = await _context.Notifications
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(n => n.CompanyId == e.CompanyId
                            && n.Type == "maintenance_due"
                            && n.ReferenceType == "maintenance_schedule"
                            && n.ReferenceId == e.ScheduleId
                            && n.CreatedAt >= cutoff, ct);

            if (alreadySent)
            {
                _logger.LogDebug(
                    "MaintenanceDue: skipping duplicate fan-out for schedule {ScheduleId} ({Template} / {Vehicle}) — already sent within {Hours}h",
                    e.ScheduleId, e.TemplateName, e.VehicleName, DedupWindow.TotalHours);
                return;
            }

            var targetUsers = await _context.Users
                .Where(u => u.CompanyId == e.CompanyId && u.Status == "active")
                .Select(u => u.Id)
                .ToListAsync(ct);

            if (targetUsers.Count == 0) return;

            var statusLabel = e.Status == "overdue" ? "EN RETARD" : "À faire";
            var detail = e.KmUntilDue.HasValue
                ? $" ({Math.Abs(e.KmUntilDue.Value)} km)"
                : e.DaysUntilDue.HasValue
                    ? $" ({Math.Abs(e.DaysUntilDue.Value)} jours)"
                    : "";

            var title = $"Entretien {statusLabel} — {e.VehicleName}";
            var message = $"{e.TemplateName} pour {e.VehicleName}{detail}";

            foreach (var userId in targetUsers)
            {
                await _notificationService.CreateAndSendAsync(
                    companyId: e.CompanyId,
                    userId: userId,
                    type: "maintenance_due",
                    title: title,
                    message: message,
                    priority: e.Status == "overdue" ? "high" : "normal",
                    referenceType: "maintenance_schedule",
                    referenceId: e.ScheduleId,
                    actionUrl: "/entretien-programmable",
                    ct: ct
                );
            }

            _logger.LogDebug("Maintenance notification sent to {Count} users: {Template} for {Vehicle}",
                targetUsers.Count, e.TemplateName, e.VehicleName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create maintenance due notification");
        }
    }
}
