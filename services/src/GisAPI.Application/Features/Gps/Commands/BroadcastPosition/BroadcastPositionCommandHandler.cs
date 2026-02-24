using System.Collections.Concurrent;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Gps.Commands.BroadcastPosition;

/// <summary>
/// Cached device→vehicle mapping to avoid DB queries per GPS frame
/// </summary>
public record DeviceCacheEntry(
    int DeviceId,
    string DeviceUid,
    int CompanyId,
    int? VehicleId,
    string? VehicleName,
    string? Plate,
    string FuelSensorMode,
    int FuelTankCapacity,
    DateTime CachedAt
);

/// <summary>
/// Handler for broadcasting GPS position updates in real-time
/// Uses in-memory cache for device lookups and dedup to prevent double broadcasts
/// </summary>
public class BroadcastPositionCommandHandler : IRequestHandler<BroadcastPositionCommand, BroadcastPositionResult>
{
    private readonly IGisDbContext _context;
    private readonly IGpsHubService _gpsHubService;
    private readonly IPublisher _publisher;
    private readonly ILogger<BroadcastPositionCommandHandler> _logger;

    private const double SPEED_THRESHOLD = 10.0; // km/h threshold for "moving"

    // Static caches shared across all handler instances (scoped per request)
    private static readonly ConcurrentDictionary<string, DeviceCacheEntry> _deviceCache = new();
    private static readonly ConcurrentDictionary<string, DateTime> _lastBroadcast = new();
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan _dedupWindow = TimeSpan.FromSeconds(2);

    // Speed alert cooldown: don't spam notifications for the same vehicle
    private static readonly ConcurrentDictionary<int, DateTime> _speedAlertCooldown = new();
    private static readonly TimeSpan _speedAlertCooldownPeriod = TimeSpan.FromMinutes(5);

    // Driving behavior cooldown per vehicle
    private static readonly ConcurrentDictionary<string, DateTime> _behaviorCooldown = new();
    private static readonly TimeSpan _behaviorCooldownPeriod = TimeSpan.FromMinutes(3);

    // Geofence state tracking: vehicleId -> set of geofenceIds the vehicle is currently inside
    private static readonly ConcurrentDictionary<int, HashSet<int>> _vehicleGeofenceState = new();
    // Geofence cache: companyId -> list of active geofences (refreshed every 2 minutes)
    private static readonly ConcurrentDictionary<int, (DateTime CachedAt, List<GeofenceCacheEntry> Geofences)> _geofenceCache = new();
    private static readonly TimeSpan _geofenceCacheTtl = TimeSpan.FromMinutes(2);

