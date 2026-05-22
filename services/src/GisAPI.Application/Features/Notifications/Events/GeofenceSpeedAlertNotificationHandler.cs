using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Notifications.Events;

public class GeofenceSpeedAlertNotificationHandler : INotificationHandler<GeofenceSpeedAlertNotificationEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IGisDbContext _context;
    private readonly ILogger<GeofenceSpeedAlertNotificationHandler> _logger;

    public GeofenceSpeedAlertNotificationHandler(
        INotificationService notificationService,
        IGisDbContext context,
        ILogger<GeofenceSpeedAlertNotificationHandler> logger)
    {
        _notificationService = notificationService;
        _context = context;
        _logger = logger;
    }

    public async Task Handle(GeofenceSpeedAlertNotificationEvent e, CancellationToken ct)
    {
        try
        {
            var targetUsers = await _context.Users
                .Where(u => u.CompanyId == e.CompanyId && u.Status == "active")
                .Select(u => u.Id)
                .ToListAsync(ct);

            if (targetUsers.Count == 0) return;

            var vehicleLabel = e.VehicleName ?? e.Plate ?? $"Véhicule #{e.VehicleId}";
            var title = $"⚠️ Vitesse dépassée en zone — {vehicleLabel}";
            var message = $"{vehicleLabel} roule à {e.SpeedKph:F0} km/h dans la zone \"{e.GeofenceName}\" (limite: {e.GeofenceSpeedLimitKph} km/h)";

            // High priority when speed is significantly over the zone limit
            var priority = e.SpeedKph > e.GeofenceSpeedLimitKph + 30 ? "high" : "normal";

            var metadata = new Dictionary<string, object>
            {
                ["latitude"] = e.Latitude,
                ["longitude"] = e.Longitude,
                ["vehicleId"] = e.VehicleId ?? 0,
                ["vehicleName"] = vehicleLabel,
                ["geofenceId"] = e.GeofenceId,
                ["geofenceName"] = e.GeofenceName,
                ["speedKph"] = e.SpeedKph,
                ["speedLimitKph"] = e.GeofenceSpeedLimitKph,
                ["timestamp"] = e.Timestamp.ToString("o")
            };

            foreach (var userId in targetUsers)
            {
                await _notificationService.CreateAndSendAsync(
                    companyId: e.CompanyId,
                    userId: userId,
                    type: "geofence_speed_alert",
                    title: title,
                    message: message,
                    priority: priority,
                    referenceType: "geofence",
                    referenceId: e.GeofenceId,
                    actionUrl: $"/monitoring?lat={e.Latitude}&lng={e.Longitude}&zoom=17&geofenceId={e.GeofenceId}&vehicleId={e.VehicleId}&timestamp={e.Timestamp:o}",
                    metadata: metadata,
                    ct: ct
                );
            }

            _logger.LogDebug("Geofence speed alert sent to {Count} users: {Vehicle} at {Speed:F0} km/h in {Zone} (limit {Limit})",
                targetUsers.Count, vehicleLabel, e.SpeedKph, e.GeofenceName, e.GeofenceSpeedLimitKph);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create geofence speed alert notification");
        }
    }
}
