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

        var startDateUtc = startDate.Kind == DateTimeKind.Utc ? startDate : startDate.ToUniversalTime();
        var endDateUtc = endDate.Kind == DateTimeKind.Utc ? endDate : endDate.ToUniversalTime();

        // ===== 1. Get GPS positions with fuel data (primary source — granular per-position) =====
        // fuel_records table only has significant events (refuels, theft, spikes).
        // gps_positions.fuelRaw has every 1% change — much more accurate for consumption.
        var rawPositions = await _context.GpsPositions
            .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value &&
                        p.RecordedAt >= startDateUtc &&
                        p.RecordedAt <= endDateUtc &&
                        p.FuelRaw != null && p.FuelRaw >= 0 && p.FuelRaw <= 100)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new FuelPositionSlim
            {
                RecordedAt = p.RecordedAt,
                FuelPercent = p.FuelRaw!.Value,
                OdometerKm = p.OdometerKm,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                SpeedKph = p.SpeedKph ?? 0
            })
            .ToListAsync(cancellationToken);

        // ===== 2. Binary oscillation detection: broken sensors that only report 0 and 100 =====
        if (rawPositions.Count >= 4)
        {
            var extremeCount = rawPositions.Count(p => p.FuelPercent <= 2 || p.FuelPercent >= 98);
            var extremeRatio = (double)extremeCount / rawPositions.Count;
            var largeSwings = 0;
            for (int i = 1; i < rawPositions.Count; i++)
            {
                if (Math.Abs(rawPositions[i].FuelPercent - rawPositions[i - 1].FuelPercent) > 50)
                    largeSwings++;
            }
            var swingRatio = (double)largeSwings / (rawPositions.Count - 1);

            if (extremeRatio > 0.6 && swingRatio > 0.1)
            {
                // Sensor is garbage (0↔100 oscillation) — fall back to distance-based estimation
                rawPositions.Clear();
            }
        }

        // ===== 3. Spike filter: remove isolated sensor glitches =====
        var positions = FilterFuelSpikes(rawPositions);

        // ===== 4. Calculate distance from GPS positions =====
        int totalDistance = positions.Count >= 2
            ? CalculateDistanceFromPositions(positions, startDateUtc, endDateUtc)
            : await CalculateDistanceFromGps(vehicle.GpsDeviceId.Value, startDateUtc, endDateUtc, true, cancellationToken);

        // ===== 4. Walk through fuel level changes → consumption + refuels =====
        decimal totalFuelConsumedPercent = 0;
        bool hasFuelData = false;
        var dailyConsumption = new Dictionary<DateTime, (decimal fuelPercent, int distance)>();
        var refuels = new List<FuelRefillEventDto>();

        // Collapse consecutive positions with same fuel level (keep first and last)
        var fuelChanges = CollapseFuelReadings(positions);

        if (fuelChanges.Count >= 2)
        {
            for (int i = 1; i < fuelChanges.Count; i++)
            {
                var prev = fuelChanges[i - 1];
                var curr = fuelChanges[i];
                var fuelDelta = curr.FuelPercent - prev.FuelPercent;

                // Detect refuel: fuel went up >= 10%
                if (fuelDelta >= 10)
                {
                    var addedLiters = (fuelDelta / 100m) * tankCapacity;
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

                // Skip small positive changes (+1 to +4%): sensor noise
                if (fuelDelta > 0)
                    continue;

                var drop = -fuelDelta; // Make positive
                if (drop > 0 && drop < 50) // Ignore drops > 50% (sensor error/disconnect)
                {
                    totalFuelConsumedPercent += drop;
                    hasFuelData = true;

                    var date = curr.RecordedAt.Date;
                    if (!dailyConsumption.ContainsKey(date))
                        dailyConsumption[date] = (0, 0);

                    var segDist = 0;
                    if (prev.OdometerKm.HasValue && curr.OdometerKm.HasValue &&
                        curr.OdometerKm.Value > prev.OdometerKm.Value)
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

        if (hasFuelData && totalFuelConsumedLiters > 0)
        {
            // Real fuel sensor data → use actual consumption
            avgConsumption = totalDistance > 0
                ? (totalFuelConsumedLiters / totalDistance) * 100
                : 0;
        }
        else if (totalDistance > 0)
        {
            // No fuel data → estimate from distance + default L/100km rate
            useEstimation = true;
            avgConsumption = DefaultConsumptionRates.GetValueOrDefault(vehicleType, 8.0m);
            totalFuelConsumedLiters = (avgConsumption / 100m) * totalDistance;

            dailyConsumption = await BuildDailyFromGps(
                vehicle.GpsDeviceId.Value, startDateUtc, endDateUtc, avgConsumption, cancellationToken);
        }

        // ===== 5. Apply fuel price =====
        var pricePerLiter = fuelPrices.GetValueOrDefault(fuelType, 0);
        var totalCost = totalFuelConsumedLiters * pricePerLiter;

        // ===== 6. Build daily DTOs =====
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
    /// Lightweight DTO for fuel calculation from GPS positions
    /// </summary>
    private class FuelPositionSlim
    {
        public DateTime RecordedAt { get; set; }
        public int FuelPercent { get; set; }
        public long? OdometerKm { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double SpeedKph { get; set; }
    }

    /// <summary>
    /// Remove isolated fuel spikes (sensor glitches).
    /// Pattern: F1 → F2 (spike) → back near F1 → remove F2.
    /// </summary>
    private static List<FuelPositionSlim> FilterFuelSpikes(List<FuelPositionSlim> positions)
    {
        if (positions.Count < 3) return positions;

        var spikeIndices = new HashSet<int>();

        for (int i = 1; i < positions.Count - 1; i++)
        {
            var prevFuel = positions[i - 1].FuelPercent;
            var currFuel = positions[i].FuelPercent;
            var nextFuel = positions[i + 1].FuelPercent;

            var dropFromPrev = Math.Abs(currFuel - prevFuel);
            var recoveryToNext = Math.Abs(nextFuel - prevFuel);

            // Big change from previous (>10%) and next reading recovers (<5% from original)
            if (dropFromPrev > 10 && recoveryToNext <= 5)
                spikeIndices.Add(i);
        }

        // Handle consecutive spikes (e.g., 40 → 20 → 15 → 40)
        for (int i = 1; i < positions.Count - 2; i++)
        {
            if (spikeIndices.Contains(i)) continue;

            var prevFuel = positions[i - 1].FuelPercent;
            var currFuel = positions[i].FuelPercent;
            var afterNextFuel = positions[i + 2].FuelPercent;

            var dropFromPrev = Math.Abs(currFuel - prevFuel);
            var recoveryToAfterNext = Math.Abs(afterNextFuel - prevFuel);

            if (dropFromPrev > 10 && recoveryToAfterNext <= 5)
            {
                spikeIndices.Add(i);
                spikeIndices.Add(i + 1);
            }
        }

        return positions.Where((_, idx) => !spikeIndices.Contains(idx)).ToList();
    }

    /// <summary>
    /// Collapse consecutive readings with the same fuel level into a single entry.
    /// Keeps the first occurrence of each fuel level change.
    /// </summary>
    private static List<FuelPositionSlim> CollapseFuelReadings(List<FuelPositionSlim> positions)
    {
        if (positions.Count == 0) return positions;

        var result = new List<FuelPositionSlim> { positions[0] };
        var lastFuel = positions[0].FuelPercent;

        for (int i = 1; i < positions.Count; i++)
        {
            if (positions[i].FuelPercent != lastFuel)
            {
                result.Add(positions[i]);
                lastFuel = positions[i].FuelPercent;
            }
        }

        return result;
    }

    /// <summary>
    /// Calculate distance directly from a list of GPS positions (Haversine + odometer).
    /// </summary>
    private static int CalculateDistanceFromPositions(List<FuelPositionSlim> positions, DateTime startUtc, DateTime endUtc)
    {
        if (positions.Count < 2) return 0;

        // Haversine distance with jump filter
        double haversineKm = 0;
        for (int i = 1; i < positions.Count; i++)
        {
            var prev = positions[i - 1];
            var curr = positions[i];
            var dist = HaversineKm(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
            if (dist > 0.01 && dist < 5 && curr.SpeedKph > 2)
                haversineKm += dist;
        }

        int haversineDist = (int)Math.Round(haversineKm);

        // Odometer-based distance
        var first = positions.First();
        var last = positions.Last();
        if (first.OdometerKm.HasValue && last.OdometerKm.HasValue && last.OdometerKm.Value > first.OdometerKm.Value)
        {
            int odometerDist = (int)(last.OdometerKm.Value - first.OdometerKm.Value);

            // Auto-detect meters vs km (some devices send odometer in meters)
            if (haversineDist > 0 && odometerDist > haversineDist * 500)
                odometerDist /= 1000;

            var periodHours = (endUtc - startUtc).TotalHours;
            var maxReasonableKm = (int)Math.Max(periodHours * 200, 500);

            if (odometerDist <= maxReasonableKm)
                return Math.Max(odometerDist, haversineDist);
        }

        return haversineDist;
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
            .Select(p => new { p.OdometerKm, p.Latitude, p.Longitude, p.SpeedKph, p.RecordedAt })
            .ToListAsync(ct);

        if (positions.Count < 2) return 0;

        // Always compute Haversine distance with jump filter (max 5 km between consecutive points)
        double haversineKm = 0;
        for (int i = 1; i < positions.Count; i++)
        {
            var prev = positions[i - 1];
            var curr = positions[i];
            var dist = HaversineKm(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
            if (dist > 0.01 && dist < 5 && (curr.SpeedKph ?? 0) > 2)
                haversineKm += dist;
        }

        int haversineDist = (int)Math.Round(haversineKm);

        // Sanity check: max reasonable distance = period hours * 200 km/h
        var periodHours = (endUtc - startUtc).TotalHours;
        var maxReasonableKm = (int)Math.Max(periodHours * 200, 500);

        // For L-type devices, also try odometer with sanity check
        if (trustOdometer)
        {
            var first = positions.First();
            var last = positions.Last();
            if (first.OdometerKm.HasValue && last.OdometerKm.HasValue && last.OdometerKm.Value > first.OdometerKm.Value)
            {
                int odometerDist = (int)(last.OdometerKm.Value - first.OdometerKm.Value);
                
                // Auto-detect: some GPS devices send odometer in meters instead of km
                // If odometer is ~1000x larger than haversine, it's likely in meters
                if (haversineDist > 0 && odometerDist > haversineDist * 500)
                {
                    odometerDist = odometerDist / 1000;
                }
                
                // Only trust odometer if it's within reasonable bounds
                if (odometerDist <= maxReasonableKm)
                    return Math.Max(odometerDist, haversineDist);
                // Odometer aberrant → fall back to haversine
            }
        }

        return Math.Min(haversineDist, maxReasonableKm);
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
            if (h > 0.01 && h < 5 && (curr.SpeedKph ?? 0) > 2)
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

        var startDateUtc = startDate.Kind == DateTimeKind.Utc ? startDate : startDate.ToUniversalTime();
        var endDateUtc = endDate.Kind == DateTimeKind.Utc ? endDate : endDate.ToUniversalTime();

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
