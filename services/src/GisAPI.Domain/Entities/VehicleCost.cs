using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class VehicleCost : TenantEntity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    /// <summary>
    /// Cost category. Calypso 7 adds two values driven by the accident
    /// timeline:
    /// <list type="bullet">
    ///   <item><c>"repair"</c> — auto-inserted when an accident's Phase 5
    ///     (repair) is finalised with an actual cost. The row links back
    ///     via <see cref="AccidentEventId"/>.</item>
    ///   <item><c>"insurance_refund"</c> — auto-inserted when an accident's
    ///     Phase 6 (insurance settlement) records an approved amount.
    ///     Rendered as a credit (green/negative) in the /depenses UI.</item>
    ///   <item><c>"credit_note"</c> — avoir fournisseur (décision du 16/09/2026) :
    ///     écran Dépenses, scan d'un avoir, import Excel.</item>
    /// </list>
    /// Existing values (fuel, maintenance, document renewal, …) remain
    /// unchanged — only new values are added.
    ///
    /// <para>CRÉDITS : <c>insurance_refund</c> et <c>credit_note</c> sont stockés en
    /// <see cref="Amount"/> POSITIF (un montant ≤ 0 est refusé, DEF-050) et DÉDUITS des
    /// coûts. Tout lecteur qui additionne des dépenses passe par
    /// <c>VehicleCostCategory.SignedAmount</c> / <c>SignedTotalAsync</c> : une somme brute
    /// de <see cref="Amount"/> ajoute le crédit au lieu de le retrancher.</para>
    /// </summary>
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public int? Mileage { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? ReceiptUrl { get; set; }

    /// <summary>
    /// Optional invoice breakdown as a JSON array of
    /// <c>{"label","amount","category"}</c> objects — the billed lines of a
    /// scanned facture (vidange, filtres, main d'œuvre…). The expense stays a
    /// SINGLE row (one facture = one dépense); the breakdown is only rendered
    /// in the expense detail panel. Null for expenses without a breakdown.
    /// </summary>
    public string? DetailsJson { get; set; }
    public string? FuelType { get; set; }
    public decimal? Liters { get; set; }
    public decimal? PricePerLiter { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    /// <summary>
    /// Renouvellement de document (assurance, visite technique, vignette…) :
    /// nouvelle échéance du document payé, fournisseur et notes saisis dans la
    /// fenêtre de renouvellement. Colonnes <c>expiry_date</c>, <c>provider</c> et
    /// <c>notes</c> ajoutées par la migration 048 — avant elles ces propriétés
    /// n'avaient aucune colonne derrière et la saisie disparaissait sans message
    /// (recette GPA du 11/09/2026). Null sur toute autre dépense.
    /// </summary>
    public DateTime? ExpiryDate { get; set; }
    public string? Provider { get; set; }
    public string? Notes { get; set; }

    /// <summary>
    /// Numéro de pièce et justificatif d'un renouvellement : ce sont
    /// <see cref="ReceiptNumber"/> et <see cref="ReceiptUrl"/>, les seules
    /// colonnes de cette nature. Alias en LECTURE SEULE : tant qu'ils étaient des
    /// propriétés autonomes, tout écran qui les lisait recevait null (écrans
    /// Échéances compris) et tout code qui les écrivait perdait la saisie.
    /// </summary>
    public string? DocumentNumber => ReceiptNumber;
    public string? DocumentUrl => ReceiptUrl;

    /// <summary>
    /// Calypso 7 — back-reference to the accident that produced this
    /// cost row (Phase 5 repair or Phase 6 insurance refund). Null on
    /// every cost row that was entered manually or by another module
    /// (fuel, maintenance, etc.). Used by the /depenses UI to surface
    /// a "🚗 Accident" badge and group repair + refund into a net view.
    /// </summary>
    public int? AccidentEventId { get; set; }
    public AccidentEvent? AccidentEvent { get; set; }

    public Societe? Societe { get; set; }
}


