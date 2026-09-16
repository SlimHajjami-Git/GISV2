using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Notifications.Events;

public class SpeedAlertNotificationHandler : INotificationHandler<SpeedAlertNotificationEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IGisDbContext _context;
    private readonly ILogger<SpeedAlertNotificationHandler> _logger;

    public SpeedAlertNotificationHandler(
        INotificationService notificationService,
        IGisDbContext context,
        ILogger<SpeedAlertNotificationHandler> logger)
    {
        _notificationService = notificationService;
        _context = context;
        _logger = logger;
    }

    public async Task Handle(SpeedAlertNotificationEvent e, CancellationToken ct)
    {
        try
        {
            // Cloisonnement : administrateurs de societe + utilisateurs affectes
            // a CE vehicule. Sans ce filtre, un operateur restreint a 2 vehicules
            // etait notifie de tout le parc (incident Hertz du 15/09/2026).
            var targetUsers = await NotificationAudience.ForVehicleAsync(
                _context, e.CompanyId, e.VehicleId, ct);

            if (targetUsers.Count == 0) return;

            var vehicleLabel = e.VehicleName ?? e.Plate ?? $"Véhicule #{e.VehicleId}";
            var title = $"⚠️ Excès de vitesse — {vehicleLabel}";
            var message = e.SpeedLimitKph.HasValue
                ? $"{vehicleLabel} roule à {e.SpeedKph:F0} km/h (limite: {e.SpeedLimitKph} km/h)"
                : $"{vehicleLabel} roule à {e.SpeedKph:F0} km/h";

            foreach (var userId in targetUsers)
            {
                await _notificationService.CreateAndSendAsync(
                    companyId: e.CompanyId,
                    userId: userId,
                    type: "speed_alert",
                    title: title,
                    message: message,
                    priority: e.SpeedLimitKph.HasValue && e.SpeedKph > e.SpeedLimitKph.Value + 40 ? "high" 
                        : e.SpeedKph > 140 ? "high" : "normal",
                    referenceType: "vehicle",
                    referenceId: e.VehicleId,
                    actionUrl: "/monitoring",
                    ct: ct
                );
            }

            _logger.LogDebug("Speed alert notification sent to {Count} users for vehicle {Vehicle}",
                targetUsers.Count, vehicleLabel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create speed alert notification");
        }
    }
}
