using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// Calypso 7 — unified accident event covering the full multi-day,
/// multi-actor lifecycle of a real-world accident:
///
/// <list type="number">
///   <item><description><b>Detection / Declaration</b> (day 0): MEMS sensor
///     auto-detect or manual admin declaration. Sensor narrative stored in
///     story / reasons / indicators.</description></item>
///   <item><description><b>Confirmation</b> (day 0): admin clicks
///     "Confirmer", initial visible damages captured (description,
///     severity, damaged zones). <b>No cost yet</b> — costs are unknown
///     on day 0 in real life.</description></item>
///   <item><description><b>Expertise</b> (day +N): insurance expert visits,
///     gives written assessment + estimated amount. Expert PDF can be
///     attached.</description></item>
///   <item><description><b>Mechanic quote</b> (day +M): garage provides a
///     repair quote. Quote PDF attached.</description></item>
///   <item><description><b>Repair</b> (day +X): repair starts, completes,
///     actual invoice cost. <b>Auto-syncs to VehicleCost</b> so the
///     /depenses module shows the expense.</description></item>
///   <item><description><b>Insurance settlement</b> (day +Y): claim number,
///     submission date, approved amount, lifecycle status. <b>Auto-syncs
///     to VehicleCost</b> as a refund/credit row.</description></item>
/// </list>
///
/// <para>This entity replaces the old <c>AccidentClaim</c> — manual claim
/// data is migrated in by the EF migration. Multiple documents per
/// accident are tracked in <see cref="AccidentEventDocument"/>; third
/// parties involved in the same accident go in
/// <see cref="AccidentEventThirdParty"/>.</para>
///
/// <para>The frontend timeline component reads this single entity and
/// renders one accordion section per phase, each independently editable
/// at the moment the real-world step happens.</para>
/// </summary>
public class AccidentEvent : TenantEntity
{
    // ── Origin & identification ────────────────────────────────────────────

    /// <summary>
    /// <c>"auto"</c> = MEMS-detected by <c>AccidentDetectionService</c>.
    /// <c>"manual"</c> = admin declared after the fact (system did not
    /// detect, or the accident pre-dates GPS coverage).
    /// </summary>
    public string Origin { get; set; } = "auto";

    public int? VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public int? GpsDeviceId { get; set; }
    public GpsDevice? GpsDevice { get; set; }

    public int? DriverId { get; set; }
    public Driver? Driver { get; set; }

    /// <summary>Raw device_uid (IMEI) — copied at creation.</summary>
    public string DeviceUid { get; set; } = string.Empty;

    public DateTime IncidentAt { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public string? ReferenceCode { get; set; }
    public string? VehicleLabel { get; set; }
    public string? LocationCommune { get; set; }
    public string? LocationGovernorate { get; set; }
    public string? LocationRoadType { get; set; }

    // ── Phase 1: Detection (day 0) — sensor narrative ──────────────────────

    public string? SynthesisText { get; set; }
    public int Confidence { get; set; } = 90;
    public string? StoryJson { get; set; }
    public string? ReasonsJson { get; set; }
    public string? IndicatorsJson { get; set; }
    public string? WeatherConditions { get; set; }
    public string? RoadConditions { get; set; }
    public string? PoliceReportNumber { get; set; }
    public int? MileageAtAccident { get; set; }

    // ── Phase 2: Confirmation (day 0) ─────────────────────────────────────

    /// <summary>
    /// Lifecycle status:
    /// <list type="bullet">
    ///   <item><c>"pending"</c> — auto-detected, awaiting admin decision</item>
    ///   <item><c>"confirmed"</c> — real accident; subsequent phases drive the timeline</item>
    ///   <item><c>"dismissed"</c> — false alarm</item>
    /// </list>
    /// Note Calypso 7 simplifies this — no separate <c>"awaiting_details"</c>
    /// state since each phase tracks its own completeness independently.
    /// </summary>
    public string Status { get; set; } = "pending";
    public int? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }

    /// <summary>Initial damage description filled at confirmation time (visible damage).</summary>
    public string? InitialDescription { get; set; }
    /// <summary>Initial severity perceived by the admin: <c>minor|moderate|severe|total</c>.</summary>
    public string? InitialSeverity { get; set; }
    /// <summary>JSON array of damaged zones (e.g. <c>["Avant","Aile gauche"]</c>).</summary>
    public string? DamagedZonesJson { get; set; }

