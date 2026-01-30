using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.AccidentClaims.Commands;

public record UpdateAccidentClaimCommand(
    int Id,
    int? VehicleId,
    int? DriverId,
    DateTime? AccidentDate,
    string? AccidentTime,
    string? Location,
    double? Latitude,
    double? Longitude,
    string? Description,
    string? Severity,
    decimal? EstimatedDamage,
    string[]? DamagedZones,
    bool? ThirdPartyInvolved,
    string? PoliceReportNumber,
    int? MileageAtAccident,
    string? Witnesses,
    string? AdditionalNotes
) : ICommand<bool>;



