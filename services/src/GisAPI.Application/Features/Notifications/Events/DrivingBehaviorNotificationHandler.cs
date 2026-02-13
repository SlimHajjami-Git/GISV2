using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Notifications.Events;

public class DrivingBehaviorNotificationHandler : INotificationHandler<DrivingBehaviorNotificationEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IGisDbContext _context;
    private readonly ILogger<DrivingBehaviorNotificationHandler> _logger;

    public DrivingBehaviorNotificationHandler(
        INotificationService notificationService,
        IGisDbContext context,
        ILogger<DrivingBehaviorNotificationHandler> logger)
    {
        _notificationService = notificationService;
        _context = context;
        _logger = logger;
    }

    public async Task Handle(DrivingBehaviorNotificationEvent e, CancellationToken ct)
    {
        try
        {
            var adminUsers = await _context.Users
                .Where(u => u.CompanyId == e.CompanyId && u.Status == "active")
                .Include(u => u.Role)
                .Where(u => u.Role != null && u.Role.IsCompanyAdmin)
                .Select(u => u.Id)
                .ToListAsync(ct);

            if (adminUsers.Count == 0) return;

            var vehicleLabel = e.VehicleName ?? e.Plate ?? $"Véhicule #{e.VehicleId}";
            var (title, message, priority) = e.BehaviorType switch
            {
                "harsh_braking" => (
                    $"Freinage brusque — {vehicleLabel}",
                    $"{vehicleLabel} a effectué un freinage brusque à {e.SpeedKph:F0} km/h",
                    "high"
                ),
                "rapid_acceleration" => (
                    $"Accélération rapide — {vehicleLabel}",
                    $"{vehicleLabel} a effectué une accélération rapide",
                    "normal"
                ),
                "sharp_turn" => (
                    $"Virage brusque — {vehicleLabel}",
                    $"{vehicleLabel} a effectué un virage brusque à {e.SpeedKph:F0} km/h",
                    "normal"
                ),
                "pothole" => (
                    $"Choc détecté — {vehicleLabel}",
                    $"{vehicleLabel} a subi un choc (nid-de-poule)",
                    "low"
                ),
                "overspeed" => (
                    $"Excès de vitesse — {vehicleLabel}",
                    $"{vehicleLabel} roule à {e.SpeedKph:F0} km/h",
                    e.SpeedKph > 140 ? "high" : "normal"
                ),
                _ => (
                    $"Conduite dangereuse — {vehicleLabel}",
                    $"{vehicleLabel} — comportement de conduite anormal détecté",
                    "normal"
                )
            };

            foreach (var userId in adminUsers)
            {
                await _notificationService.CreateAndSendAsync(
                    companyId: e.CompanyId,
                    userId: userId,
                    type: "driving_behavior",
                    title: title,
                    message: message,
                    priority: priority,
                    referenceType: "vehicle",
                    referenceId: e.VehicleId,
                    actionUrl: "/monitoring",
                    metadata: new Dictionary<string, object>
                    {
                        { "behaviorType", e.BehaviorType },
                        { "latitude", e.Latitude },
                        { "longitude", e.Longitude }
                    },
                    ct: ct
                );
            }

            _logger.LogDebug("Driving behavior notification sent to {Count} admins: {Vehicle} — {BehaviorType}",
                adminUsers.Count, vehicleLabel, e.BehaviorType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create driving behavior notification");
        }
    }
}