    // ── Phase 3: Expert assessment (day +N) ───────────────────────────────

    public DateTime? ExpertVisitedAt { get; set; }
    public string? ExpertName { get; set; }
    public string? ExpertCompany { get; set; }
    public string? ExpertAssessment { get; set; }
    public decimal? ExpertEstimatedAmount { get; set; }

    // ── Phase 4: Mechanic quote (day +M) ──────────────────────────────────

    public DateTime? MechanicQuoteAt { get; set; }
    public string? MechanicName { get; set; }
    public decimal? MechanicQuotedAmount { get; set; }

    // ── Phase 5: Repair (day +X) — auto-syncs to VehicleCost ──────────────

    public DateTime? RepairStartedAt { get; set; }
    public DateTime? RepairCompletedAt { get; set; }
    public decimal? ActualRepairCost { get; set; }

    /// <summary>
    /// Auto-monitor (<c>AccidentTowMonitoringService</c>) stamp: the moment
    /// the vehicle resumed motion (3 frames > 5 km/h) within 14 days of
    /// the incident. Indicates the vehicle was towed away from the scene.
    /// </summary>
    public DateTime? TowDetectedAt { get; set; }

    // ── Phase 6: Insurance settlement (day +Y) — auto-syncs to VehicleCost ─

    public string? ClaimNumber { get; set; }
    public DateTime? ClaimSubmittedAt { get; set; }
    public decimal? ClaimApprovedAmount { get; set; }
    /// <summary><c>pending|approved|partial|rejected|closed</c>.</summary>
    public string? ClaimStatus { get; set; }
    public bool ThirdPartyInvolved { get; set; }

    // ── Misc ──────────────────────────────────────────────────────────────

    public string? Witnesses { get; set; }
    public string? AdditionalNotes { get; set; }

    /// <summary>
    /// Auto-generated at confirmation time (frontend jsPDF) — kept for the
    /// "Télécharger le rapport PDF" link on the report header. Phase-specific
    /// PDFs (expert report, mechanic quote, invoice, photos…) are tracked
    /// in <see cref="AccidentEventDocument"/>.
    /// </summary>
    public string? PdfReportUrl { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────

    public ICollection<AccidentEventDocument> Documents { get; set; } = new List<AccidentEventDocument>();
    public ICollection<AccidentEventThirdParty> ThirdParties { get; set; } = new List<AccidentEventThirdParty>();
}

/// <summary>
/// Document attached to an accident, typed by the phase / role:
/// <list type="bullet">
///   <item><c>"detection_pdf"</c> — auto-generated at confirmation (Phase 2)</item>
///   <item><c>"expert_report"</c> — insurance expert PDF (Phase 3)</item>
///   <item><c>"mechanic_quote"</c> — garage quote PDF (Phase 4)</item>
///   <item><c>"repair_invoice"</c> — final invoice (Phase 5)</item>
///   <item><c>"insurance_response"</c> — insurance reply / settlement letter (Phase 6)</item>
///   <item><c>"police_report"</c> — police report (any phase)</item>
///   <item><c>"photo"</c> — accident photos (any phase)</item>
///   <item><c>"other"</c> — fallback</item>
/// </list>
/// Files are stored under <c>uploads/accident-reports/{accidentEventId}/</c>
/// and served as static files at <c>/uploads/accident-reports/...</c>.
/// </summary>
public class AccidentEventDocument : Entity
{
    public int AccidentEventId { get; set; }
    public AccidentEvent? AccidentEvent { get; set; }

    public string DocumentType { get; set; } = "other";
    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public int? FileSize { get; set; }
    public string? MimeType { get; set; }

    public int? UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Other vehicles / parties involved in the same accident. Useful for
/// insurance claims that need to identify counterparts.
/// </summary>
public class AccidentEventThirdParty : Entity
{
    public int AccidentEventId { get; set; }
    public AccidentEvent? AccidentEvent { get; set; }

    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? VehiclePlate { get; set; }
    public string? VehicleModel { get; set; }
    public string? InsuranceCompany { get; set; }
    public string? InsuranceNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
