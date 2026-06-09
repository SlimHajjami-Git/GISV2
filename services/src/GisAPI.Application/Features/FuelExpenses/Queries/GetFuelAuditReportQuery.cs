using MediatR;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

/// <summary>
/// Per-vehicle fuel audit: cross-checks each billed card fill (FuelEntry) against the actual
/// tank refills detected from the boitier fuel sensor, so we can verify whether each card
/// transaction really went into THIS vehicle's tank (or was used for another vehicle).
/// </summary>
public record GetFuelAuditReportQuery(
    int VehicleId,
    DateTime? StartDate,
    DateTime? EndDate
) : IRequest<FuelAuditReportDto>;

/// <summary>A billed fuel-card fill (FuelEntry) for the vehicle.</summary>
public record CardFillDto(
    DateTime Date,
    decimal Liters,
    decimal Cost,
    string? Station
);

/// <summary>Result of matching one card fill against the GPS-detected refills.</summary>
public record FillCheckDto(
    DateTime? FillDate,         // null = aucun plein declare (remplissage detecte mais non saisi)
    decimal BilledLiters,
    DateTime? MatchedRefillDate,
    decimal? DetectedLiters,
    double? GapHours,
    string Verdict   // "confirme" | "ecart" | "non_detecte" | "non_declare"
);

public record FuelAuditReportDto(
    int VehicleId,
    string VehicleName,
    string? Plate,
    bool HasSensor,
    int TankCapacity,
    DateTime StartDate,
    DateTime EndDate,
    List<FuelLevelPointDto> LevelSeries,
    List<CardFillDto> CardFills,
    List<DetectedRefillDto> DetectedRefills,
    List<FillCheckDto> FillChecks,
    int ConfirmedCount,
    int NotDetectedCount,
    int UndeclaredCount
);
