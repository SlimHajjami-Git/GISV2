using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.FuelExpenses;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Infrastructure.Services;

/// <summary>
/// Service for calculating fuel consumption and expenses from GPS data
/// </summary>
public class FuelCalculationService : IFuelCalculationService
{
    private readonly IGisDbContext _context;

    public FuelCalculationService(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<VehicleFuelExpenseDto?> CalculateVehicleFuelExpenseAsync(
        Vehicle vehicle,
        DateTime startDate,
        DateTime endDate,
        Dictionary<string, decimal> fuelPrices,
        CancellationToken cancellationToken = default)
    {
        if (!vehicle.GpsDeviceId.HasValue || vehicle.GpsDevice == null)
            return null;

        var tankCapacity = vehicle.FuelTankCapacity ?? 60; // Default 60L if not set
        var sensorMode = vehicle.GpsDevice.FuelSensorMode ?? "raw_255";
        var fuelType = vehicle.FuelType?.ToLower() ?? "diesel";

        // Ensure dates are UTC for PostgreSQL timestamp with time zone
        var startDateUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
        var endDateUtc = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        // Get GPS positions with fuel data
        var deviceId = vehicle.GpsDeviceId.Value;
        var positions = await _context.GpsPositions
            .Where(p => p.DeviceId == deviceId &&
                       p.RecordedAt >= startDateUtc &&
                       p.RecordedAt <= endDateUtc &&
                       p.FuelRaw != null)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new PositionData
            {
                RecordedAt = p.RecordedAt,
                FuelRaw = p.FuelRaw,
                OdometerKm = p.OdometerKm,
                Latitude = p.Latitude,
                Longitude = p.Longitude
            })
            .ToListAsync(cancellationToken);

        if (!positions.Any())
        {
            return new VehicleFuelExpenseDto(
                VehicleId: vehicle.Id,
                VehicleName: vehicle.Name,
                Plate: vehicle.Plate,
                FuelType: vehicle.FuelType,
                FuelTankCapacity: vehicle.FuelTankCapacity,
                TotalFuelConsumedLiters: 0,
                TotalFuelCost: 0,
                AverageConsumptionPer100Km: 0,
                DeviationFromFleetAverage: 0,
                TotalDistanceKm: 0,
                DailyConsumption: new List<DailyFuelConsumptionDto>()
            );
        }

        // Calculate fuel consumption
        decimal totalFuelConsumed = 0;
        var dailyConsumption = new Dictionary<DateTime, (decimal fuel, int distance)>();

        for (int i = 1; i < positions.Count; i++)
        {
            var prev = positions[i - 1];
            var curr = positions[i];

            var prevFuelLiters = ConvertFuelToLiters(prev.FuelRaw, sensorMode, tankCapacity);
            var currFuelLiters = ConvertFuelToLiters(curr.FuelRaw, sensorMode, tankCapacity);

            // Fuel consumed = previous level - current level (if positive, fuel was used)
            var fuelUsed = prevFuelLiters - currFuelLiters;
            
            // Only count positive fuel consumption (negative means refill)
            if (fuelUsed > 0 && fuelUsed < tankCapacity * 0.5m) // Sanity check
            {
                totalFuelConsumed += fuelUsed;

                var date = curr.RecordedAt.Date;
                if (!dailyConsumption.ContainsKey(date))
                    dailyConsumption[date] = (0, 0);

                var distance = 0;
                if (prev.OdometerKm.HasValue && curr.OdometerKm.HasValue)
                    distance = (int)(curr.OdometerKm.Value - prev.OdometerKm.Value);

                var (existingFuel, existingDistance) = dailyConsumption[date];
                dailyConsumption[date] = (existingFuel + fuelUsed, existingDistance + distance);
            }
        }

        // Calculate total distance
        int totalDistance = 0;
        if (positions.First().OdometerKm.HasValue && positions.Last().OdometerKm.HasValue)
        {
            totalDistance = (int)(positions.Last().OdometerKm!.Value - positions.First().OdometerKm!.Value);
        }

        // Get fuel price
        var pricePerLiter = fuelPrices.GetValueOrDefault(fuelType, 0);
        var totalCost = totalFuelConsumed * pricePerLiter;

        // Calculate average consumption per 100km
        var avgConsumption = totalDistance > 0 
            ? (totalFuelConsumed / totalDistance) * 100 
            : 0;

        // Build daily consumption DTOs
        var dailyDtos = dailyConsumption
            .OrderBy(kv => kv.Key)
            .Select(kv => new DailyFuelConsumptionDto(
                Date: kv.Key,
                FuelConsumedLiters: Math.Round(kv.Value.fuel, 2),
                FuelCost: Math.Round(kv.Value.fuel * pricePerLiter, 2),
                DistanceKm: kv.Value.distance,
                ConsumptionPer100Km: kv.Value.distance > 0 
                    ? Math.Round((kv.Value.fuel / kv.Value.distance) * 100, 2) 
                    : 0
            ))
            .ToList();

        return new VehicleFuelExpenseDto(
            VehicleId: vehicle.Id,
            VehicleName: vehicle.Name,
            Plate: vehicle.Plate,
            FuelType: vehicle.FuelType,
            FuelTankCapacity: vehicle.FuelTankCapacity,
            TotalFuelConsumedLiters: Math.Round(totalFuelConsumed, 2),
            TotalFuelCost: Math.Round(totalCost, 2),
            AverageConsumptionPer100Km: Math.Round(avgConsumption, 2),
            DeviationFromFleetAverage: 0, // Will be calculated at fleet level
            TotalDistanceKm: totalDistance,
            DailyConsumption: dailyDtos
        );
    }

