using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// Clé d'automatisation de l'API (migration 069). Créée une fois par l'administrateur de la société
/// pour une tâche planifiée : elle n'ouvre AUCUNE donnée par elle-même, elle ne sert qu'à obtenir des
/// jetons à usage unique (<see cref="ApiToken"/>, 15 minutes, arrêts seulement) par
/// POST /api/api-tokens/echange. Les jetons obtenus empruntent les droits et le périmètre de véhicules
/// de <see cref="UserId"/>, relus à chaque échange. Seule l'empreinte est conservée.
/// </summary>
public class ApiKey : TenantEntity
{
    /// <summary>L'administrateur qui a créé la clé : les jetons obtenus portent SES droits.</summary>
    public int UserId { get; set; }

    /// <summary>Nom choisi à la création (« serveur de paie »), pour distinguer deux clés.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Début de la clé (« clyk_ab12cd3 »), pour la reconnaître dans la liste.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>SHA-256 hexadécimal de la clé complète.</summary>
    public string KeyHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Renseigné à la révocation : la clé ne s'échange plus jamais.</summary>
    public DateTime? RevokedAt { get; set; }

    public int? RevokedByUserId { get; set; }

    /// <summary>Dernier échange accepté (l'historique complet est dans api_tokens.api_key_id).</summary>
    public DateTime? LastUsedAt { get; set; }

    public string? LastUsedIp { get; set; }

    public string? CreatedIp { get; set; }

    public bool IsActive(DateTime nowUtc) => RevokedAt == null && ExpiresAt > nowUtc;
}
