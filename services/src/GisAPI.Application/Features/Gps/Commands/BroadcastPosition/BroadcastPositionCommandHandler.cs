using System.Collections.Concurrent;
using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Gps.Commands.BroadcastPosition;

/// <summary>
/// Handler for broadcasting GPS position updates with adaptive interval logic
/// </summary>
public class BroadcastPositionCommandHandler : IRequestHandler<BroadcastPositionCommand, BroadcastPositionResult>
{
    private readonly IGisDbContext _context;
    private readonly IGpsHubService _gpsHubService;
    private readonly ILogger<BroadcastPositionCommandHandler> _logger;

    // Adaptive interval configuration (in seconds)
    private const int MOVING_VEHICLE_INTERVAL = 30;        // 30 seconds for moving vehicles
    private const int STOPPED_VEHICLE_INTERVAL = 120;      // 2 minutes for stopped (ignition on, speed < 10)
    private const int PARKED_VEHICLE_INTERVAL = 600;       // 10 minutes for parked (ignition off)
    private const double SPEED_THRESHOLD = 10.0;           // km/h threshold for "moving"

    // In-memory cache of last broadcast times per device
    private static readonly ConcurrentDictionary<string, LastBroadcastInfo> _lastBroadcasts = new();

    public BroadcastPositionCommandHandler(
        IGisDbContext context,
        IGpsHubService gpsHubService,
        ILogger<BroadcastPositionCommandHandler> logger)
    {
        _context = context;
        _gpsHubService = gpsHubService;
        _logger = logger;
    }

    public async Task<BroadcastPositionResult> Handle(BroadcastPositionCommand request, CancellationToken ct)
    {
        // Determine vehicle state and required interval
        var ignitionOn = request.IgnitionOn ?? false;
        var speed = request.SpeedKph ?? 0;
        var requiredInterval = GetRequiredInterval(ignitionOn, speed);

        // Check if enough time has passed since last broadcast
        if (_lastBroadcasts.TryGetValue(request.DeviceUid, out var lastBroadcast))
        {
            var elapsed = (DateTime.UtcNow - lastBroadcast.Timestamp).TotalSeconds;
            
            if (elapsed < requiredInterval)
            {
                _logger.LogDebug(
                    "Skipping broadcast for {DeviceUid}: elapsed={Elapsed}s, required={Required}s",
                    request.DeviceUid, elapsed, requiredInterval);
                
                return new BroadcastPositionResult(
                    Broadcasted: false,
                    VehicleId: null,
                    SkipReason: $"Interval not reached ({elapsed:F0}s / {requiredInterval}s)"
                );
            }
        }

        // Look up device and vehicle
        var device = await _context.GpsDevices
            .AsNoTracking()
            .Include(d => d.Vehicle)
            .FirstOrDefaultAsync(d => d.DeviceUid == request.DeviceUid, ct);

        if (device == null)
        {
            _logger.LogDebug("Device not found: {DeviceUid}", request.DeviceUid);
            return new BroadcastPositionResult(false, null, "Device not found");
        }

        // Round speed to whole number, set to 0 if ignition off
        var displaySpeed = ignitionOn ? Math.Round(speed) : 0;

        // Prepare position update DTO
        var positionUpdate = new VehiclePositionUpdateDto
        {
            DeviceId = device.Id,
            DeviceUid = device.DeviceUid,
            VehicleId = device.Vehicle?.Id,
            VehicleName = device.Vehicle?.Name,
            Plate = device.Vehicle?.Plate,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            SpeedKph = displaySpeed,
            CourseDeg = request.CourseDeg ?? 0,
            IgnitionOn = ignitionOn,
            IsMoving = ignitionOn && speed >= SPEED_THRESHOLD,
            RecordedAt = request.RecordedAt,
            Timestamp = DateTime.UtcNow
        };

        // Broadcast to company group
        if (device.CompanyId > 0)
        {
            await _gpsHubService.SendPositionUpdateAsync(device.CompanyId, positionUpdate);
        }

        // Broadcast to specific vehicle subscribers
        if (device.Vehicle != null)
        {
            await _gpsHubService.SendVehiclePositionAsync(device.Vehicle.Id, positionUpdate);
        }

        // Handle alerts
        if (!string.IsNullOrEmpty(request.AlertType) && 
            request.AlertType != "normal" && 
            request.AlertType != "periodic")
        {
            var alert = new VehicleAlertDto
            {
                DeviceId = device.Id,
                VehicleId = device.Vehicle?.Id,
                VehicleName = device.Vehicle?.Name,
                Type = request.AlertType,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Timestamp = request.RecordedAt
            };

            if (device.CompanyId > 0)
            {
                await _gpsHubService.SendAlertAsync(device.CompanyId, alert);
            }
        }

        // Update last broadcast time
        _lastBroadcasts[request.DeviceUid] = new LastBroadcastInfo
        {
            Timestamp = DateTime.UtcNow,
            IgnitionOn = ignitionOn,
            Speed = speed
        };

        _logger.LogDebug(
            "Broadcasted position for {DeviceUid} (Vehicle: {VehicleName}, Speed: {Speed} km/h, Interval: {Interval}s)",
            request.DeviceUid, device.Vehicle?.Name, displaySpeed, requiredInterval);

        return new BroadcastPositionResult(
            Broadcasted: true,
            VehicleId: device.Vehicle?.Id,
            SkipReason: null
        );
    }

    private static int GetRequiredInterval(bool ignitionOn, double speed)
    {
        if (!ignitionOn)
        {
            // Parked vehicle (ignition off)
            return PARKED_VEHICLE_INTERVAL;
        }
        
        if (speed < SPEED_THRESHOLD)
        {
            // Stopped but ignition on (traffic, delivery, etc.)
            return STOPPED_VEHICLE_INTERVAL;
        }

        // Moving vehicle
        return MOVING_VEHICLE_INTERVAL;
    }

    private class LastBroadcastInfo
    {
        public DateTime Timestamp { get; set; }
        public bool IgnitionOn { get; set; }
        public double Speed { get; set; }
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
