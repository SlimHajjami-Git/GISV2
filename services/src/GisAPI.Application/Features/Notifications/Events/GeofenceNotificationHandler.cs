using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Notifications.Events;

public class GeofenceNotificationHandler : INotificationHandler<GeofenceNotificationEvent>
{
    private readonly INotificationService _notificationService;
    private readonly IGisDbContext _context;
    private readonly ILogger<GeofenceNotificationHandler> _logger;

    public GeofenceNotificationHandler(
        INotificationService notificationService,
        IGisDbContext context,
        ILogger<GeofenceNotificationHandler> logger)
    {
        _notificationService = notificationService;
        _context = context;
        _logger = logger;
    }

    public async Task Handle(GeofenceNotificationEvent e, CancellationToken ct)
    {
        try
        {
            // Persist geofence event in the database for history
            var geofenceEvent = new GisAPI.Domain.Entities.GeofenceEvent
            {
                GeofenceId = e.GeofenceId,
                VehicleId = e.VehicleId ?? 0,
                CompanyId = e.CompanyId,
                Type = e.EventType,
                Latitude = e.Latitude,
                Longitude = e.Longitude,
                Timestamp = e.Timestamp,
                IsNotified = true,
                NotifiedAt = DateTime.UtcNow
            };
            _context.GeofenceEvents.Add(geofenceEvent);
            await _context.SaveChangesAsync(ct);

            // Cloisonnement : administrateurs de societe + utilisateurs affectes
            // a CE vehicule. Sans ce filtre, un operateur restreint a 2 vehicules
            // etait notifie de tout le parc (incident Hertz du 15/09/2026).
            var targetUsers = await NotificationAudience.ForVehicleAsync(
                _context, e.CompanyId, e.VehicleId, ct);

            if (targetUsers.Count == 0) return;

            var vehicleLabel = e.VehicleName ?? $"Véhicule #{e.VehicleId}";
            var action = e.EventType == "entry" ? "est entré dans" : "a quitté";
            var title = $"Géofence — {vehicleLabel}";
            var message = $"{vehicleLabel} {action} la zone \"{e.GeofenceName}\"";

            foreach (var userId in targetUsers)
            {
                var metadata = new Dictionary<string, object>
                {
                    ["eventType"] = e.EventType,
                    ["latitude"] = e.Latitude,
                    ["longitude"] = e.Longitude,
                    ["vehicleId"] = e.VehicleId ?? 0,
                    ["vehicleName"] = vehicleLabel,
                    ["geofenceName"] = e.GeofenceName,
                    ["geofenceId"] = e.GeofenceId,
                    ["timestamp"] = e.Timestamp.ToString("o")
                };

                await _notificationService.CreateAndSendAsync(
                    companyId: e.CompanyId,
                    userId: userId,
                    type: "geofence_event",
                    title: title,
                    message: message,
                    priority: "normal",
                    referenceType: "geofence",
                    referenceId: e.GeofenceId,
                    actionUrl: $"/monitoring?lat={e.Latitude}&lng={e.Longitude}&zoom=17&geofenceId={e.GeofenceId}&vehicleId={e.VehicleId}&timestamp={e.Timestamp:o}",
                    metadata: metadata,
                    ct: ct
                );
            }

            _logger.LogDebug("Geofence notification sent to {Count} users: {Vehicle} {Action} {Geofence}",
                targetUsers.Count, vehicleLabel, action, e.GeofenceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create geofence notification");
        }
    }
}
