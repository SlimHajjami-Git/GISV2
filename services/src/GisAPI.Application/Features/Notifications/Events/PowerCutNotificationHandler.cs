using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Notifications.Events;

public class PowerCutNotificationHandler : INotificationHandler<PowerCutNotificationEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IGisDbContext _context;
    private readonly ILogger<PowerCutNotificationHandler> _logger;

    public PowerCutNotificationHandler(
        INotificationService notificationService,
        IGisDbContext context,
        ILogger<PowerCutNotificationHandler> logger)
    {
        _notificationService = notificationService;
        _context = context;
        _logger = logger;
    }

    public async Task Handle(PowerCutNotificationEvent e, CancellationToken ct)
    {
        try
        {
            var targetUsers = await _context.Users
                .Where(u => u.CompanyId == e.CompanyId && u.Status == "active")
                .Select(u => u.Id)
                .ToListAsync(ct);

            if (targetUsers.Count == 0) return;

            var vehicleLabel = e.VehicleName ?? $"Véhicule #{e.VehicleId}";
            var hours = e.OfflineDurationSecs / 3600;
            var minutes = (e.OfflineDurationSecs % 3600) / 60;
            var durationStr = minutes > 0 ? $"{hours}h {minutes}min" : $"{hours}h";

            var title = $"Coupure d'alimentation — {vehicleLabel}";
            var message = $"{vehicleLabel} a été hors ligne pendant {durationStr}";

            foreach (var userId in targetUsers)
            {
                await _notificationService.CreateAndSendAsync(
                    companyId: e.CompanyId,
                    userId: userId,
                    type: "power_cut",
                    title: title,
                    message: message,
                    priority: "high",
                    referenceType: "vehicle",
                    referenceId: e.VehicleId,
                    actionUrl: "/securite",
                    metadata: new Dictionary<string, object>
                    {
                        ["offlineDurationSecs"] = e.OfflineDurationSecs,
                        ["eventType"] = e.EventType,
                        ["lastKnownLat"] = e.LastKnownLat ?? 0,
                        ["lastKnownLon"] = e.LastKnownLon ?? 0
                    },
                    ct: ct
                );
            }

            _logger.LogDebug("Power cut notification sent to {Count} users for {Vehicle} (offline {Duration})",
                targetUsers.Count, vehicleLabel, durationStr);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create power cut notification");
        }
    }
}
