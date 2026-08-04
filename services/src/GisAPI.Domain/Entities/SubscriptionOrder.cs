using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// Une COMMANDE d'abonnement passée par un client en libre-service (offre GPA).
///
/// <para>POURQUOI UNE COMMANDE ET PAS UN PAIEMENT — aucun prestataire de paiement
/// en ligne n'est branché : les règlements se font hors application (virement,
/// CCP, espèces). Le circuit honnête est donc : le client COMMANDE depuis son
/// écran Abonnement, la plateforme VALIDE une fois le règlement reçu, et la
/// validation active l'abonnement via la même logique que le renouvellement
/// manuel (RenewSubscriptionCommand). Le jour où un prestataire sera branché, son
/// webhook confirmera la commande au lieu de l'opérateur — le reste ne bouge pas.</para>
///
/// <para>Le MONTANT est figé À LA COMMANDE, calculé côté serveur depuis le plan et
/// le cycle : si les prix changent entre la commande et la validation, le client
/// paie ce qu'on lui a affiché, pas le nouveau tarif.</para>
/// </summary>
public class SubscriptionOrder : TenantEntity
{
    public int SubscriptionTypeId { get; set; }
    public SubscriptionType? SubscriptionType { get; set; }

    /// <summary>monthly | quarterly | yearly.</summary>
    public string BillingCycle { get; set; } = "yearly";

    /// <summary>Montant dû, figé à la commande (devise du déploiement).</summary>
    public decimal Amount { get; set; }

    /// <summary>pending | confirmed | cancelled (par le client) | rejected (par la plateforme, motif dans Note).</summary>
    public string Status { get; set; } = "pending";

    /// <summary>L'utilisateur qui a passé la commande.</summary>
    public int CreatedByUserId { get; set; }

    /// <summary>Validation ou rejet : quand, et par qui (sys_admin).</summary>
    public DateTime? ProcessedAt { get; set; }
    public int? ProcessedByUserId { get; set; }

    /// <summary>Motif de rejet, montré au client.</summary>
    public string? Note { get; set; }
}
