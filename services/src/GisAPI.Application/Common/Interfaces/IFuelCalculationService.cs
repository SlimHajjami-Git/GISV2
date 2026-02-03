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
    /// Convert raw fuel value to liters based on sensor mode and tank capacity
    /// </summary>
    decimal ConvertFuelToLiters(int? rawValue, string sensorMode, int tankCapacity);

    /// <summary>
    /// Detect fuel refill events from GPS position data
    /// </summary>
    Task<List<FuelRefillEventDto>> DetectFuelRefillsAsync(
        int deviceId,
        int tankCapacity,
        string sensorMode,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);
}
