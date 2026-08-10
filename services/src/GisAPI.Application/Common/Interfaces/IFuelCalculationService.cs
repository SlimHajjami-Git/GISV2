using GisAPI.Application.Common.FuelCalibration;
using GisAPI.Application.Features.FuelExpenses;
using GisAPI.Domain.Entities;

namespace GisAPI.Application.Common.Interfaces;

/// <summary>
/// Service for calculating fuel consumption and expenses from GPS data
/// </summary>
public interface IFuelCalculationService
{
    /// <summary>
    /// Calculate fuel expense for a vehicle based on GPS fuel data
    /// </summary>
    Task<VehicleFuelExpenseDto?> CalculateVehicleFuelExpenseAsync(
        Vehicle vehicle,
        DateTime startDate,
        DateTime endDate,
        Dictionary<string, decimal> fuelPrices,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch-calculate fuel stats for all vehicles in a fleet (dashboard optimization).
    /// Uses 3 SQL queries instead of N*4 per-vehicle queries.
    /// </summary>
    Task<List<VehicleFuelExpenseDto>> CalculateFleetFuelBatchAsync(
        List<Vehicle> vehicles,
        DateTime startDate,
        DateTime endDate,
        Dictionary<string, decimal> fuelPrices,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detect fuel refill events from fuel_records
    /// </summary>
    Task<List<FuelRefillEventDto>> DetectFuelRefillsAsync(
        int deviceId,
        int tankCapacity,
        string sensorMode,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Per-vehicle fuel-level audit from the boitier sensor: the fuel-level curve over time
    /// (litres) plus the detected refills (level jumps >= 10%). Used to verify each billed
    /// card fill against an actual tank refill. Returns HasSensor=false when the vehicle has
    /// no usable fuel sensor (no/garbage fuel_raw data).
    /// </summary>
    Task<FuelLevelAuditDto> GetFuelLevelAuditAsync(
        Vehicle vehicle,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consommation par tranches de X km depuis la jauge (ratchet étalonné).
    /// Distance = odomètre quand sain, sinon Haversine. Les segments portant
    /// une signature de capteur défaillant sont retournés marqués non fiables.
    /// TonnageT est laissé null ici — le handler l'enrichit depuis les périodes
    /// de chargement déclarées.
    /// </summary>
    Task<ConsumptionSegmentsReportDto?> GetConsumptionSegmentsAsync(
        Vehicle vehicle,
        DateTime startDate,
        DateTime endDate,
        int segmentKm,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Étalonnage points-de-jauge → litres du véhicule, appris de ses pleins
    /// FACTURÉS rapprochés des montées de jauge (12 derniers mois). Tant que
    /// moins de 4 pleins cohérents existent, retourne la conversion nominale
    /// (capacité/100) avec une incertitude large — jamais une fausse précision.
    /// </summary>
    Task<TankCalibrationResult> GetTankCalibrationAsync(
        Vehicle vehicle,
        CancellationToken cancellationToken = default);
}
