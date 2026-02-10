using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.FuelExpenses;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Infrastructure.Services;

/// <summary>
/// Service for calculating fuel consumption and expenses.
/// Primary data source: fuel_records table (populated by Rust GPS ingest).
/// Fallback: distance-based estimation using default L/100km rates.
/// </summary>
public class FuelCalculationService : IFuelCalculationService
{
    private readonly IGisDbContext _context;

    public FuelCalculationService(IGisDbContext context)
    {
        _context = context;
    }

    // Default consumption rates (L/100km) by vehicle type for estimation fallback
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
        var fuelType = vehicle.FuelType?.ToLower() ?? "diesel";
        var vehicleType = vehicle.Type?.ToLower() ?? "berline";

        var startDateUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
        var endDateUtc = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        // ===== 1. Get fuel_records for this vehicle (primary data source) =====
        var fuelRecords = await _context.FuelRecords
            .Where(fr => fr.VehicleId == vehicle.Id &&
                         fr.RecordedAt >= startDateUtc &&
                         fr.RecordedAt <= endDateUtc)
            .OrderBy(fr => fr.RecordedAt)
            .ToListAsync(cancellationToken);

        // Deduplicate: keep only one record per (recorded_at, fuel_percent) combo
        var deduped = fuelRecords
            .GroupBy(fr => new { fr.RecordedAt, fr.FuelPercent })
            .Select(g => g.First())
            .OrderBy(fr => fr.RecordedAt)
            .ToList();

        // ===== 2. Calculate distance: always use GPS positions (most reliable) =====
        // fuel_records odometer only captures snapshots during fuel events — not the full trip distance.
        // GPS positions cover the entire period, so always compute from there.
        var hasFuelRecords = deduped.Count >= 2;
        int totalDistance = await CalculateDistanceFromGps(
            vehicle.GpsDeviceId.Value, startDateUtc, endDateUtc, hasFuelRecords, cancellationToken);

        // ===== 3. Calculate fuel consumption + detect refuels from fuel_records =====
        decimal totalFuelConsumedPercent = 0;
        bool hasFuelData = false;
        var dailyConsumption = new Dictionary<DateTime, (decimal fuelPercent, int distance)>();
        var refuels = new List<FuelRefillEventDto>();

        if (deduped.Count >= 2)
        {
            for (int i = 1; i < deduped.Count; i++)
            {
                var prev = deduped[i - 1];
                var curr = deduped[i];

                // Detect refuel: fuel went up significantly
                if (curr.FuelPercent > prev.FuelPercent + 3) // +3% threshold to avoid noise
                {
                    var addedPercent = curr.FuelPercent - prev.FuelPercent;
                    var addedLiters = (addedPercent / 100m) * tankCapacity;
                    var priceForRefuel = fuelPrices.GetValueOrDefault(fuelType, 0);
                    refuels.Add(new FuelRefillEventDto(
                        Timestamp: curr.RecordedAt,
                        VehicleId: vehicle.Id,
                        FuelAddedLiters: Math.Round(addedLiters, 2),
                        EstimatedCost: Math.Round(addedLiters * priceForRefuel, 2),
                        Latitude: curr.Latitude,
                        Longitude: curr.Longitude
                    ));
                    continue;
                }

                var drop = prev.FuelPercent - curr.FuelPercent;
                if (drop > 0 && drop < 80) // Sanity: ignore drops > 80% (sensor error)
                {
                    totalFuelConsumedPercent += drop;
                    hasFuelData = true;

                    var date = curr.RecordedAt.Date;
                    if (!dailyConsumption.ContainsKey(date))
                        dailyConsumption[date] = (0, 0);

                    var segDist = 0;
                    if (prev.OdometerKm.HasValue && curr.OdometerKm.HasValue)
                        segDist = (int)(curr.OdometerKm.Value - prev.OdometerKm.Value);

                    var (existPct, existDist) = dailyConsumption[date];
                    dailyConsumption[date] = (existPct + drop, existDist + segDist);
                }
            }
        }

        // Convert percent consumed to liters
        decimal totalFuelConsumedLiters = (totalFuelConsumedPercent / 100m) * tankCapacity;
        decimal avgConsumption = 0;
        bool useEstimation = false;

        if (hasFuelRecords)
        {
            // Fuel sensor data exists — trust it, even if consumption is 0
            // (e.g. vehicle refueled and hasn't consumed much since)
            avgConsumption = totalDistance > 0 && totalFuelConsumedLiters > 0
                ? (totalFuelConsumedLiters / totalDistance) * 100
                : 0;
        }
        else if (totalDistance > 0)
        {
            // No fuel records at all → estimate from distance and default rate
            useEstimation = true;
            avgConsumption = DefaultConsumptionRates.GetValueOrDefault(vehicleType, 8.0m);
            totalFuelConsumedLiters = (avgConsumption / 100m) * totalDistance;

            // Build daily from GPS positions
            dailyConsumption = await BuildDailyFromGps(
                vehicle.GpsDeviceId.Value, startDateUtc, endDateUtc, avgConsumption, cancellationToken);
        }

        // ===== 4. Apply fuel price =====
        var pricePerLiter = fuelPrices.GetValueOrDefault(fuelType, 0);
        var totalCost = totalFuelConsumedLiters * pricePerLiter;

