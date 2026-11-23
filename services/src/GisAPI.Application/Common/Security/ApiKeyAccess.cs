using System.Security.Cryptography;
using GisAPI.Domain.Entities;

namespace GisAPI.Application.Common.Security;

/// <summary>
/// Clé d'API (migration 069) : format de la clé, plafonds et messages.
///
/// <para>Une application ne peut pas générer à la main un jeton à usage unique à chaque appel. La clé
/// s'utilise de deux façons : envoyée telle quelle (en-tête X-Api-Key) sur les routes des arrêts —
/// demande client du 23/11/2026, une clé utilisable pendant un mois sans étape intermédiaire —, ou
/// échangée contre un jeton de 15 minutes (<see cref="ApiTokenAccess"/>, route d'échange conservée).
/// Dans les deux cas elle n'ouvre QUE ce qu'ouvre un jeton : lecture des arrêts, 31 jours au plus par
/// appel, chaque appel compté et journalisé. Une clé volée se révoque en un clic et expire d'elle-même
/// au bout de <see cref="LifetimeDays"/> jours.</para>
/// </summary>
public static class ApiKeyAccess
{
    /// <summary>Préfixe de toutes les clés. Distinct de « cly_ » : une clé n'est jamais prise pour un jeton.</summary>
    public const string KeyPrefix = "clyk_";

    public const string HeaderName = "X-Api-Key";

    /// <summary>Durée de vie d'une clé (un mois, décision du 23/11/2026) : au-delà, l'administrateur en recrée une.</summary>
    public const int LifetimeDays = 30;

    /// <summary>Clés actives par société : deux pour pouvoir en changer sans coupure.</summary>
    public const int MaxActivePerCompany = 2;

    /// <summary>
    /// Utilisations par clé sur 24 heures glissantes, appels directs et échanges confondus. Un appel ne
    /// lit qu'une page de résultats : le plafond laisse de la place à une application qui pagine ou lit
    /// véhicule par véhicule.
    /// </summary>
    public const int MaxExchangesPer24Hours = 100;

    public const int LabelMaxLength = 100;
    public const string DefaultLabel = "Clé d'API";

    // ── Messages ──

    public const string InvalidKeyMessage =
        "Clé d'API absente ou inconnue. Envoyez-la dans l'en-tête X-Api-Key.";
    public const string RevokedMessage =
        "Cette clé d'API a été révoquée. Créez-en une nouvelle dans Profil > Accès API > Clés d'API.";
    public const string ExpiredMessage =
        "Cette clé d'API a expiré. Créez-en une nouvelle dans Profil > Accès API > Clés d'API.";
    public const string OwnerInactiveMessage =
        "Le compte qui a créé cette clé n'est plus actif : la clé ne fonctionne plus.";
    public const string OwnerNotAdminMessage =
        "Le compte qui a créé cette clé n'est plus administrateur de la société : la clé ne fonctionne plus.";
    public const string OwnerCannotMessage =
        "Le compte qui a créé cette clé ne peut plus utiliser l'API : la clé ne fonctionne plus.";
    public static readonly string QuotaMessage =
        $"Cette clé a déjà servi {MaxExchangesPer24Hours} fois sur les dernières 24 heures. Réessayez plus tard.";
    public static readonly string TooManyPendingMessage =
        $"{ApiTokenAccess.MaxPendingPerUser} jetons obtenus n'ont pas encore servi. "
        + $"Utilisez-les, ou attendez leur expiration ({ApiTokenAccess.LifetimeMinutes} minutes).";
    public static readonly string TooManyActiveMessage =
        $"Votre société a déjà {MaxActivePerCompany} clés actives. Révoquez-en une avant d'en créer une autre.";
    public static readonly string LabelTooLongMessage =
        $"Le nom de la clé ne peut pas dépasser {LabelMaxLength} caractères.";

    /// <summary>Génère une clé neuve : préfixe + 32 octets aléatoires en base64 URL.</summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var body = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return KeyPrefix + body;
    }

    /// <summary>Même empreinte que les jetons : la clé est aléatoire, un hachage lent n'apporte rien.</summary>
    public static string Hash(string key) => ApiTokenAccess.Hash(key);

    /// <summary>Début affichable de la clé, pour la reconnaître dans la liste.</summary>
    public static string DisplayPrefix(string key) => key.Length <= 12 ? key : key[..12];

    public static bool LooksLikeKey(string? value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(KeyPrefix, StringComparison.Ordinal);

    /// <summary>active, revoked ou expired (la révocation l'emporte sur l'expiration).</summary>
    public static string Status(ApiKey key, DateTime nowUtc) =>
        key.RevokedAt != null ? "revoked" : key.ExpiresAt <= nowUtc ? "expired" : "active";

    /// <summary>
    /// Raison pour laquelle le compte qui a créé la clé ne peut plus en obtenir de jetons, ou
    /// <c>null</c>. C'est la règle de <see cref="ApiTokenAccess.AccountRefusal"/> (relue à CHAQUE
    /// échange), dite du point de vue de la tâche planifiée et non de l'utilisateur connecté.
    /// </summary>
    public static string? OwnerRefusal(User owner, bool companyApiAccessEnabled) =>
        ApiTokenAccess.AccountRefusal(owner, companyApiAccessEnabled) switch
        {
            null => null,
            ApiTokenAccess.InactiveMessage => OwnerInactiveMessage,
            ApiTokenAccess.AdminOnlyMessage => OwnerNotAdminMessage,
            ApiTokenAccess.NotEnabledMessage => ApiTokenAccess.NotEnabledMessage,
            _ => OwnerCannotMessage
        };
}
