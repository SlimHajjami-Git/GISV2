using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;

namespace GisAPI.Application.Features.AccidentClaims.Queries;

public record GetAccidentClaimsQuery(
    string? SearchTerm = null,
    string? Status = null,
    string? Severity = null,
    int? VehicleId = null,
    int Page = 1,
    int PageSize = 50
) : IQuery<PaginatedList<AccidentClaimDto>>;

public record AccidentClaimDto(
    int Id,
    string ClaimNumber,
    int VehicleId,
    string VehicleName,
    string? VehiclePlate,
    int? DriverId,
    string? DriverName,
    DateTime AccidentDate,
    string AccidentTime,
    string Location,
    double? Latitude,
    double? Longitude,
    string Description,
    string Severity,
    decimal EstimatedDamage,
    decimal? ApprovedAmount,
    string Status,
    bool ThirdPartyInvolved,
    string? PoliceReportNumber,
    int? MileageAtAccident,
    string[]? DamagedZones,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<AccidentClaimThirdPartyDto> ThirdParties,
    List<AccidentClaimDocumentDto> Documents
);

public record AccidentClaimThirdPartyDto(
    int Id,
    string? Name,
    string? Phone,
    string? VehiclePlate,
    string? VehicleModel,
    string? InsuranceCompany,
    string? InsuranceNumber
);

public record AccidentClaimDocumentDto(
    int Id,
    string DocumentType,
    string FileName,
    string FileUrl,
    int? FileSize,
    string? MimeType,
    DateTime UploadedAt
);

public record AccidentClaimStatsDto(
    int TotalClaims,
    int DraftCount,
    int SubmittedCount,
    int UnderReviewCount,
    int ApprovedCount,
    int RejectedCount,
    int ClosedCount,
    decimal TotalEstimatedDamage,
    decimal TotalApprovedAmount
);
