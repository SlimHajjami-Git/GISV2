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

    // Default consumption rates (L/100km) by vehicle type for estimation when no sensor data
    private static readonly Dictionary<string, decimal> DefaultConsumptionRates = new(StringComparer.OrdinalIgnoreCase)
    {
        { "citadine", 6.5m },
        { "berline", 7.5m },
        { "suv", 9.0m },
        { "camion", 25.0m },
        { "camionnette", 10.0m },
        { "fourgon", 11.0m },
        { "utilitaire", 10.0m },
        { "bus", 30.0m },
        { "moto", 4.0m },
        { "pickup", 11.0m },
        { "van", 9.5m },
        { "minibus", 15.0m }
    };

    public async Task<VehicleFuelExpenseDto?> CalculateVehicleFuelExpenseAsync(
        Vehicle vehicle,
        DateTime startDate,
        DateTime endDate,
        Dictionary<string, decimal> fuelPrices,
        CancellationToken cancellationToken = default)
    {
        if (!vehicle.GpsDeviceId.HasValue || vehicle.GpsDevice == null)
            return null;

        var tankCapacity = vehicle.FuelTankCapacity ?? 60;
        var sensorMode = vehicle.GpsDevice.FuelSensorMode ?? "raw_255";
        var fuelType = vehicle.FuelType?.ToLower() ?? "diesel";
        var vehicleType = vehicle.Type?.ToLower() ?? "berline";

        var startDateUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
        var endDateUtc = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        var deviceId = vehicle.GpsDeviceId.Value;

        // Query ALL positions (not just ones with FuelRaw) to calculate distance
        var allPositions = await _context.GpsPositions
            .Where(p => p.DeviceId == deviceId &&
                       p.RecordedAt >= startDateUtc &&
                       p.RecordedAt <= endDateUtc)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new PositionData
            {
                RecordedAt = p.RecordedAt,
                FuelRaw = p.FuelRaw,
                OdometerKm = p.OdometerKm,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                SpeedKph = p.SpeedKph
            })
            .ToListAsync(cancellationToken);

        if (!allPositions.Any())
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
                IsEstimated: true,
                DailyConsumption: new List<DailyFuelConsumptionDto>()
            );
        }

        // Calculate total distance from odometer or Haversine
        int totalDistance = CalculateTotalDistance(allPositions);

        // Separate positions that have fuel data and check if values actually vary
        var fuelPositions = allPositions.Where(p => p.FuelRaw.HasValue).ToList();
        var distinctFuelValues = fuelPositions.Select(p => p.FuelRaw!.Value).Distinct().Count();
        // Sensor data is only useful if we have >=2 positions with >=2 distinct fuel values
        bool hasSensorData = fuelPositions.Count >= 2 && distinctFuelValues >= 2;

        decimal totalFuelConsumed = 0;
        decimal avgConsumption = 0;
        bool useEstimation = false;
        var dailyConsumption = new Dictionary<DateTime, (decimal fuel, int distance)>();

        if (hasSensorData)
        {
            // ===== MODE 1: Use actual sensor data =====
            for (int i = 1; i < fuelPositions.Count; i++)
            {
                var prev = fuelPositions[i - 1];
                var curr = fuelPositions[i];

                var prevFuelLiters = ConvertFuelToLiters(prev.FuelRaw, sensorMode, tankCapacity);
                var currFuelLiters = ConvertFuelToLiters(curr.FuelRaw, sensorMode, tankCapacity);

                var fuelUsed = prevFuelLiters - currFuelLiters;
                
                if (fuelUsed > 0 && fuelUsed < tankCapacity * 0.5m)
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

            avgConsumption = totalDistance > 0 
                ? (totalFuelConsumed / totalDistance) * 100 
                : 0;

            // Fallback: if sensor gave 0 consumption but vehicle actually moved, use estimation
            if (totalFuelConsumed == 0 && totalDistance > 5)
            {
                useEstimation = true;
                dailyConsumption.Clear();
            }
        }
        else
        {
            useEstimation = true;
        }

        if (useEstimation && totalDistance > 0)
        {
            // ===== MODE 2: Estimate from distance using default consumption rate =====
            avgConsumption = DefaultConsumptionRates.GetValueOrDefault(vehicleType, 8.0m);
            totalFuelConsumed = (avgConsumption / 100m) * totalDistance;

            // Build daily breakdown from all positions
            for (int i = 1; i < allPositions.Count; i++)
            {
                var prev = allPositions[i - 1];
                var curr = allPositions[i];
                var date = curr.RecordedAt.Date;

                var segmentDist = CalculateSegmentDistance(prev, curr);
                if (segmentDist <= 0) continue;

                var segmentFuel = (avgConsumption / 100m) * segmentDist;

                if (!dailyConsumption.ContainsKey(date))
                    dailyConsumption[date] = (0, 0);

                var (existingFuel, existingDistance) = dailyConsumption[date];
                dailyConsumption[date] = (existingFuel + segmentFuel, existingDistance + (int)segmentDist);
            }
        }

        // Get fuel price and calculate cost
        var pricePerLiter = fuelPrices.GetValueOrDefault(fuelType, 0);
        var totalCost = totalFuelConsumed * pricePerLiter;

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
            DeviationFromFleetAverage: 0,
            TotalDistanceKm: totalDistance,
            IsEstimated: useEstimation,
            DailyConsumption: dailyDtos
        );
    }

    /// <summary>
    /// Calculate total distance from odometer (preferred) or Haversine fallback
    /// </summary>
    private int CalculateTotalDistance(List<PositionData> positions)
    {
        if (positions.Count < 2) return 0;

        // Try odometer first
        var first = positions.First();
        var last = positions.Last();
        if (first.OdometerKm.HasValue && last.OdometerKm.HasValue && last.OdometerKm.Value > first.OdometerKm.Value)
        {
            return (int)(last.OdometerKm.Value - first.OdometerKm.Value);
        }

        // Fallback: sum Haversine distances between consecutive positions where speed > 0
        double totalKm = 0;
        for (int i = 1; i < positions.Count; i++)
        {
            var prev = positions[i - 1];
            var curr = positions[i];
            var dist = HaversineKm(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
            // Only count reasonable segments (< 50km between consecutive points, speed > 0)
            if (dist > 0.01 && dist < 50 && (curr.SpeedKph ?? 0) > 2)
            {
                totalKm += dist;
            }
        }
        return (int)Math.Round(totalKm);
    }

    /// <summary>
    /// Calculate segment distance between two consecutive positions
    /// </summary>
    private decimal CalculateSegmentDistance(PositionData prev, PositionData curr)
    {
        if (prev.OdometerKm.HasValue && curr.OdometerKm.HasValue && curr.OdometerKm.Value > prev.OdometerKm.Value)
        {
            return (decimal)(curr.OdometerKm.Value - prev.OdometerKm.Value);
        }
        var dist = HaversineKm(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
        if (dist > 0.01 && dist < 50 && (curr.SpeedKph ?? 0) > 2)
            return (decimal)dist;
        return 0;
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
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
    public double? SpeedKph { get; set; }
}
