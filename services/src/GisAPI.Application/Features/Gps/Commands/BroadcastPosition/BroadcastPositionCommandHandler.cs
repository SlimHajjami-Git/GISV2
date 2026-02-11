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

        // Dedup: skip if same device+recordedAt was broadcast within 2 seconds
        var dedupKey = $"{request.DeviceUid}:{request.RecordedAt:O}";
        var now = DateTime.UtcNow;
        if (_lastBroadcast.TryGetValue(dedupKey, out var lastTime) && (now - lastTime) < _dedupWindow)
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
                DateTime.UtcNow);
            _deviceCache[request.DeviceUid] = cached;
        }

        // Round speed to whole number, set to 0 if ignition off
        var displaySpeed = ignitionOn ? Math.Round(speed) : 0;

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
}

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



