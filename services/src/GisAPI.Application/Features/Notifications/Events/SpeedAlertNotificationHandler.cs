using GisAPI.Application.Common.Interfaces;
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
            // Find company admin users to notify
            var adminUsers = await _context.Users
                .Where(u => u.CompanyId == e.CompanyId && u.Status == "active")
                .Include(u => u.Role)
                .Where(u => u.Role != null && u.Role.IsCompanyAdmin)
                .Select(u => u.Id)
                .ToListAsync(ct);

            if (adminUsers.Count == 0) return;

            var vehicleLabel = e.VehicleName ?? e.Plate ?? $"Véhicule #{e.VehicleId}";
            var title = $"⚠️ Excès de vitesse — {vehicleLabel}";
            var message = e.SpeedLimitKph.HasValue
                ? $"{vehicleLabel} roule à {e.SpeedKph:F0} km/h (limite: {e.SpeedLimitKph} km/h)"
                : $"{vehicleLabel} roule à {e.SpeedKph:F0} km/h";

            foreach (var userId in adminUsers)
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
                adminUsers.Count, vehicleLabel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create speed alert notification");
        }
    }
}
