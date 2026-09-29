using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Common;
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
    public int? BatteryRaw { get; set; }
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

        // Fetch the latest position per device via a LATERAL JOIN —
        // the only pattern that reliably forces Postgres to do exactly
        // ONE index seek per device on (device_id, recorded_at DESC).
        //
        // Previous attempts that failed in prod:
        //   - LINQ GroupBy + OrderByDescending + First → window-function
        //     plan that scans the full table (~billions of rows).
        //   - SQL DISTINCT ON → planner picked a bitmap heap scan that
        //     loaded 96k rows for 5 devices, then sorted in memory
        //     (measured 2.5 s on local DB, much worse on prod history).
        //
        // The LATERAL form forces a NESTED LOOP over each device_id with
        // a `LIMIT 1 ORDER BY recorded_at DESC` subquery — Postgres reads
        // the composite index BACKWARD, picks the first row, stops. 1 ms
        // for 5 devices on the local DB; constant per device regardless
        // of history depth.
        var latestPositions = new Dictionary<int, LatestPositionData>();
        if (deviceIds.Count > 0)
        {
            const string sql = @"
SELECT
    lp.device_id      AS ""DeviceId"",
    lp.id             AS ""Id"",
    lp.latitude       AS ""Latitude"",
    lp.longitude      AS ""Longitude"",
    lp.speed_kph      AS ""SpeedKph"",
    lp.course_deg     AS ""CourseDeg"",
    lp.ignition_on    AS ""IgnitionOn"",
    lp.recorded_at    AS ""RecordedAt"",
    lp.fuel_raw       AS ""FuelRaw"",
    lp.temperature_c  AS ""TemperatureC"",
    lp.power_voltage  AS ""PowerVoltage"",
    lp.battery_raw    AS ""BatteryRaw"",
    lp.address        AS ""Address"",
    lp.odometer_km    AS ""OdometerKm""
FROM unnest({0}::integer[]) AS d(device_id)
CROSS JOIN LATERAL (
    SELECT id, device_id, latitude, longitude, speed_kph, course_deg,
           ignition_on, recorded_at, fuel_raw, temperature_c,
           power_voltage, battery_raw, address, odometer_km
    FROM gps_positions
    WHERE device_id = d.device_id
    ORDER BY recorded_at DESC
    LIMIT 1
) lp;
";
            var rows = await _context.Database
                .SqlQueryRaw<LatestPositionData>(sql, deviceIds.ToArray())
                .ToListAsync(ct);
            latestPositions = rows.ToDictionary(p => p.DeviceId);
        }

        // Stats du jour par device (24 h) — AGRÉGÉES EN SQL.
        //
        // L'ancienne version RAMENAIT EN MÉMOIRE toutes les positions des
        // dernières 24 h (≈165 000 lignes sur la flotte prod) puis calculait
        // les stats en .NET. Ce handler étant appelé par la carte de suivi ET
        // le dashboard, en polling ~30 s et par utilisateur, chaque appel
        // matérialisait 165 000 objets → pression GC massive et lenteur
        // GÉNÉRALE dès que plusieurs opérateurs étaient connectés en même temps
        // (cause de la lenteur signalée le 15/07).
        //
        // Postgres calcule maintenant vitesse max / minutes en mouvement /
        // à l'arrêt / nombre de trames via une fenêtre LAG et ne renvoie
        // qu'UNE ligne par device (~280 au lieu de 165 000). Même écart de
        // temps entre deux trames plafonné à 10 min (offline non compté).
        var since = DateTime.UtcNow.AddHours(-24);

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
            // Same LATERAL JOIN pattern as the latest-position query above.
            // Forces Postgres to do one index seek per device on the
            // (device_id, recorded_at DESC) WHERE ignition_on = true
            // partial index. The DISTINCT ON variant was tolerable here
            // (17 ms locally) but degrades on prod-scale history; LATERAL
            // stays constant per device whatever the lookback.
            const string sql = @"
