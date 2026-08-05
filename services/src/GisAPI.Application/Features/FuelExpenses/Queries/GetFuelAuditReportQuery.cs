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

/// <summary>
/// Result of matching one card fill against the GPS-detected refills.
/// DetectedLiters est un CENTRE de fourchette (Low/High), jamais une vérité au
/// litre : la jauge mesure des points (~1 % de cuve) et la conversion en litres
/// porte l'incertitude de la géométrie du réservoir. Afficher le chiffre sec
/// a déjà fait accuser un chauffeur à tort (Scania 001 : 438 L pompe « détecté
/// 390 L » — capacité de fiche fausse, pas de gazole manquant).
/// </summary>
public record FillCheckDto(
    DateTime? FillDate,         // null = aucun plein declare (remplissage detecte mais non saisi)
    decimal BilledLiters,
    DateTime? MatchedRefillDate,
    decimal? DetectedLiters,
    double? GapHours,
    string Verdict,  // "confirme" | "ecart" | "non_detecte" | "non_declare" | "volume_non_saisi"
    decimal? DetectedLitersLow = null,
    decimal? DetectedLitersHigh = null,
    int? DeltaPoints = null
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
    int UndeclaredCount,
    // ── Synthèse : LE chiffre que le gestionnaire cherche, en tête de rapport ──
    decimal TotalBilledLiters = 0,
    decimal TotalDetectedLiters = 0,
    decimal UndeclaredLiters = 0,
    decimal? CoveragePercent = null,        // facturé / détecté ; null sans détection
    decimal? EstimatedUndeclaredCost = null, // au prix moyen des factures de la période
    // ── Étalonnage : d'où vient la conversion points→litres ──
    bool IsCalibrated = false,
    int CalibrationPointCount = 0,
    int? EffectiveTankLiters = null
);
