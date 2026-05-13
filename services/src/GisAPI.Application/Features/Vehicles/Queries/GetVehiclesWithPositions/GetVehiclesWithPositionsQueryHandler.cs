using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions;

// Query projection carrier — public because Npgsql's SqlQueryRaw<T>
// requires a concrete public type. Stays a plain POCO with settable
// properties so the runtime can hydrate it from the raw SQL columns.
public class LatestPositionData
{
    public int DeviceId { get; set; }
    public long Id { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? SpeedKph { get; set; }
    public double? CourseDeg { get; set; }
    public bool? IgnitionOn { get; set; }
    public DateTime RecordedAt { get; set; }
    public int? FuelRaw { get; set; }
    public short? TemperatureC { get; set; }
    public int? PowerVoltage { get; set; }
    public string? Address { get; set; }
    public long? OdometerKm { get; set; }
}

public class GetVehiclesWithPositionsQueryHandler : IRequestHandler<GetVehiclesWithPositionsQuery, List<VehicleWithPositionDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetVehiclesWithPositionsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<List<VehicleWithPositionDto>> Handle(GetVehiclesWithPositionsQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;
        var userId = _tenantService.UserId ?? 0;
        var isAdmin = _tenantService.UserRoles.Any(r => r == "company_admin" || r == "admin" || r == "super_admin" || r == "system_admin");

        // Get vehicles with GPS devices
        var vehicleQuery = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Include(v => v.GpsDevice)
            .AsQueryable();

        // Non-admin users only see their assigned vehicles
        if (!isAdmin && userId > 0)
        {
            var assignedVehicleIds = await _context.UserVehicles
                .Where(uv => uv.UserId == userId)
                .Select(uv => uv.VehicleId)
                .ToListAsync(ct);

            if (assignedVehicleIds.Any())
                vehicleQuery = vehicleQuery.Where(v => assignedVehicleIds.Contains(v.Id));
            else
                vehicleQuery = vehicleQuery.Where(v => false);
        }

        var vehicles = await vehicleQuery.ToListAsync(ct);

        var deviceIds = vehicles
            .Where(v => v.GpsDevice != null)
            .Select(v => v.GpsDevice!.Id)
            .ToList();

        // Fetch the latest position per device in ONE Postgres-native
        // DISTINCT ON query. The previous LINQ form
        //   GpsPositions.Where(...).GroupBy(DeviceId)
        //     .Select(g => g.OrderByDescending(RecordedAt).First().Id)
        // translated to a window-function plan that touched the full
        // gps_positions table (no time filter, billions of rows on a
        // multi-year fleet history) — the dominant cause of monitoring
        // slowness in prod.
        //
        // DISTINCT ON walks the existing (device_id, recorded_at DESC)
        // index in reverse, picks the first row per device, stops. With
        // the INCLUDE columns on idx_gps_positions_device_recorded_desc
        // (latitude, longitude, speed_kph, course_deg, ignition_on,
        // fuel_raw, temperature_c, address, odometer_km) this is mostly
        // an index-only scan — constant time per device.
        var latestPositions = new Dictionary<int, LatestPositionData>();
        if (deviceIds.Count > 0)
        {
            const string sql = @"
SELECT DISTINCT ON (device_id)
    device_id      AS ""DeviceId"",
    id             AS ""Id"",
    latitude       AS ""Latitude"",
    longitude      AS ""Longitude"",
    speed_kph      AS ""SpeedKph"",
    course_deg     AS ""CourseDeg"",
    ignition_on    AS ""IgnitionOn"",
    recorded_at    AS ""RecordedAt"",
    fuel_raw       AS ""FuelRaw"",
    temperature_c  AS ""TemperatureC"",
    power_voltage  AS ""PowerVoltage"",
    address        AS ""Address"",
    odometer_km    AS ""OdometerKm""
FROM gps_positions
WHERE device_id = ANY({0})
ORDER BY device_id, recorded_at DESC;
";
            var rows = await _context.Database
                .SqlQueryRaw<LatestPositionData>(sql, deviceIds.ToArray())
                .ToListAsync(ct);
            latestPositions = rows.ToDictionary(p => p.DeviceId);
        }

        // Get today's stats per device (last 24 hours) — ONE query for all devices
        // Fetch all positions for all devices at once, then compute stats in-memory
        var since = DateTime.UtcNow.AddHours(-24);
        var allRecentPositions = await _context.GpsPositions
            .AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId) && p.RecordedAt >= since)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new { p.DeviceId, p.RecordedAt, p.SpeedKph })
            .ToListAsync(ct);

        // "Engine off since" — for each device, the most recent frame
        // where the engine was on. After that timestamp the engine has
        // been off.
        //
        // We use raw SQL with Postgres' DISTINCT ON because the previous
        // LINQ GroupBy + Max forced a scan of all ignition-on frames
        // over 30 days for every device (≈5 M rows on a 121-vehicle
        // fleet) and produced visible monitoring-page slowness.
        //
        // The partial index ix_gps_positions_ignition_on_recent makes
        // this query satisfiable via a single index scan per device:
        // Postgres walks the (device_id, recorded_at DESC) entries that
        // already have ignition_on=true, picks the first row per device,
        // stops. Constant-time per device regardless of how many frames
        // are in the 30-day window.
        var engineOffLookback = DateTime.UtcNow.AddDays(-30);
        var lastIgnitionOn = new Dictionary<int, DateTime>();
        if (deviceIds.Count > 0)
        {
            const string sql = @"
SELECT DISTINCT ON (device_id)
    device_id AS ""DeviceId"",
    recorded_at AS ""LastOn""
FROM gps_positions
WHERE device_id = ANY({0})
  AND ignition_on = TRUE
  AND recorded_at >= {1}
ORDER BY device_id, recorded_at DESC;
";
            var rows = await _context.Database
                .SqlQueryRaw<EngineOffRow>(sql, deviceIds.ToArray(), engineOffLookback)
                .ToListAsync(ct);
            lastIgnitionOn = rows.ToDictionary(r => r.DeviceId, r => r.LastOn);
        }

        var deviceStats = new Dictionary<int, (double MaxSpeed, double MovingMinutes, double StoppedMinutes, int TotalCount)>();
        foreach (var group in allRecentPositions.GroupBy(p => p.DeviceId))
        {
            var positions = group.ToList();
            var maxSpeed = positions.Max(p => p.SpeedKph ?? 0);
            double movingSeconds = 0;
            double stoppedSeconds = 0;

            for (int i = 1; i < positions.Count; i++)
            {
                var gap = (positions[i].RecordedAt - positions[i - 1].RecordedAt).TotalSeconds;
                // Cap gap at 10 minutes to avoid counting long offline periods
                if (gap > 600) gap = 600;

                // Attribute the gap to moving or stopped based on the previous position's speed
                if ((positions[i - 1].SpeedKph ?? 0) > 5)
                    movingSeconds += gap;
                else
                    stoppedSeconds += gap;
            }

            deviceStats[group.Key] = (maxSpeed, movingSeconds / 60.0, stoppedSeconds / 60.0, positions.Count);
        }

        var result = vehicles.Select(v =>
        {
            var deviceId = v.GpsDevice?.Id ?? 0;
            latestPositions.TryGetValue(deviceId, out var position);
            deviceStats.TryGetValue(deviceId, out var stats);
            var lastComm = v.GpsDevice?.LastCommunication;
            var isOnline = lastComm.HasValue && (DateTime.UtcNow - lastComm.Value).TotalMinutes < 41;
            // Calculate battery level from latest position's PowerVoltage (same formula as Rust ingest)
            // Fallback to GpsDevice.BatteryLevel if PowerVoltage is not available
            // ALSO compute the raw voltage in volts so the UI can show "12.6 V"
            // alongside (or instead of) the percentage — operator preference.
            int? batteryLevel = v.GpsDevice?.BatteryLevel;
            double? batteryVoltageV = null;
            if (position?.PowerVoltage != null && position.PowerVoltage > 0)
            {
                const double voltageFactor = 0.3;
                const double batteryMinV = 11.0;
                const double batteryMaxV = 12.8;
                // Operational ceiling for a 12 V system: textbook regulated
                // alternator output is 14.4 V (Bosch / Toyota service specs).
                // Some NEMS L boîtiers report bytes ≥ 49 (≥ 14.7 V) due to a
                // firmware calibration quirk that breaks above the alternator
                // range — those values are physically impossible on a 12 V
                // car and would otherwise display as e.g. "16.5 V" on a
                // perfectly healthy rental vehicle. We clamp at the display
                // layer only; the raw byte stays untouched in the DB.
                const double batteryRealisticMaxV = 14.4;
                var voltage = position.PowerVoltage.Value * voltageFactor;
                if (voltage > batteryRealisticMaxV) voltage = batteryRealisticMaxV;
                batteryVoltageV = Math.Round(voltage, 1);
                if (batteryLevel == null)
                {
                    if (voltage <= batteryMinV) batteryLevel = 0;
                    else if (voltage >= batteryMaxV) batteryLevel = 100;
                    else batteryLevel = (int)Math.Round((voltage - batteryMinV) / (batteryMaxV - batteryMinV) * 100.0);
                }
            }

            // Sticky battery-health flag: any voltage-health alert raised in
            // the last 7 days surfaces as a warning indicator on the
            // monitoring page. We deliberately go beyond the 48h cooldown of
            // the detector so an admin who didn't see the SignalR push still
            // notices the warning when they open the page.
            var batteryAlertCutoff = DateTime.UtcNow.AddDays(-7);
            var hasBatteryHealthAlert =
                v.GpsDevice?.LastVoltageHealthAlertAt.HasValue == true
                && v.GpsDevice.LastVoltageHealthAlertAt.Value >= batteryAlertCutoff;

            // If ignition is off, speed is 0
            var ignitionOn = position?.IgnitionOn ?? false;
            var rawSpeed = position?.SpeedKph ?? 0.0;

            // Stale ignition detection: if last position says ignition ON but speed is 0
            // and the position is older than 10 minutes, it's likely stale data
            // (the ignition-OFF frame was missed due to GPS ingest throttling)
            if (ignitionOn && rawSpeed <= 1 && position != null)
            {
                var positionAge = (DateTime.UtcNow - position.RecordedAt).TotalMinutes;
                if (positionAge > 10)
                {
                    ignitionOn = false;
                }
            }

            var currentSpeed = ignitionOn ? Math.Round(rawSpeed) : 0.0;
            var isMoving = ignitionOn && rawSpeed > 5;

            // Round max speed to whole number
            var maxSpeed = Math.Round(stats.MaxSpeed);

            // Filter invalid temperature values (-32768 is uninitialized/error value)
            var temperature = position?.TemperatureC;
            if (temperature.HasValue && (temperature.Value < -100 || temperature.Value > 200))
            {
                temperature = null;
            }

            // Convert fuel raw value based on fuel_sensor_mode
            var fuelRaw = position?.FuelRaw;
            int? fuelLevel = null;
            if (fuelRaw.HasValue)
            {
                var fuelMode = v.GpsDevice?.FuelSensorMode ?? "raw_255";
                var tankCapacity = v.FuelTankCapacity ?? 60; // Default 60L if not set
                fuelLevel = fuelMode switch
                {
                    "percent" => fuelRaw.Value, // Already 0-100%
                    "raw_255" => (int)Math.Round(fuelRaw.Value / 255.0 * 100.0), // 0-255 -> 0-100%
                    "liters" => tankCapacity > 0 ? (int)Math.Round(fuelRaw.Value * 100.0 / tankCapacity) : fuelRaw.Value, // Liters -> %
                    "half_liter" => tankCapacity > 0 ? (int)Math.Round(fuelRaw.Value * 0.5 * 100.0 / tankCapacity) : (int)Math.Round(fuelRaw.Value * 0.5), // Half-liters -> %
                    _ => fuelRaw.Value // Default: keep as-is
                };
                // Clamp to 0-100
                if (fuelLevel > 100) fuelLevel = 100;
                if (fuelLevel < 0) fuelLevel = 0;
            }

            // Accurate moving/stopped time from time-gap calculation
            var movingMinutes = stats.MovingMinutes;
            var stoppedMinutes = stats.StoppedMinutes;

            return new VehicleWithPositionDto(
                v.Id,
                v.Name,
                v.Type,
                v.Brand,
                v.Model,
                v.Plate,
                v.Status,
                v.HasGps,
                v.GpsDevice?.DeviceUid,
                v.GpsDevice?.Id,
                lastComm,
                isOnline,
                position != null ? new PositionDto(
                    (int)position.Id,
                    position.Latitude,
                    position.Longitude,
                    ignitionOn ? Math.Round(position.SpeedKph ?? 0.0) : 0.0,
                    position.CourseDeg ?? 0.0,
                    ignitionOn,
                    position.RecordedAt,
                    position.FuelRaw,
                    temperature,
                    batteryLevel,
                    position.Address,
                    position.OdometerKm,
                    batteryVoltageV
                ) : null,
                new VehicleStatsDto(
                    currentSpeed,
                    maxSpeed,
                    fuelLevel,
                    temperature,
                    batteryLevel,
                    batteryVoltageV,
                    isMoving,
                    !isMoving,
                    TimeSpan.FromMinutes(movingMinutes),
                    TimeSpan.FromMinutes(stoppedMinutes),
                    isMoving ? null : position?.RecordedAt,
                    isMoving ? position?.RecordedAt : null,
                    // EngineOffSince: the timestamp of the most recent
                    // ignition-on frame. Null while the engine is
                    // currently on, or if there's no ignition-on frame
                    // in the lookback window.
                    isMoving
                        ? null
                        : (lastIgnitionOn.TryGetValue(deviceId, out var lastOn) ? lastOn : (DateTime?)null)
                ),
                // Firmware "L": use GPS odometer_km directly, otherwise use vehicle mileage
                (v.GpsDevice?.FirmwareVersion != null
                 && v.GpsDevice.FirmwareVersion.StartsWith("L", StringComparison.OrdinalIgnoreCase)
                 && position?.OdometerKm > 0
                 && position?.OdometerKm != 1048574)
                    ? (int)position!.OdometerKm.Value
                    : v.Mileage,
                hasBatteryHealthAlert,
                v.IsImmobilized,
                v.ImmobilizationReason,
                v.ImmobilizationStartedAt
            );
        }).ToList();

        return result;
    }

    /// <summary>
    /// Carrier for the DISTINCT ON query results — Npgsql needs a
    /// concrete public type for <c>SqlQueryRaw&lt;T&gt;</c>.
    /// </summary>
    public class EngineOffRow
    {
        public int DeviceId { get; set; }
        public DateTime LastOn { get; set; }
    }
}