SELECT
    d.device_id      AS ""DeviceId"",
    lp.recorded_at   AS ""LastOn""
FROM unnest({0}::integer[]) AS d(device_id)
CROSS JOIN LATERAL (
    SELECT recorded_at
    FROM gps_positions
    WHERE device_id = d.device_id
      AND ignition_on = TRUE
      AND recorded_at >= {1}
    ORDER BY recorded_at DESC
    LIMIT 1
) lp;
";
            var rows = await _context.Database
                .SqlQueryRaw<EngineOffRow>(sql, deviceIds.ToArray(), engineOffLookback)
                .ToListAsync(ct);
            lastIgnitionOn = rows.ToDictionary(r => r.DeviceId, r => r.LastOn);
        }

        var deviceStats = new Dictionary<int, (double MaxSpeed, double MovingMinutes, double StoppedMinutes, int TotalCount)>();
        if (deviceIds.Count > 0)
        {
            // Fenêtre LAG : pour chaque trame, écart avec la précédente (plafonné
            // à 600 s pour ne pas compter les coupures offline), attribué en
            // mouvement/arrêt selon la vitesse de la trame précédente — exactement
            // l'ancienne logique .NET, mais exécutée par Postgres et agrégée.
            //
            // LA BATTERIE N'EST PAS CALCULÉE ICI, et c'est délibéré. Une version
            // intermédiaire y agrégeait l'octet « Batterie » sur la journée ; mesuré
            // le 28/09/2026 sur les 307 véhicules HERTZ, cela coûtait 674 ms → 788 à
            // 944 ms par appel, sur un chemin pollé toutes les ~30 s par page et par
            // utilisateur. La tension retenue au démarrage est désormais un ÉTAT du
            // boîtier (gps_devices.battery_start_raw, écrit par
            // BatteryStartReadingService) : elle arrive avec le véhicule, gratuitement.
            const string statsSql = @"
SELECT device_id AS ""DeviceId"",
       COALESCE(MAX(speed_kph), 0)::double precision AS ""MaxSpeed"",
       (COALESCE(SUM(CASE WHEN prev_speed > 5 THEN gap ELSE 0 END), 0) / 60.0)::double precision AS ""MovingMinutes"",
       (COALESCE(SUM(CASE WHEN prev_speed <= 5 OR prev_speed IS NULL THEN gap ELSE 0 END), 0) / 60.0)::double precision AS ""StoppedMinutes"",
       COUNT(*)::int AS ""TotalCount""
FROM (
    SELECT device_id, speed_kph, recorded_at,
           LEAST(GREATEST(EXTRACT(EPOCH FROM (recorded_at - LAG(recorded_at) OVER w)), 0), 600) AS gap,
           LAG(speed_kph) OVER w AS prev_speed
    FROM gps_positions
    WHERE device_id = ANY({0}) AND recorded_at >= {1}
    WINDOW w AS (PARTITION BY device_id ORDER BY recorded_at)
) t
GROUP BY device_id;
";
            var statRows = await _context.Database
                .SqlQueryRaw<DeviceStatRow>(statsSql, deviceIds.ToArray(), since)
                .ToListAsync(ct);
            foreach (var r in statRows)
                deviceStats[r.DeviceId] = (r.MaxSpeed, r.MovingMinutes, r.StoppedMinutes, r.TotalCount);
        }

        var result = vehicles.Select(v =>
        {
            var deviceId = v.GpsDevice?.Id ?? 0;
            latestPositions.TryGetValue(deviceId, out var position);
            deviceStats.TryGetValue(deviceId, out var stats);
            var lastComm = v.GpsDevice?.LastCommunication;
            var isOnline = lastComm.HasValue && (DateTime.UtcNow - lastComm.Value).TotalMinutes < 41;
            // Tension et niveau de batterie (voir BatteryReadout) :
            // - NEMS : tension retenue au DERNIER DÉMARRAGE (Slim, 29/09/2026), lue
            //   sur le boîtier, N/A tant qu'aucun démarrage exploitable n'est connu ;
            // - Teltonika : dernière trame, seulement si l'audit a validé le
            //   capteur. Sur 243 véhicules TN, 213 renvoyaient la même valeur
            //   moteur tournant et éteint (259 TU 4987 affiché « 12,9 V / 100 % »
            //   sans pouvoir démarrer, 14/08/2026) : devant une batterie, un trou
            //   franc vaut mieux qu'une fausse assurance.
            var battery = BatteryReadout.Compute(
                v.GpsDevice?.ProtocolType,
                v.GpsDevice?.VoltageSensorReliable,
                v.GpsDevice?.BatteryLevel,
                position?.BatteryRaw,
                position?.PowerVoltage,
                v.GpsDevice?.BatteryStartRaw,
                v.GpsDevice?.BatteryStartAt,
                v.GpsDevice?.BatteryStartMedianRaw);
            var batteryLevel = battery.Percent;
            var batteryVoltageV = battery.Volts;

            // Icône « Anomalie batterie ».
            // - NEMS : tension du dernier démarrage sous 11,5 V (Slim, 29/09/2026).
            //   C'est exactement ce que juge la notification « batterie en fin de
            //   vie » (VoltageHealthMonitoringService) : l'écran et l'alerte disent
            //   désormais la même chose, à partir de la même valeur.
            // - Autres : drapeau collant de l'alerte de santé des 7 derniers jours,
            //   au-delà des 48 h de silence du détecteur, pour qu'un admin qui a
            //   manqué la notification voie quand même l'avertissement.
            var batteryAlertCutoff = DateTime.UtcNow.AddDays(-7);
            var hasBatteryHealthAlert = battery.IsStartReading
                ? battery.LowWarning
                : v.GpsDevice?.LastVoltageHealthAlertAt.HasValue == true
                  && v.GpsDevice.LastVoltageHealthAlertAt.Value >= batteryAlertCutoff;

            // If ignition is off, speed is 0
            var ignitionOn = position?.IgnitionOn ?? false;
            var rawSpeed = position?.SpeedKph ?? 0.0;

            // Stale-frame detection: if the most recent frame is older
            // than 10 minutes, the vehicle's current state is unknown —
            // we should NOT trust the frame's ignition/speed values for
            // "currently moving" / "currently running" UI labels.
            //
            // Previous logic only kicked in when rawSpeed <= 1, which left
            // a gaping hole: a vehicle whose last frame showed "ignition
            // ON, speed 60 km/h" but went silent right after stayed
            // shown as "moving at 60 km/h" forever on the monitoring
            // page. Operators reported it as "stopped vehicles displayed
            // as moving" — the GPS device just lost signal mid-trip and
            // the last frame happened to capture a moving snapshot.
            //
            // The fix: ANY frame older than 10 min → force ignition off
            // and speed to 0 for display purposes. Better a false
            // "stopped" than a false "speeding".
            if (position != null)
            {
                var positionAge = (DateTime.UtcNow - position.RecordedAt).TotalMinutes;
                if (positionAge > 10)
                {
                    ignitionOn = false;
                    rawSpeed = 0.0;
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
                        : (lastIgnitionOn.TryGetValue(deviceId, out var lastOn) ? lastOn : (DateTime?)null),
                    BatteryIsStartReading: battery.IsStartReading,
                    BatteryMeasuredAt: battery.MeasuredAt,
                    BatteryMedianVoltage: battery.MedianVolts
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

    /// <summary>Stats du jour agrégées en SQL (une ligne par device).</summary>
    public class DeviceStatRow
    {
        public int DeviceId { get; set; }
        public double MaxSpeed { get; set; }
        public double MovingMinutes { get; set; }
        public double StoppedMinutes { get; set; }
        public int TotalCount { get; set; }
    }
}



