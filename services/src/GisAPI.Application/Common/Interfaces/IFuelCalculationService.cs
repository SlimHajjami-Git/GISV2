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
}
