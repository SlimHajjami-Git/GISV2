namespace GisAPI.Application.Features.FuelExpenses;

/// <summary>
/// Un segment de X km avec sa consommation mesurée par la jauge (ratchet étalonné).
/// IsReliable=false quand le segment porte une signature de capteur défaillant
/// (falaise à l'arrêt, jauge figée, consommation invraisemblable) — il est alors
/// affiché mais exclu des min/max/moyenne.
/// </summary>
public record ConsumptionSegmentDto(
    int Index,
    DateTime StartTime,
    DateTime EndTime,
    decimal DistanceKm,
    decimal FuelLiters,
    decimal LPer100Km,
    decimal? TonnageT,
    bool IsReliable,
    string? ExclusionReason);

public record ConsumptionSegmentsSummaryDto(
    decimal TotalKm,
    decimal TotalLiters,
    decimal? AvgLPer100Km,
    decimal? MinLPer100Km,
    int? MinSegmentIndex,
    decimal? MaxLPer100Km,
    int? MaxSegmentIndex,
    int ReliableSegments,
    int ExcludedSegments);

public record ConsumptionSegmentsReportDto(
    int VehicleId,
    string VehicleName,
    int SegmentKm,
    bool HasSensor,
    decimal LitersPerPoint,
    bool IsCalibrated,
    List<ConsumptionSegmentDto> Segments,
    ConsumptionSegmentsSummaryDto Summary);

/// <summary>Consommation agrégée des segments partageant le même tonnage déclaré.</summary>
public record TonnageGroupDto(
    decimal? TonnageT,
    int SegmentCount,
    decimal TotalKm,
    decimal AvgLPer100Km,
    decimal MinLPer100Km,
    decimal MaxLPer100Km,
    decimal? DeltaVsLightestPercent);

public record ConsumptionByTonnageReportDto(
    int VehicleId,
    string VehicleName,
    int SegmentKm,
    List<TonnageGroupDto> Groups);

public record VehicleLoadPeriodDto(
    int Id,
    int VehicleId,
    DateTime StartTime,
    DateTime? EndTime,
    decimal TonnageT,
    string? Notes);
