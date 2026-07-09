using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class UserDeviceToken : Entity
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    /// <summary>
    /// Identifiant STABLE de l'appareil (Android ID via Capacitor Device.getId()),
    /// qui survit aux réinstallations. Permet de désactiver les anciens jetons FCM
    /// du même téléphone à l'enregistrement d'un nouveau — sans lui, chaque
    /// réinstallation/MAJ de l'appli empile un jeton livrable de plus et
    /// l'utilisateur reçoit N copies de chaque notification.
    /// Null pour les anciennes versions de l'appli.
    /// </summary>
    public string? DeviceId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}
