using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GisAPI.Application.Common.Security;

/// <summary>
/// Jeton d'API à usage unique (migration 064) : format du jeton et périmètre de ce qu'il peut appeler.
///
/// <para>Un jeton emprunte l'identité de l'utilisateur qui l'a généré (périmètre de véhicules,
/// droits, abonnement relus en base à chaque appel), mais il est bien plus étroit que sa session : il
/// ne lit que les arrêts, jamais ne modifie rien, sur une période bornée, UNE seule fois et pendant
/// quelques minutes. Sans cette liste blanche, un jeton volé vaudrait la session complète du compte.</para>
/// </summary>
public static class ApiTokenAccess
{
    /// <summary>Préfixe de tous les jetons : reconnaissable dans un dépôt de code ou un journal.</summary>
    public const string TokenPrefix = "cly_";

    public const string HeaderName = "X-Api-Token";

    /// <summary>Claim posé sur l'identité d'une requête authentifiée par jeton d'API.</summary>
    public const string AuthMethodClaim = "auth_method";
    public const string AuthMethodValue = "api_token";

    /// <summary>Durée de vie d'un jeton non utilisé.</summary>
    public const int LifetimeMinutes = 15;

    /// <summary>Jetons générés mais ni utilisés ni expirés, par utilisateur (borne la table et le bruit).</summary>
    public const int MaxPendingPerUser = 5;

    /// <summary>Plus longue période lisible en un appel (jours, bornes comprises).</summary>
    public const int MaxPeriodDays = 31;

    /// <summary>Plus grande page de la liste à plat des arrêts.</summary>
    public const int MaxPageSize = 1000;

    public const string ReadOnlyMessage = "Ce jeton d'API ne permet que la lecture.";
    public const string RouteMessage = "Ce jeton d'API donne uniquement accès aux arrêts des véhicules.";
    public const string PeriodRequiredMessage = "Les paramètres startDate et endDate sont obligatoires avec un jeton d'API.";
    public const string PeriodTooLongMessage = "La période demandée ne peut pas dépasser 31 jours avec un jeton d'API.";
    public const string PageSizeMessage = "pageSize ne peut pas dépasser 1000 avec un jeton d'API.";

    /// <summary>Routes ouvertes à un jeton (frontière de segment : « /api/vehiclestopsx » n'en est pas une).</summary>
    private static readonly string[] AllowedRoutes = { "/api/reports/stops", "/api/vehiclestops" };

    /// <summary>Résultat d'un refus : statut HTTP et message.</summary>
    public readonly record struct Refusal(int StatusCode, string Message);

    /// <summary>Génère un jeton neuf : préfixe + 32 octets aléatoires en base64 URL.</summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var body = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return TokenPrefix + body;
    }

    /// <summary>Empreinte SHA-256 hexadécimale (le jeton est aléatoire : un hachage lent n'apporte rien).</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Début affichable du jeton, pour le reconnaître dans la liste.</summary>
    public static string DisplayPrefix(string token) => token.Length <= 12 ? token : token[..12];

    public static bool LooksLikeToken(string? value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(TokenPrefix, StringComparison.Ordinal);

    private static bool IsRouteOrSubRoute(string path, string route) =>
        string.Equals(path, route, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(route + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Un jeton peut-il faire cet appel ? <c>null</c> = oui ; sinon le refus à renvoyer.
    /// Les trois paramètres de requête sont les valeurs BRUTES de la chaîne de requête.
    /// Évalué AVANT de consommer le jeton : un appel refusé ici ne le brûle pas.
    /// </summary>
    public static Refusal? Evaluate(string method, string path, string? startDate, string? endDate, string? pageSize)
    {
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            return new Refusal(403, ReadOnlyMessage);

        if (!AllowedRoutes.Any(r => IsRouteOrSubRoute(path, r)))
            return new Refusal(403, RouteMessage);

        if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
            return new Refusal(400, PeriodRequiredMessage);

        if (!TryParseDate(startDate, out var start) || !TryParseDate(endDate, out var end))
            return new Refusal(400, PeriodRequiredMessage);

        // Période inversée : laissée au contrôleur, qui la refuse avec son propre message.
        if (start.Date <= end.Date && (end.Date - start.Date).TotalDays + 1 > MaxPeriodDays)
            return new Refusal(400, PeriodTooLongMessage);

        if (!string.IsNullOrWhiteSpace(pageSize)
            && (!int.TryParse(pageSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) || size > MaxPageSize))
            return new Refusal(400, PageSizeMessage);

        return null;
    }

    private static bool TryParseDate(string raw, out DateTime value) =>
        DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value);

    // ── Le compte qui génère ou porte le jeton ──

    public const string InactiveMessage = "Votre compte n'est pas actif.";
    public const string DriverMessage = "Un compte chauffeur ne peut pas générer de jeton d'API.";
    public const string SystemRoleMessage = "Un compte administrateur système ne peut pas générer de jeton d'API.";

    /// <summary>
    /// Raison pour laquelle ce compte ne peut pas porter de jeton, ou <c>null</c>. Même règle à la
    /// génération et à CHAQUE appel : un compte désactivé, converti en chauffeur ou promu
    /// administrateur système après coup ferme ses jetons en attente. Le rôle doit être chargé.
    /// </summary>
    public static string? AccountRefusal(GisAPI.Domain.Entities.User user)
    {
        if (!string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
            return InactiveMessage;
        if (user.IsDriverAccount)
            return DriverMessage;
        if (user.Role?.IsSystemRole == true)
            return SystemRoleMessage;
        return null;
    }
}
