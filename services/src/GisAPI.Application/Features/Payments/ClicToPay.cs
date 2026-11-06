using System.Globalization;

namespace GisAPI.Application.Features.Payments;

/// <summary>
/// Réglages ClicToPay de CE déploiement (section « ClicToPay » de la configuration, variables
/// <c>ClicToPay__UserName</c>, <c>ClicToPay__Password</c>… sur le serveur). Les identifiants
/// marchand ne vont JAMAIS dans le dépôt. Sans identifiant, le paiement par carte est fermé :
/// l'écran ne propose pas le bouton et l'API refuse.
/// </summary>
public class ClicToPayOptions
{
    /// <summary>Environnement de TEST du manuel « Basic 1.0 ».</summary>
    public const string TestBaseUrl = "https://test.clictopay.com/payment/rest/";
    public const string ProductionBaseUrl = "https://ipay.clictopay.com/payment/rest/";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = TestBaseUrl;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Adresse publique où ClicToPay renvoie le client ; vide = App:PublicBaseUrl.</summary>
    public string? ReturnBaseUrl { get; set; }

    /// <summary>Langue de la page de paiement (fr, en, ar).</summary>
    public string Language { get; set; } = "fr";

    /// <summary>
    /// Demander le gabarit MOBILE sur petit écran. Faux par défaut : sur le compte de test
    /// (06/11/2026), pageView=MOBILE renvoie une page mobile_payment.html en 404 ; la page
    /// DESKTOP s'affiche correctement sur téléphone. À activer seulement si ClicToPay confirme
    /// que le gabarit mobile est configuré pour le compte marchand.
    /// </summary>
    public bool MobilePageView { get; set; }

    /// <summary>
    /// Sociétés pilotes, séparées par des virgules (« 1,15 »). Vide = toutes. Sert à la recette
    /// ClicToPay sur le serveur de production : les identifiants de TEST y sont posés sans que les
    /// clients voient un paiement « de test ».
    /// </summary>
    public string? PilotCompanyIds { get; set; }

    /// <summary>
    /// Bouton « Payer par carte » côté CLIENTS (écran Abonnement). Faux par défaut : avec les seuls
    /// identifiants posés, ClicToPay ne s'éprouve que depuis la console admin (page « Paiement
    /// ClicToPay »). À passer à vrai avec les identifiants de production.
    /// </summary>
    public bool ClientCheckoutEnabled { get; set; }

    public bool IsConfigured =>
        Enabled && !string.IsNullOrWhiteSpace(UserName) && !string.IsNullOrWhiteSpace(Password)
        && Uri.TryCreate(BaseUrl, UriKind.Absolute, out _);

    /// <summary>Le paiement par carte est-il ouvert aux CLIENTS de cette société ?</summary>
    public bool IsEnabledFor(int? companyId)
    {
        if (!IsConfigured || !ClientCheckoutEnabled) return false;
        if (string.IsNullOrWhiteSpace(PilotCompanyIds)) return true;
        if (companyId is not int id) return false;
        return PilotCompanyIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(s => int.TryParse(s, out var pilot) && pilot == id);
    }

    /// <summary>Vrai sur l'environnement de test : l'écran le dit, aucun débit réel.</summary>
    public bool IsTestEnvironment =>
        BaseUrl.Contains("test.clictopay.com", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Appel register.do : la commande que le client va payer.</summary>
public record ClicToPayRegisterRequest(
    string OrderNumber,
    long AmountMinor,
    int CurrencyCode,
    string ReturnUrl,
    string FailUrl,
    string Description,
    string Language,
    string PageView);

/// <summary>Réponse de register.do : identifiant ClicToPay et page de paiement, ou l'erreur.</summary>
public record ClicToPayRegisterResult(int ErrorCode, string? ErrorMessage, string? OrderId, string? FormUrl)
{
    public bool Succeeded => ErrorCode == 0 && !string.IsNullOrEmpty(OrderId) && !string.IsNullOrEmpty(FormUrl);
}

/// <summary>Réponse de getOrderStatusExtended.do, réduite à ce que Calypso vérifie et garde.</summary>
public record ClicToPayOrderStatus(
    int ErrorCode,
    string? ErrorMessage,
    string? OrderNumber,
    int? OrderStatus,
    int? ActionCode,
    string? ActionCodeDescription,
    long? Amount,
    int? CurrencyCode,
    string? CardPanMasked,
    string? ApprovalCode);

/// <summary>Les deux appels du manuel. Une erreur de transport lève une exception.</summary>
public interface IClicToPayClient
{
    Task<ClicToPayRegisterResult> RegisterAsync(ClicToPayRegisterRequest request, CancellationToken ct);
    Task<ClicToPayOrderStatus> GetOrderStatusAsync(string providerOrderId, CancellationToken ct);
}

/// <summary>Devises et montants au format ClicToPay (unités minimales, codes ISO 4217 numériques).</summary>
public static class ClicToPayAmounts
{
    /// <summary>orderStatus = 2 : « Paiement accepté — montant débité avec succès ».</summary>
    public const int OrderStatusPaid = 2;

    /// <summary>Code numérique ISO 4217, ou null si ClicToPay ne prend pas cette devise.</summary>
    public static int? CurrencyCode(string? currency) => (currency ?? "").Trim().ToUpperInvariant() switch
    {
        "TND" => 788,
        "EUR" => 978,
        "USD" => 840,
        _ => null
    };

    /// <summary>Décimales de la devise : 3 pour le dinar (millimes), 2 sinon.</summary>
    public static int Decimals(string currency) =>
        string.Equals(currency?.Trim(), "TND", StringComparison.OrdinalIgnoreCase) ? 3 : 2;

    /// <summary>
    /// Montant en unités minimales : 50 TND → 50000, 36,50 EUR → 3650. Refus d'un montant qui
    /// ne tombe pas juste (un tiers de millime) : l'arrondir ferait payer autre chose que la commande.
    /// </summary>
    public static long ToMinorUnits(decimal amount, string currency)
    {
        var scaled = amount * (decimal)Math.Pow(10, Decimals(currency));
        if (scaled != decimal.Truncate(scaled))
            throw new ArgumentException($"Montant {amount.ToString(CultureInfo.InvariantCulture)} non exprimable en {currency}.");
        return (long)scaled;
    }

    public static decimal FromMinorUnits(long minor, string currency) =>
        minor / (decimal)Math.Pow(10, Decimals(currency));
}
