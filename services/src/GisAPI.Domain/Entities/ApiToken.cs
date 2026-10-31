using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// Jeton d'API à usage unique (migration 064). Généré par un utilisateur qui vient de ressaisir son
/// e-mail et son mot de passe ; emprunte ses droits et son périmètre de véhicules ; n'ouvre qu'UN
/// appel de lecture des arrêts (voir <c>ApiTokenAccess</c>). Seule l'empreinte est conservée.
/// </summary>
public class ApiToken : TenantEntity
{
    public int UserId { get; set; }

    /// <summary>Début du jeton (« cly_ab12cd34 »), pour le reconnaître dans la liste.</summary>
    public string TokenPrefix { get; set; } = string.Empty;

    /// <summary>SHA-256 hexadécimal du jeton complet.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Renseigné au premier appel accepté : le jeton ne sert plus ensuite.</summary>
    public DateTime? UsedAt { get; set; }

    public string? CreatedIp { get; set; }

    public bool IsUsable(DateTime nowUtc) => UsedAt == null && ExpiresAt > nowUtc;
}
