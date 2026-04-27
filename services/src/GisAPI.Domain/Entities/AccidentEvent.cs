using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// A persisted accident event: the canonical facts (when, where, which
/// device) plus the narrative sections rendered on the report page
/// (<c>/rapport-accident/{id}</c>).
///
/// Story / Reasons / Indicators are stored as JSON strings and deserialized
/// at the application layer — this avoids a table explosion for a feature
/// that is, for now, used to back a single retro accident.
/// </summary>
public class AccidentEvent : TenantEntity
{
    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public int? GpsDeviceId { get; set; }
    public GpsDevice? GpsDevice { get; set; }

    /// <summary>
    /// Raw device_uid (IMEI) — copied from the GPS device at creation time so
    /// the frontend can pull the real history even if the device row is later
    /// detached from the vehicle.
    /// </summary>
    public string DeviceUid { get; set; } = string.Empty;

    public DateTime IncidentAt { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>Human-friendly reference shown on the report header.</summary>
    public string? ReferenceCode { get; set; }

    /// <summary>Plate / label shown on the report header.</summary>
    public string? VehicleLabel { get; set; }

    public string? LocationCommune { get; set; }
    public string? LocationGovernorate { get; set; }
    public string? LocationRoadType { get; set; }

    /// <summary>
    /// Additional free text that appears under the synthesis section header
    /// (e.g. "choc violent ayant entraîné un retournement du véhicule").
    /// </summary>
    public string? SynthesisText { get; set; }

    /// <summary>0–100 confidence score for the accident diagnosis.</summary>
    public int Confidence { get; set; } = 90;

    /// <summary>JSON array of <c>{time, title, body, severity}</c> events.</summary>
    public string? StoryJson { get; set; }

    /// <summary>JSON array of <c>{title, text}</c> reason items.</summary>
    public string? ReasonsJson { get; set; }

    /// <summary>JSON array of <c>{label, value, hint?}</c> indicator rows.</summary>
    public string? IndicatorsJson { get; set; }

    // ── Decision workflow (modal + tow monitoring) ─────────────────────────
    // Detection creates a row with status = "pending". A company admin then
    // confirms or dismisses the accident via the strictly-blocking modal
    // (or the fallback buttons on /rapport-accident/:id). Once confirmed,
    // AccidentTowMonitoringService watches GPS positions for the next 14 days
    // and stamps TowDetectedAt the moment the vehicle starts moving again.

    /// <summary>
    /// Lifecycle status. Calypso 6 (P9) extended to 4 values:
    /// <list type="bullet">
    ///   <item><c>"pending"</c> — detection created the row, awaiting admin decision in the modal.</item>
    ///   <item><c>"awaiting_details"</c> — admin confirmed via the modal, PDF was auto-generated and
    ///     attached, but the damages form is still empty.</item>
    ///   <item><c>"confirmed"</c> — admin filled the damages form and finalised the report.</item>
    ///   <item><c>"dismissed"</c> — fausse alerte; not a real accident.</item>
    /// </list>
    /// Manual reports (uploaded by the user for accidents the system missed) are
    /// inserted directly with status <c>"confirmed"</c>.
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>Id of the admin user who confirmed / dismissed the accident (null while pending).</summary>
    public int? DecidedByUserId { get; set; }

    /// <summary>Moment the decision was taken (null while pending).</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>
    /// Moment <see cref="GisAPI.Services.AccidentTowMonitoringService"/> decided the
    /// vehicle was on the move again (3 consecutive frames &gt; 5 km/h). Null while
    /// the vehicle is still immobilised or the event has not been confirmed.
    /// </summary>
    public DateTime? TowDetectedAt { get; set; }

    // ── Calypso 6 (P9): PDF report + damages capture ───────────────────────

    /// <summary>
    /// Public URL (under the <c>/uploads</c> static-files endpoint) of the
    /// auto-generated or manually-uploaded accident report PDF. Null until
    /// the admin clicks "Confirmer" (auto path) or uploads a file (manual
    /// path). Format: <c>/uploads/accident-reports/{id}/{guid}.pdf</c>.
    /// </summary>
    public string? PdfReportUrl { get; set; }

    /// <summary>
    /// Free-form damages capture filled by the admin after confirmation.
    /// Stored as JSON to avoid a column explosion for fields that may
    /// evolve. Expected shape:
    /// <code>
    /// {
    ///   "description": "...",
    ///   "severity":    "minor" | "moderate" | "severe" | "total",
    ///   "estimatedCost": 1234.56,
    ///   "claimNumber":   "AAS-2026-0123",
    ///   "internalNotes": "...",
    ///   "manualTowDate": "2026-04-27T08:30:00Z"
    /// }
    /// </code>
    /// All fields are optional. Null until the admin saves the damages form.
    /// </summary>
    public string? DamagesJson { get; set; }
}
