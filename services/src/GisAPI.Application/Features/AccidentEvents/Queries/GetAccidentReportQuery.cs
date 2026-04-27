using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.AccidentEvents.Queries;

/// <summary>
/// Fetches the full persisted accident report for a given <c>accident_events.id</c>.
/// Returns <c>null</c> when the row doesn't exist or belongs to another tenant
/// (the global tenant query filter takes care of cross-company leaks).
/// </summary>
public record GetAccidentReportQuery(int AccidentId) : IQuery<AccidentReportDto?>;

public record AccidentReportDto(
    int Id,
    int CompanyId,
    int? VehicleId,
    int? GpsDeviceId,
    string DeviceUid,
    DateTime IncidentAt,
    double Latitude,
    double Longitude,
    string? ReferenceCode,
    string? VehicleLabel,
    string? LocationCommune,
    string? LocationGovernorate,
    string? LocationRoadType,
    string? SynthesisText,
    int Confidence,
    List<AccidentReportStoryEventDto>? Story,
    List<AccidentReportReasonDto>? Reasons,
    List<AccidentReportIndicatorDto>? Indicators,
    // Decision workflow — null/empty until an admin clicks through the
    // modal on /rapport-accident/:id.
    string Status,
    int? DecidedByUserId,
    string? DecidedByName,
    DateTime? DecidedAt,
    DateTime? TowDetectedAt,
    // Calypso 6 (P9): PDF report URL + damages capture.
    string? PdfReportUrl,
    AccidentReportDamagesDto? Damages);

public record AccidentReportDamagesDto(
    string? Description,
    string? Severity,            // "minor" | "moderate" | "severe" | "total"
    decimal? EstimatedCost,
    string? ClaimNumber,
    string? InternalNotes,
    DateTime? ManualTowDate);

public record AccidentReportStoryEventDto(
    string Time,
    string Title,
    string Body,
    string Severity);

public record AccidentReportReasonDto(
    string Title,
    string Text);

public record AccidentReportIndicatorDto(
    string Label,
    string Value,
    string? Hint);
