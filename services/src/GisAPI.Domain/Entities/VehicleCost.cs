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
    /// </list>
    /// Existing values (fuel, maintenance, document renewal, …) remain
    /// unchanged — only new values are added.
    /// </summary>
    public string Type { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public int? Mileage { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? ReceiptUrl { get; set; }
    public string? FuelType { get; set; }
    public decimal? Liters { get; set; }
    public decimal? PricePerLiter { get; set; }
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    // Document renewal fields
    public DateTime? ExpiryDate { get; set; }
    public string? DocumentNumber { get; set; }
    public string? DocumentUrl { get; set; }

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


