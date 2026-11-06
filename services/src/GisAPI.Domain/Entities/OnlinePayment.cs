using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// Une TENTATIVE de paiement en ligne d'une commande d'abonnement (migration 067, ClicToPay).
///
/// <para>Le client peut ouvrir la page de paiement, l'abandonner, recommencer : une ligne par
/// tentative. Seule une tentative vérifiée auprès du prestataire (statut « payée », montant,
/// devise et numéro conformes) valide la commande, par le même chemin que la validation
/// manuelle (ConfirmSubscriptionOrderCommand). Le retour du navigateur ne prouve rien.</para>
/// </summary>
public class OnlinePayment : TenantEntity
{
    public const string StatusPending = "pending";
    public const string StatusPaid = "paid";
    public const string StatusFailed = "failed";
    public const string StatusError = "error";
    public const string StatusExpired = "expired";
    public const string StatusAnomaly = "anomaly";

    public const string KindSubscription = "subscription";
    public const string KindAdminTest = "admin_test";

    /// <summary>La commande réglée ; null pour un paiement de test de la console admin.</summary>
    public int? SubscriptionOrderId { get; set; }
    public SubscriptionOrder? SubscriptionOrder { get; set; }

    /// <summary>subscription | admin_test (aucune commande ni abonnement touchés).</summary>
    public string Kind { get; set; } = KindSubscription;

    public string Provider { get; set; } = "clictopay";

    /// <summary>NOTRE numéro envoyé au prestataire : unique, 32 caractères au plus.</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>Identifiant du prestataire (UUID renvoyé par register.do).</summary>
    public string? ProviderOrderId { get; set; }

    /// <summary>Montant en unités minimales (millimes pour TND, centimes pour EUR/USD).</summary>
    public long AmountMinor { get; set; }

    /// <summary>Code ISO à 3 lettres (TND, EUR, USD).</summary>
    public string Currency { get; set; } = "TND";

    /// <summary>pending | paid | failed | error | expired | anomaly (voir migration 067).</summary>
    public string Status { get; set; } = StatusPending;

    /// <summary>orderStatus du prestataire au dernier contrôle (2 = payé).</summary>
    public int? ProviderOrderStatus { get; set; }
    public int? ActionCode { get; set; }
    public string? ActionDescription { get; set; }

    /// <summary>Numéro de carte tel que le prestataire le renvoie, DÉJÀ masqué.</summary>
    public string? CardPanMasked { get; set; }
    public string? ApprovalCode { get; set; }

    /// <summary>Motif d'une erreur, d'une anomalie ou d'un paiement à régulariser.</summary>
    public string? Note { get; set; }

    public int? CreatedByUserId { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
}
