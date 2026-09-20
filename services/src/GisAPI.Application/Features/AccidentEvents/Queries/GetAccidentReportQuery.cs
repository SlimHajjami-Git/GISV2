using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.AccidentEvents.Queries;

/// <summary>
/// Calypso 7 — fetches the full timeline-aware accident report.
/// Returns the parent <see cref="AccidentReportDto"/> plus the
/// associated documents and third parties.
///
/// Returns <c>null</c> when the row doesn't exist or belongs to
/// another tenant (the global tenant filter takes care of this).
/// </summary>
public record GetAccidentReportQuery(int AccidentId) : IQuery<AccidentReportDto?>;

public record AccidentReportDto(
    int Id,
    int CompanyId,
    string Origin,
    int? VehicleId,
    int? GpsDeviceId,
    int? DriverId,
    string DeviceUid,
    DateTime IncidentAt,
    double Latitude,
    double Longitude,
    string? ReferenceCode,
    string? VehicleLabel,
    string? LocationCommune,
    string? LocationGovernorate,
    string? LocationRoadType,

    // Phase 1 — Detection / sensor
    string? SynthesisText,
    int Confidence,
    List<AccidentReportStoryEventDto>? Story,
    List<AccidentReportReasonDto>? Reasons,
    List<AccidentReportIndicatorDto>? Indicators,
    string? WeatherConditions,
    string? RoadConditions,
    string? PoliceReportNumber,
    int? MileageAtAccident,

    // Phase 2 — Confirmation
    string Status,
    int? DecidedByUserId,
    string? DecidedByName,
    DateTime? DecidedAt,
    string? InitialDescription,
    string? InitialSeverity,
    List<string>? DamagedZones,

    // Phase 3 — Expert
    DateTime? ExpertVisitedAt,
    string? ExpertName,
    string? ExpertCompany,
    string? ExpertAssessment,
    decimal? ExpertEstimatedAmount,

    // Phase 4 — Mechanic quote
    DateTime? MechanicQuoteAt,
    string? MechanicName,
    decimal? MechanicQuotedAmount,

    // Phase 5 — Repair
    DateTime? RepairStartedAt,
    DateTime? RepairCompletedAt,
    decimal? ActualRepairCost,
    DateTime? TowDetectedAt,

    // Phase 6 — Insurance settlement
    string? ClaimNumber,
    DateTime? ClaimSubmittedAt,
    decimal? ClaimApprovedAmount,
    string? ClaimStatus,
    bool ThirdPartyInvolved,

    // Misc
    string? Witnesses,
    string? AdditionalNotes,
    string? PdfReportUrl,

    // Children
    List<AccidentReportDocumentDto> Documents,
    List<AccidentReportThirdPartyDto> ThirdParties,

    /// <summary>
    /// Dernière modification du dossier, pour signaler un PDF plus ancien que les
    /// informations qu'il est censé porter.
    /// </summary>
    DateTime? UpdatedAt = null,

    /// <summary>
    /// Le dossier est rattaché à un véhicule qui existe encore. Un sinistre survit à la
    /// suppression de son véhicule — la cascade met <c>vehicle_id</c> à NULL après avoir
    /// figé <c>vehicle_label</c> — et ni la réparation ni le remboursement ne peuvent
    /// alors être reportés. FAUX aussi quand le dossier n'a jamais eu de véhicule :
    /// l'écran dit la même chose dans les deux cas, les montants ne sortent pas du
    /// dossier.
    /// </summary>
    bool VehicleExists = true,

    /// <summary>Référence de la réparation créée par la phase 5, si elle existe.</summary>
    string? RepairReference = null);

public record AccidentReportStoryEventDto(string Time, string Title, string Body, string Severity);
public record AccidentReportReasonDto(string Title, string Text);
public record AccidentReportIndicatorDto(string Label, string Value, string? Hint);

public record AccidentReportDocumentDto(
    int Id,
    string DocumentType,
    string FileName,
    string FileUrl,
    int? FileSize,
    string? MimeType,
    DateTime UploadedAt);

public record AccidentReportThirdPartyDto(
    int Id,
    string? Name,
    string? Phone,
    string? VehiclePlate,
    string? VehicleModel,
    string? InsuranceCompany,
    string? InsuranceNumber,
    DateTime? InsuranceExpiry);