        // ===== 5. Build daily DTOs =====
        var dailyDtos = dailyConsumption
            .OrderBy(kv => kv.Key)
            .Select(kv =>
            {
                var fuelLiters = useEstimation
                    ? kv.Value.fuelPercent  // Already in liters for estimation mode
                    : (kv.Value.fuelPercent / 100m) * tankCapacity;
                return new DailyFuelConsumptionDto(
                    Date: kv.Key,
                    FuelConsumedLiters: Math.Round(fuelLiters, 2),
                    FuelCost: Math.Round(fuelLiters * pricePerLiter, 2),
                    DistanceKm: kv.Value.distance,
                    ConsumptionPer100Km: kv.Value.distance > 0
                        ? Math.Round((fuelLiters / kv.Value.distance) * 100, 2)
                        : 0
                );
            })
            .ToList();

        return new VehicleFuelExpenseDto(
            VehicleId: vehicle.Id,
            VehicleName: vehicle.Name,
            Plate: vehicle.Plate,
            FuelType: vehicle.FuelType,
            FuelTankCapacity: vehicle.FuelTankCapacity,
            TotalFuelConsumedLiters: Math.Round(totalFuelConsumedLiters, 2),
            TotalFuelCost: Math.Round(totalCost, 2),
            AverageConsumptionPer100Km: Math.Round(avgConsumption, 2),
            DeviationFromFleetAverage: 0,
            TotalDistanceKm: totalDistance,
            IsEstimated: useEstimation,
            DailyConsumption: dailyDtos,
            Refuels: refuels
        );
    }

    /// <summary>
    /// Calculate distance from GPS positions.
    /// Computes both odometer-based and Haversine-based distances, returns the larger value
    /// to avoid undercounting from sparse odometer snapshots or filtered Haversine segments.
    /// </summary>
    private async Task<int> CalculateDistanceFromGps(
        int deviceId, DateTime startUtc, DateTime endUtc, bool trustOdometer, CancellationToken ct)
    {
        var positions = await _context.GpsPositions
            .Where(p => p.DeviceId == deviceId && p.RecordedAt >= startUtc && p.RecordedAt <= endUtc)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new { p.OdometerKm, p.Latitude, p.Longitude, p.SpeedKph })
            .ToListAsync(ct);

        if (positions.Count < 2) return 0;

        // Always compute Haversine distance
        double haversineKm = 0;
        for (int i = 1; i < positions.Count; i++)
        {
            var prev = positions[i - 1];
            var curr = positions[i];
            var dist = HaversineKm(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
            if (dist > 0.01 && dist < 50 && (curr.SpeedKph ?? 0) > 2)
                haversineKm += dist;
        }

        int haversineDist = (int)Math.Round(haversineKm);

        // For L-type devices, also try odometer and take the larger value
        if (trustOdometer)
        {
            var first = positions.First();
            var last = positions.Last();
            if (first.OdometerKm.HasValue && last.OdometerKm.HasValue && last.OdometerKm.Value > first.OdometerKm.Value)
            {
                int odometerDist = (int)(last.OdometerKm.Value - first.OdometerKm.Value);
                return Math.Max(odometerDist, haversineDist);
            }
        }

        return haversineDist;
    }

    /// <summary>
    /// Build daily consumption breakdown from GPS positions for estimation mode
    /// </summary>
    private async Task<Dictionary<DateTime, (decimal fuelPercent, int distance)>> BuildDailyFromGps(
        int deviceId, DateTime startUtc, DateTime endUtc, decimal avgConsumption, CancellationToken ct)
    {
        var daily = new Dictionary<DateTime, (decimal fuelPercent, int distance)>();

        var positions = await _context.GpsPositions
            .Where(p => p.DeviceId == deviceId && p.RecordedAt >= startUtc && p.RecordedAt <= endUtc)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new { p.RecordedAt, p.OdometerKm, p.Latitude, p.Longitude, p.SpeedKph })
            .ToListAsync(ct);

        for (int i = 1; i < positions.Count; i++)
        {
            var prev = positions[i - 1];
            var curr = positions[i];
            var date = curr.RecordedAt.Date;

            // Estimation mode = S-type device → odometer is garbage, always use Haversine
            decimal segDist = 0;
            var h = HaversineKm(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
            if (h > 0.01 && h < 50 && (curr.SpeedKph ?? 0) > 2)
                segDist = (decimal)h;

            if (segDist <= 0) continue;

            // In estimation mode, fuelPercent field stores liters directly
            var segFuelLiters = (avgConsumption / 100m) * segDist;

            if (!daily.ContainsKey(date))
                daily[date] = (0, 0);

            var (existFuel, existDist) = daily[date];
            daily[date] = (existFuel + segFuelLiters, existDist + (int)segDist);
        }

        return daily;
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
    /// Detect fuel refill events from fuel_records table
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

        var startDateUtc = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
        var endDateUtc = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        var vehicleId = await _context.Vehicles
            .Where(v => v.GpsDeviceId == deviceId)
            .Select(v => v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (vehicleId == 0) return refills;

        var records = await _context.FuelRecords
            .Where(fr => fr.VehicleId == vehicleId &&
                         fr.RecordedAt >= startDateUtc &&
                         fr.RecordedAt <= endDateUtc &&
                         fr.EventType == "refuel")
            .GroupBy(fr => new { fr.RecordedAt, fr.FuelPercent })
            .Select(g => g.First())
            .OrderBy(fr => fr.RecordedAt)
            .ToListAsync(cancellationToken);

        foreach (var record in records)
        {
            if (record.FuelChange.HasValue && record.FuelChange.Value > 0)
            {
                var litersAdded = (record.FuelChange.Value / 100m) * tankCapacity;
                refills.Add(new FuelRefillEventDto(
                    Timestamp: record.RecordedAt,
                    VehicleId: vehicleId,
                    FuelAddedLiters: Math.Round(litersAdded, 2),
                    EstimatedCost: record.RefuelCost,
                    Latitude: record.Latitude,
                    Longitude: record.Longitude
                ));
            }
        }

        return refills;
    }
}
