using GisAPI.Application.Common.FuelCalibration;
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
        var sensorMode = vehicle.GpsDevice.FuelSensorMode?.ToLower() ?? "percent";

        var startDateUtc = startDate.Kind == DateTimeKind.Utc ? startDate : startDate.ToUniversalTime();
        var endDateUtc = endDate.Kind == DateTimeKind.Utc ? endDate : endDate.ToUniversalTime();

        // ===== 0. Query FMS CAN bus FuelRateLPer100Km (most accurate source) =====
        // Un TAUX moyen n'a pas besoin de toute la période demandée : sur les
        // longues fenêtres (la page carburant s'ouvrait sur janvier→aujourd'hui)
        // ce scan saturait PostgreSQL — incident TN du 11/08. 30 jours suffisent.
        var rateFromUtc = endDateUtc.AddDays(-30) > startDateUtc ? endDateUtc.AddDays(-30) : startDateUtc;
        var fmsRates = await _context.GpsPositions
            .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value &&
                        p.RecordedAt >= rateFromUtc && p.RecordedAt <= endDateUtc &&
                        p.FuelRateLPer100Km != null && p.FuelRateLPer100Km > 0 && p.FuelRateLPer100Km < 80)
            .Select(p => (double)p.FuelRateLPer100Km!.Value)
            .ToListAsync(cancellationToken);

        // ===== 1. Get GPS positions with fuel data (primary source — granular per-position) =====
        // fuel_records table only has significant events (refuels, theft, spikes).
        // gps_positions.fuelRaw has every 1% change — much more accurate for consumption.
        // Filter range depends on sensor mode:
        //   percent  → 0-100  (FuelRaw is 0-100%)
        //   raw_255  → 0-255  (FuelRaw is 0-255 raw ADC value)
        //   liters   → 0-tank (FuelRaw is actual liters)
        int maxRawValue = sensorMode switch
        {
            "raw_255" => 255,
            "liters" => (int)(tankCapacity * 1.2m), // Allow slight overfill
            _ => 100 // percent
        };

        var rawPositions = await _context.GpsPositions
            .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value &&
                        p.RecordedAt >= startDateUtc &&
                        p.RecordedAt <= endDateUtc &&
                        p.FuelRaw != null && p.FuelRaw >= 0 && p.FuelRaw <= maxRawValue)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new FuelPositionSlim
            {
                RecordedAt = p.RecordedAt,
                FuelPercent = p.FuelRaw!.Value, // Will be converted to actual % below
                OdometerKm = p.OdometerKm,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                SpeedKph = p.SpeedKph ?? 0
            })
            .ToListAsync(cancellationToken);

        // Convert raw sensor values to actual percent (0-100) based on sensor mode
        if (sensorMode == "raw_255")
        {
            foreach (var pos in rawPositions)
                pos.FuelPercent = (int)Math.Round(pos.FuelPercent / 255.0 * 100.0);
        }
        else if (sensorMode == "liters")
        {
            foreach (var pos in rawPositions)
                pos.FuelPercent = (int)Math.Round((double)pos.FuelPercent / (double)tankCapacity * 100.0);
        }
        // For "percent" mode, FuelRaw is already 0-100 — no conversion needed

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

        // ===== 5. Calculate fuel consumption =====
        // Priority: 1) FMS CAN bus  2) FuelRaw % drops (ratchet)  3) Vehicle-type default
        // Ratchet pattern from monthly fleet report: lastFuel only updated on drops & refuels,
        // NOT on positive noise → prevents oscillation double-counting.
        decimal totalFuelConsumedLiters = 0;
        decimal avgConsumption = 0;
        bool useEstimation = false;
        bool hasFuelData = false;
        var dailyConsumption = new Dictionary<DateTime, (decimal fuelPercent, int distance)>();
        var refuels = new List<FuelRefillEventDto>();

        // Sanity thresholds by vehicle type
        var maxReasonable = vehicleType switch
        {
            "camion" or "bus" => 50m,
            "camionnette" or "fourgon" or "utilitaire" or "minibus" => 25m,
            _ => 20m
        };

        // === SOURCE 1: FMS CAN bus FuelRateLPer100Km (most accurate) ===
        if (fmsRates.Count >= 5 && totalDistance > 0)
        {
            // Trimmed mean: exclude top/bottom 10% outliers
            var sorted = fmsRates.OrderBy(r => r).ToList();
            var trimCount = Math.Max(1, sorted.Count / 10);
            var trimmed = sorted.Skip(trimCount).Take(sorted.Count - 2 * trimCount).ToList();
            var fmsRate = (decimal)(trimmed.Any() ? trimmed.Average() : sorted.Average());

            // Sanity check: reject if result is clearly wrong (< 2 or > max L/100km)
            if (fmsRate >= 2m && fmsRate <= maxReasonable)
            {
                avgConsumption = fmsRate;
                totalFuelConsumedLiters = (avgConsumption / 100m) * totalDistance;
                hasFuelData = true;

                dailyConsumption = await BuildDailyFromGps(
                    vehicle.GpsDeviceId.Value, startDateUtc, endDateUtc, avgConsumption, cancellationToken);
            }
        }

        // === SOURCE 2: FuelRaw sensor % drops (ratchet pattern) ===
        // Conversion points→litres ÉTALONNÉE par les pleins facturés du véhicule
        // quand elle existe (sinon capacité/100). Chargée seulement ici : la
        // source CAN (1) n'en a pas besoin et l'étalonnage coûte des requêtes.
        decimal litersPerPoint = tankCapacity / 100m;
        if (!hasFuelData && positions.Count >= 3)
        {
            var calibration = await GetTankCalibrationAsync(vehicle, cancellationToken);
            litersPerPoint = calibration.LitersPerPoint;
            int lastFuel = positions[0].FuelPercent;
            decimal totalDropPercent = 0;

            for (int i = 1; i < positions.Count; i++)
            {
                var curr = positions[i];
                var currFuel = curr.FuelPercent;
                var delta = currFuel - lastFuel;

                // Detect refuel: fuel went up >= 10% from last low
                if (delta >= 10)
                {
                    var addedLiters = delta * litersPerPoint;
                    var priceForRefuel = fuelPrices.GetValueOrDefault(fuelType, 0);
                    refuels.Add(new FuelRefillEventDto(
                        Timestamp: curr.RecordedAt,
                        VehicleId: vehicle.Id,
                        FuelAddedLiters: Math.Round(addedLiters, 2),
                        EstimatedCost: Math.Round(addedLiters * priceForRefuel, 2),
                        Latitude: curr.Latitude,
                        Longitude: curr.Longitude
                    ));
                    lastFuel = currFuel;
                    continue;
                }

                // Small positive change: sensor noise — do NOT update lastFuel
                if (delta > 0)
                    continue;

                // Fuel drop: count it and update lastFuel
                var drop = -delta;
                if (drop > 0 && drop < 50)
                {
                    totalDropPercent += drop;

                    // Track daily consumption
                    var date = curr.RecordedAt.Date;
                    if (!dailyConsumption.ContainsKey(date))
                        dailyConsumption[date] = (0, 0);

                    var prev = positions[i - 1];
                    int segDist = 0;
                    if (prev.OdometerKm.HasValue && curr.OdometerKm.HasValue &&
                        curr.OdometerKm.Value > prev.OdometerKm.Value)
                        segDist = (int)(curr.OdometerKm.Value - prev.OdometerKm.Value);

                    var (ep, ed) = dailyConsumption[date];
                    dailyConsumption[date] = (ep + drop, ed + segDist);
                }
                lastFuel = currFuel;
            }

            if (totalDropPercent > 0)
            {
                totalFuelConsumedLiters = totalDropPercent * litersPerPoint;
                avgConsumption = totalDistance > 0
                    ? (totalFuelConsumedLiters / totalDistance) * 100
                    : 0;

                // Sanity check: reject if result is clearly wrong
                if (avgConsumption >= 2m && avgConsumption <= maxReasonable)
                {
                    hasFuelData = true;
                }
            }
        }

        // === SOURCE 3: Estimation fallback (no reliable fuel data) ===
        if (!hasFuelData)
        {
            useEstimation = true;
            avgConsumption = DefaultConsumptionRates.GetValueOrDefault(vehicleType, 8.0m);
            totalFuelConsumedLiters = totalDistance > 0 ? (avgConsumption / 100m) * totalDistance : 0;

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
                    : kv.Value.fuelPercent * litersPerPoint;
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
    /// Apply a running median filter to smooth fuel sensor oscillations.
    /// Window size should be odd (e.g. 5 or 7). Replaces each fuel reading
    /// with the median of surrounding readings to eliminate noise.
    /// </summary>
    private static List<FuelPositionSlim> MedianSmoothFuel(List<FuelPositionSlim> positions, int windowSize = 5)
    {
        if (positions.Count < windowSize) return positions;

        var half = windowSize / 2;
        var result = new List<FuelPositionSlim>(positions.Count);

        for (int i = 0; i < positions.Count; i++)
        {
            var start = Math.Max(0, i - half);
            var end = Math.Min(positions.Count - 1, i + half);
            var window = new List<int>(end - start + 1);
            for (int j = start; j <= end; j++)
                window.Add(positions[j].FuelPercent);
            window.Sort();

            result.Add(new FuelPositionSlim
            {
                RecordedAt = positions[i].RecordedAt,
                FuelPercent = window[window.Count / 2],
                OdometerKm = positions[i].OdometerKm,
                Latitude = positions[i].Latitude,
                Longitude = positions[i].Longitude,
                SpeedKph = positions[i].SpeedKph
            });
        }

        return result;
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
    /// Batch-calculate fuel stats for all vehicles in a fleet.
    /// 3 SQL queries instead of N*4 per-vehicle queries.
    /// </summary>
    public async Task<List<VehicleFuelExpenseDto>> CalculateFleetFuelBatchAsync(
        List<Vehicle> vehicles,
        DateTime startDate,
        DateTime endDate,
        Dictionary<string, decimal> fuelPrices,
        CancellationToken cancellationToken = default)
    {
        var startUtc = startDate.Kind == DateTimeKind.Utc ? startDate : startDate.ToUniversalTime();
        var endUtc = endDate.Kind == DateTimeKind.Utc ? endDate : endDate.ToUniversalTime();

        var gpsVehicles = vehicles.Where(v => v.GpsDeviceId.HasValue && v.GpsDevice != null).ToList();
        if (gpsVehicles.Count == 0) return new List<VehicleFuelExpenseDto>();

        var deviceIds = gpsVehicles.Select(v => v.GpsDeviceId!.Value).ToList();
        var vehicleIds = gpsVehicles.Select(v => v.Id).ToList();
        var deviceToVehicle = gpsVehicles.ToDictionary(v => v.GpsDeviceId!.Value, v => v);

        // ── Query 1: FMS fuel rates per device (SQL GROUP BY) ──
        // Fenêtre du taux plafonnée à 30 jours : un taux moyen flotte sur 7 mois
        // = scan de dizaines de millions de trames, saturation PG (incident TN
        // 11/08, 3 requêtes concurrentes à 13s+ et retries EF en cascade).
        var rateFromUtc = endUtc.AddDays(-30) > startUtc ? endUtc.AddDays(-30) : startUtc;
        var fmsRatesByDevice = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId) &&
                        p.RecordedAt >= rateFromUtc && p.RecordedAt <= endUtc &&
                        p.FuelRateLPer100Km != null && p.FuelRateLPer100Km > 0 && p.FuelRateLPer100Km < 80)
            .GroupBy(p => p.DeviceId)
            .Select(g => new
            {
                DeviceId = g.Key,
                AvgRate = g.Average(p => (double)p.FuelRateLPer100Km!.Value),
                Count = g.Count()
            })
            .ToDictionaryAsync(x => x.DeviceId, x => x, cancellationToken);

        // ── Query 2: Trip distances per vehicle (pre-computed in trips table) ──
        var tripDistances = await _context.Trips
            .AsNoTracking()
            .Where(t => vehicleIds.Contains(t.VehicleId) &&
                        t.StartTime >= startUtc && t.StartTime <= endUtc &&
                        t.Status == "completed")
            .GroupBy(t => t.VehicleId)
            .Select(g => new
            {
                VehicleId = g.Key,
                TotalKm = (int)g.Sum(t => t.DistanceKm)
            })
            .ToDictionaryAsync(x => x.VehicleId, x => x.TotalKm, cancellationToken);

        // ── Query 3: Daily trip distances per vehicle (for chart) ──
        var dailyDistances = await _context.Trips
            .AsNoTracking()
            .Where(t => vehicleIds.Contains(t.VehicleId) &&
                        t.StartTime >= startUtc && t.StartTime <= endUtc &&
                        t.Status == "completed")
            .GroupBy(t => new { t.VehicleId, Date = t.StartTime.Date })
            .Select(g => new
            {
                VehicleId = g.Key.VehicleId,
                Date = g.Key.Date,
                DistanceKm = (int)g.Sum(t => t.DistanceKm)
            })
            .ToListAsync(cancellationToken);

        var dailyByVehicle = dailyDistances
            .GroupBy(d => d.VehicleId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(d => d.Date, d => d.DistanceKm));

        // ── In-memory: compute per-vehicle fuel stats ──
        var results = new List<VehicleFuelExpenseDto>();

        foreach (var vehicle in gpsVehicles)
        {
            var deviceId = vehicle.GpsDeviceId!.Value;
            var vehicleType = vehicle.Type?.ToLower() ?? "berline";
            var fuelType = vehicle.FuelType?.ToLower() ?? "diesel";
            var tankCapacity = vehicle.FuelTankCapacity ?? 60;

            var totalDistance = tripDistances.GetValueOrDefault(vehicle.Id, 0);
            if (totalDistance <= 0) continue;

            var maxReasonable = vehicleType switch
            {
                "camion" or "bus" => 50m,
                "camionnette" or "fourgon" or "utilitaire" or "minibus" => 25m,
                _ => 20m
            };

            // Priority: FMS CAN bus rate → default rate
            decimal avgConsumption;
            bool isEstimated;

            if (fmsRatesByDevice.TryGetValue(deviceId, out var fms) && fms.Count >= 5)
            {
                avgConsumption = (decimal)fms.AvgRate;
                if (avgConsumption < 2m || avgConsumption > maxReasonable)
                {
                    avgConsumption = DefaultConsumptionRates.GetValueOrDefault(vehicleType, 8.0m);
                    isEstimated = true;
                }
                else
                {
                    isEstimated = false;
                }
            }
            else
            {
                avgConsumption = DefaultConsumptionRates.GetValueOrDefault(vehicleType, 8.0m);
                isEstimated = true;
            }

            var totalFuelLiters = (avgConsumption / 100m) * totalDistance;
            var pricePerLiter = fuelPrices.GetValueOrDefault(fuelType, 0);

            // Build daily DTOs from pre-aggregated trip data
            var dailyDtos = new List<DailyFuelConsumptionDto>();
            if (dailyByVehicle.TryGetValue(vehicle.Id, out var dailyDist))
            {
                dailyDtos = dailyDist.OrderBy(kv => kv.Key).Select(kv =>
                {
                    var dayFuel = (avgConsumption / 100m) * kv.Value;
                    return new DailyFuelConsumptionDto(
                        Date: kv.Key,
                        FuelConsumedLiters: Math.Round(dayFuel, 2),
                        FuelCost: Math.Round(dayFuel * pricePerLiter, 2),
                        DistanceKm: kv.Value,
                        ConsumptionPer100Km: Math.Round(avgConsumption, 2)
                    );
                }).ToList();
            }

            results.Add(new VehicleFuelExpenseDto(
                VehicleId: vehicle.Id,
                VehicleName: vehicle.Name,
                Plate: vehicle.Plate,
                FuelType: vehicle.FuelType,
                FuelTankCapacity: vehicle.FuelTankCapacity,
                TotalFuelConsumedLiters: Math.Round(totalFuelLiters, 2),
                TotalFuelCost: Math.Round(totalFuelLiters * pricePerLiter, 2),
                AverageConsumptionPer100Km: Math.Round(avgConsumption, 2),
                DeviationFromFleetAverage: 0,
                TotalDistanceKm: totalDistance,
                IsEstimated: isEstimated,
                DailyConsumption: dailyDtos,
                Refuels: new List<FuelRefillEventDto>()
            ));
        }

        return results;
    }

    public async Task<ConsumptionSegmentsReportDto?> GetConsumptionSegmentsAsync(
        Vehicle vehicle,
        DateTime startDate,
        DateTime endDate,
        int segmentKm,
        CancellationToken cancellationToken = default)
    {
        if (!vehicle.GpsDeviceId.HasValue) return null;

        var startUtc = startDate.Kind == DateTimeKind.Utc ? startDate : startDate.ToUniversalTime();
        var endUtc = endDate.Kind == DateTimeKind.Utc ? endDate : endDate.ToUniversalTime();
        var vehicleName = string.IsNullOrWhiteSpace(vehicle.Name)
            ? (vehicle.Plate ?? $"Vehicule {vehicle.Id}") : vehicle.Name;

        var positions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value &&
                        p.RecordedAt >= startUtc && p.RecordedAt <= endUtc &&
                        p.FuelRaw != null)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new
            {
                p.RecordedAt,
                FuelPercent = p.FuelRaw!.Value,
                p.OdometerKm,
                p.Latitude,
                p.Longitude,
                p.SpeedKph
            })
            .ToListAsync(cancellationToken);

        if (positions.Count < 3)
        {
            return new ConsumptionSegmentsReportDto(vehicle.Id, vehicleName, segmentKm,
                HasSensor: false, LitersPerPoint: 0, IsCalibrated: false,
                new List<ConsumptionSegmentDto>(),
                new ConsumptionSegmentsSummaryDto(0, 0, null, null, null, null, null, 0, 0));
        }

        var calibration = await GetTankCalibrationAsync(vehicle, cancellationToken);
        var litersPerPoint = calibration.LitersPerPoint;

        // Plafond de vraisemblance par segment : plus large que le plafond période
        // (un tronçon chargé en montée dépasse légitimement la moyenne du véhicule).
        var vehicleType = vehicle.Type?.ToLower() ?? "berline";
        var maxReasonable = vehicleType switch
        {
            "camion" or "bus" => 65m,
            "camionnette" or "fourgon" or "utilitaire" or "minibus" => 33m,
            _ => 26m
        };

        var segments = new List<ConsumptionSegmentDto>();
        int lastFuel = positions[0].FuelPercent;
        decimal segLiters = 0m, segDist = 0m;
        var segStart = positions[0].RecordedAt;
        bool segSensorFault = false;

        ConsumptionSegmentDto CloseSegment(DateTime end)
        {
            var lp100 = segDist > 0 ? Math.Round(segLiters / segDist * 100m, 2) : 0m;
            string? reason = null;
            if (segSensorFault) reason = "chute capteur suspecte";
            else if (lp100 > maxReasonable) reason = "consommation invraisemblable";
            else if (lp100 < 1m) reason = "jauge figee (aucune baisse)";
            return new ConsumptionSegmentDto(segments.Count, segStart, end,
                Math.Round(segDist, 1), Math.Round(segLiters, 1), lp100,
                TonnageT: null, IsReliable: reason == null, ExclusionReason: reason);
        }

        for (int i = 1; i < positions.Count; i++)
        {
            var prev = positions[i - 1];
            var curr = positions[i];

            // Distance : odomètre entre trames consécutives quand il est sain
            // (croissant, pas de saut aberrant), sinon Haversine bornée.
            decimal stepKm = 0m;
            if (prev.OdometerKm.HasValue && curr.OdometerKm.HasValue &&
                curr.OdometerKm.Value >= prev.OdometerKm.Value &&
                curr.OdometerKm.Value - prev.OdometerKm.Value < 50)
            {
                stepKm = curr.OdometerKm.Value - prev.OdometerKm.Value;
            }
            else
            {
                var d = (decimal)HaversineKm(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
                if (d > 0.01m && d < 5m && (decimal)(curr.SpeedKph ?? 0) > 2m)
                    stepKm = d;
            }
            segDist += stepKm;

            // Carburant : mêmes règles ratchet que le calcul de consommation période.
            var delta = curr.FuelPercent - lastFuel;
            if (delta >= 10)
            {
                lastFuel = curr.FuelPercent;   // plein : pas de la consommation
            }
            else if (delta < 0)
            {
                var drop = -delta;
                if (drop < 50)
                {
                    segLiters += drop * litersPerPoint;
                    // Falaise quasi à l'arrêt = signature capteur (flotteur coincé),
                    // pas une consommation réelle.
                    if (drop >= 10 && (curr.SpeedKph ?? 0) < 5)
                        segSensorFault = true;
                }
                else
                {
                    segSensorFault = true;     // chute >= 50 pts : capteur en vrac
                }
                lastFuel = curr.FuelPercent;
            }
            // hausse < 10 pts : bruit, lastFuel inchangé

            if (segDist >= segmentKm)
            {
                segments.Add(CloseSegment(curr.RecordedAt));
                segStart = curr.RecordedAt;
                segLiters = 0m; segDist = 0m; segSensorFault = false;
            }
        }

        // Dernier segment partiel : gardé s'il couvre au moins la moitié de X km.
        if (segDist >= segmentKm / 2m)
            segments.Add(CloseSegment(positions[^1].RecordedAt));

        var reliable = segments.Where(s => s.IsReliable).ToList();
        var totalReliableKm = reliable.Sum(s => s.DistanceKm);
        var summary = new ConsumptionSegmentsSummaryDto(
            TotalKm: Math.Round(segments.Sum(s => s.DistanceKm), 1),
            TotalLiters: Math.Round(segments.Sum(s => s.FuelLiters), 1),
            AvgLPer100Km: totalReliableKm > 0
                ? Math.Round(reliable.Sum(s => s.FuelLiters) / totalReliableKm * 100m, 2) : null,
            MinLPer100Km: reliable.Count > 0 ? reliable.Min(s => s.LPer100Km) : null,
            MinSegmentIndex: reliable.Count > 0 ? reliable.OrderBy(s => s.LPer100Km).First().Index : null,
            MaxLPer100Km: reliable.Count > 0 ? reliable.Max(s => s.LPer100Km) : null,
            MaxSegmentIndex: reliable.Count > 0 ? reliable.OrderByDescending(s => s.LPer100Km).First().Index : null,
            ReliableSegments: reliable.Count,
            ExcludedSegments: segments.Count - reliable.Count);

        return new ConsumptionSegmentsReportDto(vehicle.Id, vehicleName, segmentKm,
            HasSensor: true, LitersPerPoint: Math.Round(litersPerPoint, 3),
            IsCalibrated: calibration.IsCalibrated, segments, summary);
    }

    /// <summary>
    /// Per-vehicle fuel-level audit from the boitier sensor: builds the fuel-level curve over
    /// time (litres) and the detected refills (>= 10% level jumps), reusing the same conversion
    /// and spike filtering as the consumption calc. Returns HasSensor=false when there is no
    /// usable fuel sensor data.
    /// </summary>
    public async Task<FuelLevelAuditDto> GetFuelLevelAuditAsync(
        Vehicle vehicle,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        var tankCapacity = vehicle.FuelTankCapacity ?? 60;
        var empty = new FuelLevelAuditDto(false, tankCapacity, new List<FuelLevelPointDto>(), new List<DetectedRefillDto>());

        if (!vehicle.GpsDeviceId.HasValue || vehicle.GpsDevice == null)
            return empty;

        var sensorMode = vehicle.GpsDevice.FuelSensorMode?.ToLower() ?? "percent";
        var startUtc = startDate.Kind == DateTimeKind.Utc ? startDate : startDate.ToUniversalTime();
        var endUtc = endDate.Kind == DateTimeKind.Utc ? endDate : endDate.ToUniversalTime();

        int maxRawValue = sensorMode switch
        {
            "raw_255" => 255,
            "liters" => (int)(tankCapacity * 1.2m),
            _ => 100
        };

        var rawPositions = await _context.GpsPositions
            .Where(p => p.DeviceId == vehicle.GpsDeviceId.Value &&
                        p.RecordedAt >= startUtc && p.RecordedAt <= endUtc &&
                        p.FuelRaw != null && p.FuelRaw >= 0 && p.FuelRaw <= maxRawValue)
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

        if (rawPositions.Count < 3)
            return empty;

        // Raw → percent (0-100)
        if (sensorMode == "raw_255")
            foreach (var pos in rawPositions) pos.FuelPercent = (int)Math.Round(pos.FuelPercent / 255.0 * 100.0);
        else if (sensorMode == "liters")
            foreach (var pos in rawPositions) pos.FuelPercent = (int)Math.Round((double)pos.FuelPercent / (double)tankCapacity * 100.0);

        // Garbage sensor (0<->100 oscillation) → no usable curve
        if (rawPositions.Count >= 4)
        {
            var extremeCount = rawPositions.Count(p => p.FuelPercent <= 2 || p.FuelPercent >= 98);
            var extremeRatio = (double)extremeCount / rawPositions.Count;
            var largeSwings = 0;
            for (int i = 1; i < rawPositions.Count; i++)
                if (Math.Abs(rawPositions[i].FuelPercent - rawPositions[i - 1].FuelPercent) > 50) largeSwings++;
            var swingRatio = (double)largeSwings / (rawPositions.Count - 1);
            if (extremeRatio > 0.6 && swingRatio > 0.1)
                return empty;
        }

        var positions = FilterFuelSpikes(rawPositions);
        if (positions.Count < 3)
            return empty;

        var tankCap = (decimal)tankCapacity;

        // Curve: keep only level changes (collapse consecutive identical readings).
        var collapsed = CollapseFuelReadings(positions);
        var series = collapsed.Select(p => new FuelLevelPointDto(
            p.RecordedAt,
            p.FuelPercent,
            Math.Round((decimal)p.FuelPercent / 100m * tankCap, 1)
        )).ToList();

        // Detected refills: level jumps >= 10% (same ratchet rule as consumption).
        // From/To portent la mesure brute en points — la conversion en litres
        // (nominale ici) sera ré-étalonnée par véhicule en aval.
        var refills = new List<DetectedRefillDto>();
        int lastFuel = positions[0].FuelPercent;
        for (int i = 1; i < positions.Count; i++)
        {
            var delta = positions[i].FuelPercent - lastFuel;
            if (delta >= 10)
            {
                refills.Add(new DetectedRefillDto(
                    positions[i].RecordedAt,
                    Math.Round((decimal)delta / 100m * tankCap, 1),
                    FromPercent: lastFuel,
                    ToPercent: positions[i].FuelPercent));
                lastFuel = positions[i].FuelPercent;
            }
            else if (delta < 0)
            {
                lastFuel = positions[i].FuelPercent;
            }
            // small positive change = sensor noise → keep lastFuel
        }

        return new FuelLevelAuditDto(true, tankCapacity, series, MergeSameRefill(refills, tankCap));
    }

    /// <summary>Fenêtre au-delà de laquelle deux hausses de niveau sont deux pleins distincts.</summary>
    private const double SameRefillWindowMinutes = 30.0;

    /// <summary>
    /// Regroupe les hausses de niveau successives qui appartiennent au MÊME plein.
    ///
    /// POURQUOI — le détecteur émet un événement à chaque bond de niveau ≥ 10 %. Or
    /// un remplissage réel fait monter la jauge par paliers : sur un poids lourd,
    /// un plein unique produit trois ou quatre bonds à quelques minutes d'intervalle.
    /// Chacun devenait un « remplissage » distinct, et le rapprochement avec les
    /// factures n'en associant qu'UN par facture, les autres ressortaient en
    /// « Non déclaré » — c'est-à-dire signalés comme du carburant entré dans le
    /// réservoir sans justificatif. Exactement le contraire de la vérité.
    ///
    /// Cas réel (SGF, Scania 001, 29/06/2026) : 265 L à 20 h 35 puis 140 L à 20 h 37,
    /// soit 405 L pour une facture de 423 L. Un seul plein, lu comme un plein
    /// confirmé plus une anomalie.
    ///
    /// Le regroupement corrige aussi la comparaison des volumes : 405 L contre
    /// 423 L (4 % d'écart) est probant, là où 265 L contre 423 L ne l'était pas.
    /// </summary>
    private static List<DetectedRefillDto> MergeSameRefill(List<DetectedRefillDto> refills, decimal tankCap)
    {
        if (refills.Count < 2) return refills;

        var merged = new List<DetectedRefillDto>();
        foreach (var r in refills.OrderBy(x => x.T))
        {
            if (merged.Count > 0
                && (r.T - merged[^1].T).TotalMinutes <= SameRefillWindowMinutes)
            {
                // On conserve l'horodatage du DÉBUT du plein — c'est le moment où
                // le conducteur est à la pompe. Le niveau de départ est celui du
                // PREMIER bond, l'arrivée celle du DERNIER : les litres se
                // recalculent depuis cette montée totale (et non en additionnant
                // des bonds dont les paliers intermédiaires peuvent se chevaucher).
                var toPct = Math.Max(merged[^1].ToPercent, r.ToPercent);
                var deltaPts = toPct - merged[^1].FromPercent;
                merged[^1] = merged[^1] with
                {
                    ToPercent = toPct,
                    Liters = Math.Round(deltaPts / 100m * tankCap, 1)
                };
            }
            else
            {
                merged.Add(r);
            }
        }

        return merged;
    }

    /// <summary>
    /// Étalonnage points→litres appris des pleins facturés (12 derniers mois,
    /// 12 factures max). Chaque facture avec litres est rapprochée de la montée
    /// de jauge la plus proche (±36 h : les factures sont souvent saisies sans
    /// heure). Le rapport litres/points de chaque paire nourrit
    /// TankCalibrationResult.Fit, qui décide seul s'il y a assez de points
    /// cohérents pour faire foi.
    /// </summary>
    public async Task<TankCalibrationResult> GetTankCalibrationAsync(
        Vehicle vehicle,
        CancellationToken cancellationToken = default)
    {
        var nominal = vehicle.FuelTankCapacity ?? 60;
        if (!vehicle.GpsDeviceId.HasValue || vehicle.GpsDevice == null)
            return TankCalibrationResult.Uncalibrated(nominal);

        var since = DateTime.UtcNow.AddDays(-365);
        var entries = await _context.FuelEntries
            .AsNoTracking()
            .Where(fe => fe.InvoiceDate >= since && fe.Volume > 0
                         && (fe.VehicleId == vehicle.Id
                             || (fe.VehicleId == null
                                 && !string.IsNullOrEmpty(vehicle.Plate)
                                 && fe.VehiclePlate == vehicle.Plate)))
            .OrderByDescending(fe => fe.InvoiceDate)
            .Take(12)
            .Select(fe => new { fe.InvoiceDate, fe.Volume })
            .ToListAsync(cancellationToken);

        var points = new List<TankCalibrationPoint>();
        // Deux factures ne peuvent pas revendiquer la même montée de jauge
        // (ex. facture fractionnée) : la première servie gagne, l'autre est ignorée.
        var usedRefills = new HashSet<DateTime>();

        foreach (var e in entries)
        {
            var audit = await GetFuelLevelAuditAsync(
                vehicle, e.InvoiceDate.AddHours(-36), e.InvoiceDate.AddHours(36), cancellationToken);
            if (!audit.HasSensor || audit.DetectedRefills.Count == 0) continue;

            var best = audit.DetectedRefills
                .Where(r => !usedRefills.Contains(r.T))
                .OrderBy(r => Math.Abs((r.T - e.InvoiceDate).TotalHours))
                .FirstOrDefault();
            if (best == null || best.DeltaPoints < TankCalibrationResult.MinDeltaPointsForPoint)
                continue;

            usedRefills.Add(best.T);
            points.Add(new TankCalibrationPoint(e.InvoiceDate, e.Volume, best.DeltaPoints));
        }

        return TankCalibrationResult.Fit(points, nominal);
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
