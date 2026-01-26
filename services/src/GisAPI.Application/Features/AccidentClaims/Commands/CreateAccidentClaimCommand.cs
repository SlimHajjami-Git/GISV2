using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.AccidentClaims.Commands;

public record CreateAccidentClaimCommand(
    int VehicleId,
    int? DriverId,
    DateTime AccidentDate,
    string AccidentTime,
    string Location,
    double? Latitude,
    double? Longitude,
    string Description,
    string Severity,
    decimal EstimatedDamage,
    string[]? DamagedZones,
    bool ThirdPartyInvolved,
    string? ThirdPartyName,
    string? ThirdPartyPhone,
    string? ThirdPartyVehiclePlate,
    string? ThirdPartyVehicleModel,
    string? ThirdPartyInsurance,
    string? ThirdPartyInsuranceNumber,
    string? PoliceReportNumber,
    int? MileageAtAccident,
    string? Witnesses,
    string? AdditionalNotes
) : ICommand<int>;