    public BroadcastPositionCommandHandler(
        IGisDbContext context,
        IGpsHubService gpsHubService,
        IPublisher publisher,
        ILogger<BroadcastPositionCommandHandler> logger)
    {
        _context = context;
        _gpsHubService = gpsHubService;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<BroadcastPositionResult> Handle(BroadcastPositionCommand request, CancellationToken ct)
    {
        var ignitionOn = request.IgnitionOn ?? false;
        var speed = request.SpeedKph ?? 0;

        // Dedup: skip if same device+recordedAt was already broadcast (Redis+RMQ both fire)
        var dedupKey = $"{request.DeviceUid}:{request.RecordedAt:O}";
        var now = DateTime.UtcNow;
        if (_lastBroadcast.TryGetValue(dedupKey, out _))
        {
            return new BroadcastPositionResult(false, null, "Duplicate (already broadcast)");
        }
        _lastBroadcast[dedupKey] = now;

        // Cleanup old dedup entries periodically (every ~100 entries)
        if (_lastBroadcast.Count > 500)
        {
            var cutoff = now - TimeSpan.FromMinutes(2);
            foreach (var key in _lastBroadcast.Keys)
            {
                if (_lastBroadcast.TryGetValue(key, out var ts) && ts < cutoff)
                    _lastBroadcast.TryRemove(key, out _);
            }
        }

        // Look up device from cache first, then DB
        var cached = GetCachedDevice(request.DeviceUid);
        if (cached == null)
        {
            var device = await _context.GpsDevices
                .AsNoTracking()
                .Include(d => d.Vehicle)
                .FirstOrDefaultAsync(d => d.DeviceUid == request.DeviceUid, ct);

            if (device == null)
            {
                _logger.LogDebug("Device not found: {DeviceUid}", request.DeviceUid);
                return new BroadcastPositionResult(false, null, "Device not found");
            }

            cached = new DeviceCacheEntry(
                device.Id, device.DeviceUid, device.CompanyId,
                device.Vehicle?.Id, device.Vehicle?.Name, device.Vehicle?.Plate,
                device.FuelSensorMode ?? "raw_255",
                device.Vehicle?.FuelTankCapacity ?? 60,
                DateTime.UtcNow);
            _deviceCache[request.DeviceUid] = cached;
        }

        // Round speed to whole number, set to 0 if ignition off
        var displaySpeed = ignitionOn ? Math.Round(speed) : 0;

        // Convert fuelRaw → percent based on fuel_sensor_mode (same logic as monitoring query)
        int? fuelLevel = null;
        if (request.FuelRaw.HasValue && request.FuelRaw.Value > 0)
        {
            var raw = request.FuelRaw.Value;
            var tank = cached.FuelTankCapacity;
            fuelLevel = cached.FuelSensorMode switch
            {
                "percent" => raw,
                "raw_255" => (int)Math.Round(raw / 255.0 * 100.0),
                "liters" => tank > 0 ? (int)Math.Round(raw * 100.0 / tank) : raw,
                "half_liter" => tank > 0 ? (int)Math.Round(raw * 0.5 * 100.0 / tank) : (int)Math.Round(raw * 0.5),
                _ => raw
            };
            if (fuelLevel > 100) fuelLevel = 100;
            if (fuelLevel < 0) fuelLevel = 0;
        }

        // Prepare position update DTO
        var positionUpdate = new VehiclePositionUpdateDto
        {
            DeviceId = cached.DeviceId,
            DeviceUid = cached.DeviceUid,
            VehicleId = cached.VehicleId,
            VehicleName = cached.VehicleName,
            Plate = cached.Plate,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            SpeedKph = displaySpeed,
            CourseDeg = request.CourseDeg ?? 0,
            IgnitionOn = ignitionOn,
            IsMoving = ignitionOn && speed >= SPEED_THRESHOLD,
            FuelRaw = fuelLevel,
            BatteryVoltage = request.BatteryVoltage,
            BatteryPercent = request.BatteryPercent,
            TemperatureC = request.TemperatureC,
            RecordedAt = request.RecordedAt,
            Timestamp = now
        };

        // Broadcast to company group
        if (cached.CompanyId > 0)
        {
            await _gpsHubService.SendPositionUpdateAsync(cached.CompanyId, positionUpdate);
        }

        // Broadcast to specific vehicle subscribers
        if (cached.VehicleId.HasValue)
        {
            await _gpsHubService.SendVehiclePositionAsync(cached.VehicleId.Value, positionUpdate);
        }

        // Handle alerts — persist to DB + broadcast via SignalR
        if (!string.IsNullOrEmpty(request.AlertType) && 
            request.AlertType != "normal" && 
            request.AlertType != "periodic")
        {
            // Persist alert to DB
            var severity = request.AlertType == "overspeed" && speed > 140 ? "high"
                : request.AlertType == "overspeed" ? "medium"
                : "low";

            var vehicleLabel = cached.VehicleName ?? cached.Plate ?? $"Device {cached.DeviceUid}";
            var alertMessage = request.AlertType switch
            {
                "overspeed" => $"{vehicleLabel} — excès de vitesse: {speed:F0} km/h",
                "ignition_on" => $"{vehicleLabel} — contact mis",
                "ignition_off" => $"{vehicleLabel} — contact coupé",
                "sos" => $"{vehicleLabel} — alerte SOS",
                "battery_low" => $"{vehicleLabel} — batterie faible",
                _ => $"{vehicleLabel} — alerte: {request.AlertType}"
            };

            var gpsAlert = new GisAPI.Domain.Entities.GpsAlert
            {
                DeviceId = cached.DeviceId,
                VehicleId = cached.VehicleId,
                CompanyId = cached.CompanyId,
                Type = request.AlertType,
                Severity = severity,
                Message = alertMessage,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Timestamp = request.RecordedAt
            };

            try
            {
                _context.GpsAlerts.Add(gpsAlert);
                await _context.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist GpsAlert for device {DeviceUid}", request.DeviceUid);
            }

            // Broadcast via SignalR
            var alertDto = new VehicleAlertDto
            {
                DeviceId = cached.DeviceId,
                VehicleId = cached.VehicleId,
                VehicleName = cached.VehicleName,
                Type = request.AlertType,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Timestamp = request.RecordedAt
            };

            if (cached.CompanyId > 0)
            {
                await _gpsHubService.SendAlertAsync(cached.CompanyId, alertDto);
            }

            // Publish speed alert notification (with cooldown per vehicle)
            if (request.AlertType == "overspeed" && cached.VehicleId.HasValue && speed > 0)
            {
                var vehicleId = cached.VehicleId.Value;
                var shouldNotify = true;
                if (_speedAlertCooldown.TryGetValue(vehicleId, out var lastAlert))
                {
                    shouldNotify = (now - lastAlert) > _speedAlertCooldownPeriod;
                }

                if (shouldNotify)
                {
                    _speedAlertCooldown[vehicleId] = now;
                    _ = _publisher.Publish(new SpeedAlertNotificationEvent(
                        cached.CompanyId, cached.VehicleId, cached.VehicleName, cached.Plate,
                        speed, request.Latitude, request.Longitude, request.RecordedAt
                    ), ct);
                }
            }

            // Publish driving behavior notifications (harsh_braking, rapid_acceleration, sharp_turn, etc.)
            var drivingBehaviors = new[] { "harsh_braking", "rapid_acceleration", "sharp_turn", "pothole", "jerk" };
            if (cached.VehicleId.HasValue && !string.IsNullOrEmpty(request.AlertType) && drivingBehaviors.Contains(request.AlertType))
            {
                var cooldownKey = $"{cached.VehicleId.Value}:{request.AlertType}";
                var shouldNotifyBehavior = true;
                if (_behaviorCooldown.TryGetValue(cooldownKey, out var lastBehavior))
                {
                    shouldNotifyBehavior = (now - lastBehavior) > _behaviorCooldownPeriod;
                }

                if (shouldNotifyBehavior)
                {
                    _behaviorCooldown[cooldownKey] = now;
                    _ = _publisher.Publish(new DrivingBehaviorNotificationEvent(
                        cached.CompanyId, cached.VehicleId, cached.VehicleName, cached.Plate,
                        request.AlertType, speed, request.Latitude, request.Longitude, request.RecordedAt
                    ), ct);
                }
            }
        }

        // Geofence checking (must be awaited — uses scoped DbContext)
        if (cached.VehicleId.HasValue && cached.CompanyId > 0)
        {
            try
            {
                await CheckGeofences(cached.CompanyId, cached.VehicleId.Value, cached.VehicleName, cached.Plate,
                    request.Latitude, request.Longitude, speed, request.RecordedAt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Geofence check failed for vehicle {VehicleId}", cached.VehicleId);
            }
        }

        _logger.LogInformation(
            "📡 SignalR Broadcast: Device={DeviceUid}, Vehicle={VehicleName}, VehicleId={VehicleId}, CompanyId={CompanyId}, Speed={Speed}km/h, IsMoving={IsMoving}",
            request.DeviceUid, cached.VehicleName, cached.VehicleId, cached.CompanyId, displaySpeed, positionUpdate.IsMoving);

        return new BroadcastPositionResult(
            Broadcasted: true,
            VehicleId: cached.VehicleId,
            SkipReason: null
        );
    }

    private static DeviceCacheEntry? GetCachedDevice(string deviceUid)
    {
        if (_deviceCache.TryGetValue(deviceUid, out var entry))
        {
            if ((DateTime.UtcNow - entry.CachedAt) < _cacheTtl)
                return entry;
            _deviceCache.TryRemove(deviceUid, out _);
        }
        return null;
    }

    /// <summary>
    /// Check vehicle position against all company geofences.
    /// Detects entry/exit transitions and publishes notification events.
    /// </summary>
    private async Task CheckGeofences(int companyId, int vehicleId, string? vehicleName, string? plate,
        double lat, double lng, double speed, DateTime timestamp, CancellationToken ct)
    {
        // Get cached geofences for this company
        var geofences = await GetCompanyGeofences(companyId, ct);
        if (geofences.Count == 0) return;

        // Get current state for this vehicle
        var currentInside = _vehicleGeofenceState.GetOrAdd(vehicleId, _ => new HashSet<int>());
        var newInside = new HashSet<int>();

        foreach (var gf in geofences)
        {
            var isInside = IsPointInGeofence(lat, lng, gf);
            if (isInside) newInside.Add(gf.Id);

            var wasInside = currentInside.Contains(gf.Id);

            // Entry detection
            if (isInside && !wasInside && gf.AlertOnEntry)
            {
                _logger.LogInformation("🔔 Geofence ENTRY: Vehicle {Vehicle} entered \"{Geofence}\"",
                    vehicleName ?? $"#{vehicleId}", gf.Name);

                await _publisher.Publish(new GeofenceNotificationEvent(
                    companyId, vehicleId, vehicleName ?? plate, gf.Id, gf.Name,
                    "entry", lat, lng, timestamp
                ), ct);

                // Also broadcast via SignalR to geofence subscribers
                await _gpsHubService.SendGeofenceEventAsync(gf.Id, new
                {
                    geofenceId = gf.Id,
                    vehicleId,
                    vehicleName = vehicleName ?? plate,
                    eventType = "entry",
                    latitude = lat,
                    longitude = lng,
                    timestamp
                });
            }

            // Exit detection
            if (!isInside && wasInside && gf.AlertOnExit)
            {
                _logger.LogInformation("🔔 Geofence EXIT: Vehicle {Vehicle} left \"{Geofence}\"",
                    vehicleName ?? $"#{vehicleId}", gf.Name);

                await _publisher.Publish(new GeofenceNotificationEvent(
                    companyId, vehicleId, vehicleName ?? plate, gf.Id, gf.Name,
                    "exit", lat, lng, timestamp
                ), ct);

                await _gpsHubService.SendGeofenceEventAsync(gf.Id, new
                {
                    geofenceId = gf.Id,
                    vehicleId,
                    vehicleName = vehicleName ?? plate,
                    eventType = "exit",
                    latitude = lat,
                    longitude = lng,
                    timestamp
                });
            }
        }

        // Update state
        _vehicleGeofenceState[vehicleId] = newInside;
    }

    private async Task<List<GeofenceCacheEntry>> GetCompanyGeofences(int companyId, CancellationToken ct)
    {
        if (_geofenceCache.TryGetValue(companyId, out var cached) && (DateTime.UtcNow - cached.CachedAt) < _geofenceCacheTtl)
        {
            return cached.Geofences;
        }

        var geofences = await _context.Geofences
            .AsNoTracking()
            .Where(g => g.CompanyId == companyId && g.IsActive)
            .Select(g => new GeofenceCacheEntry(
                g.Id, g.Name, g.Type, g.Coordinates, g.CenterLat, g.CenterLng, g.Radius,
                g.AlertOnEntry, g.AlertOnExit
            ))
            .ToListAsync(ct);

        _geofenceCache[companyId] = (DateTime.UtcNow, geofences);
        return geofences;
    }

    /// <summary>
    /// Point-in-polygon / point-in-circle test for geofence containment
    /// </summary>
    private static bool IsPointInGeofence(double lat, double lng, GeofenceCacheEntry gf)
    {
        if (gf.Type == "circle" && gf.CenterLat.HasValue && gf.CenterLng.HasValue && gf.Radius.HasValue)
        {
            var dist = HaversineDistance(lat, lng, gf.CenterLat.Value, gf.CenterLng.Value);
            return dist <= gf.Radius.Value;
        }

        if (gf.Coordinates == null || gf.Coordinates.Length < 3) return false;

        // Ray-casting algorithm for polygon
        var inside = false;
        var n = gf.Coordinates.Length;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var yi = gf.Coordinates[i].Lat;
            var xi = gf.Coordinates[i].Lng;
            var yj = gf.Coordinates[j].Lat;
            var xj = gf.Coordinates[j].Lng;

            if (((yi > lat) != (yj > lat)) && (lng < (xj - xi) * (lat - yi) / (yj - yi) + xi))
                inside = !inside;
        }
        return inside;
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }
}

public record GeofenceCacheEntry(
    int Id,
    string Name,
    string Type,
    GisAPI.Domain.Entities.GeofencePoint[]? Coordinates,
    double? CenterLat,
    double? CenterLng,
    double? Radius,
    bool AlertOnEntry,
    bool AlertOnExit
);

/// <summary>
/// DTO for vehicle position updates sent via SignalR
/// </summary>
public class VehiclePositionUpdateDto
{
    public int DeviceId { get; set; }
    public string DeviceUid { get; set; } = string.Empty;
    public int? VehicleId { get; set; }
    public string? VehicleName { get; set; }
    public string? Plate { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double SpeedKph { get; set; }
    public double CourseDeg { get; set; }
    public bool IgnitionOn { get; set; }
    public bool IsMoving { get; set; }
    public int? FuelRaw { get; set; }
    public double? BatteryVoltage { get; set; }
    public int? BatteryPercent { get; set; }
    public int? TemperatureC { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// DTO for vehicle alerts sent via SignalR
/// </summary>
public class VehicleAlertDto
{
    public int DeviceId { get; set; }
    public int? VehicleId { get; set; }
    public string? VehicleName { get; set; }
    public string Type { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Timestamp { get; set; }
}



