namespace GisAPI.Application.Common.Interfaces;

/// <summary>
/// Interface for GPS SignalR hub service to broadcast real-time updates
/// </summary>
public interface IGpsHubService
{
    /// <summary>
    /// Send position update to all clients subscribed to a company
    /// </summary>
    Task SendPositionUpdateAsync(int companyId, object position);

    /// <summary>
    /// Send position update to clients subscribed to a specific vehicle
    /// </summary>
    Task SendVehiclePositionAsync(int vehicleId, object position);

    /// <summary>
    /// Send alert to all clients subscribed to a company
    /// </summary>
    Task SendAlertAsync(int companyId, object alert);

    /// <summary>
    /// Send geofence event to clients subscribed to a geofence
    /// </summary>
    Task SendGeofenceEventAsync(int geofenceId, object geofenceEvent);

    /// <summary>
    /// Notifie en temps réel tous les utilisateurs connectés d'une société que
    /// l'état de son abonnement a changé (suspension/réactivation par le
    /// sys_admin) — le front re-vérifie immédiatement son statut.
    /// </summary>
    Task SendSubscriptionChangedAsync(int companyId, string status);
}



