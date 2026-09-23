namespace GisAPI.Application.Common.Interfaces;

/// <summary>
/// Interface for GPS SignalR hub service to broadcast real-time updates
/// </summary>
public interface IGpsHubService
{
    /// <summary>
    /// Diffuse une position SANS dire de quel véhicule il s'agit : elle ne peut donc
    /// atteindre que les destinataires « tout le parc » (administrateurs). À n'utiliser
    /// que lorsque le véhicule est réellement inconnu.
    /// </summary>
    Task SendPositionUpdateAsync(int companyId, object position);

    /// <summary>
    /// Diffuse une position à ceux qui voient tout le parc ET aux utilisateurs restreints
    /// dont la portée contient CE véhicule. C'est la voie normale : un flux temps réel
    /// envoyé à toute la société ne cloisonne rien chez un loueur, où chaque véhicule est
    /// loué à un client différent (incident HERTZ).
    /// </summary>
    Task SendPositionUpdateAsync(int companyId, int? vehicleId, object position);

    /// <summary>
    /// Send position update to clients subscribed to a specific vehicle
    /// </summary>
    Task SendVehiclePositionAsync(int vehicleId, object position);

    /// <summary>
    /// Diffuse une alerte — qui porte la plaque et la position du véhicule — selon la
    /// même règle que <see cref="SendPositionUpdateAsync(int, int?, object)"/>.
    /// </summary>
    Task SendAlertAsync(int companyId, int? vehicleId, object alert);

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