    /// <summary>
    /// Convert raw fuel value to liters based on sensor mode and tank capacity
    /// Supports: percent (0-100%), raw_255 (0-255), liters (direct), half_liter
    /// </summary>
    public decimal ConvertFuelToLiters(int? rawValue, string sensorMode, int tankCapacity)
    {
        if (!rawValue.HasValue || tankCapacity <= 0)
            return 0;

        var raw = rawValue.Value;

        return sensorMode.ToLower() switch
        {
            // Percentage mode (0-100%)
            "percent" or "percentage" => (raw / 100m) * tankCapacity,
            
            // Raw 0-255 mode → convert to percentage first, then to liters
            "raw_255" or "raw255" => (raw / 255m) * tankCapacity,
            
            // Direct liters mode
            "liters" or "litres" or "l" => raw,
            
            // Half-liter mode (each unit = 0.5L)
            "half_liter" or "half_liters" => raw * 0.5m,
            
            // Default: treat as raw 0-255
            _ => (raw / 255m) * tankCapacity
        };
    }

    /// <summary>
    /// Detect fuel refill events from GPS position data
    /// A refill is detected when fuel level increases significantly
    /// </summary>
    public async Task<List<FuelRefillEventDto>> DetectFuelRefillsAsync(
        int deviceId,
        int tankCapacity,
        string sensorMode,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        var refills = new List<FuelRefillEventDto>();

        // Ensure dates are UTC for PostgreSQL timestamp with time zone
        var startDateUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
        var endDateUtc = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        // Get vehicle ID for this device
        var vehicleId = await _context.Vehicles
            .Where(v => v.GpsDeviceId == deviceId)
            .Select(v => v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var positions = await _context.GpsPositions
            .Where(p => p.DeviceId == deviceId &&
                       p.RecordedAt >= startDateUtc &&
                       p.RecordedAt <= endDateUtc &&
                       p.FuelRaw != null)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new PositionData
            {
                RecordedAt = p.RecordedAt,
                FuelRaw = p.FuelRaw,
                Latitude = p.Latitude,
                Longitude = p.Longitude
            })
            .ToListAsync(cancellationToken);

        const decimal minRefillThreshold = 5m; // Minimum 5L increase to count as refill

        for (int i = 1; i < positions.Count; i++)
        {
            var prev = positions[i - 1];
            var curr = positions[i];

            var prevFuelLiters = ConvertFuelToLiters(prev.FuelRaw, sensorMode, tankCapacity);
            var currFuelLiters = ConvertFuelToLiters(curr.FuelRaw, sensorMode, tankCapacity);

            var fuelIncrease = currFuelLiters - prevFuelLiters;

            if (fuelIncrease >= minRefillThreshold)
            {
                refills.Add(new FuelRefillEventDto(
                    Timestamp: curr.RecordedAt,
                    VehicleId: vehicleId,
                    FuelAddedLiters: Math.Round(fuelIncrease, 2),
                    EstimatedCost: null, // Will be calculated with fuel prices
                    Latitude: curr.Latitude,
                    Longitude: curr.Longitude
                ));
            }
        }

        return refills;
    }
}

/// <summary>
/// Internal DTO for GPS position data
/// </summary>
internal class PositionData
{
    public DateTime RecordedAt { get; set; }
    public int? FuelRaw { get; set; }
    public long? OdometerKm { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}
