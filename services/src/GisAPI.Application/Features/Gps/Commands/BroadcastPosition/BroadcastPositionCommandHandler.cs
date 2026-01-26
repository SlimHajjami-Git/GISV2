using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Gps.Commands.BroadcastPosition;

/// <summary>
/// Handler for broadcasting GPS position updates in real-time
/// Every GPS frame received is immediately broadcasted to connected clients
/// </summary>
public class BroadcastPositionCommandHandler : IRequestHandler<BroadcastPositionCommand, BroadcastPositionResult>
{
    private readonly IGisDbContext _context;
    private readonly IGpsHubService _gpsHubService;
    private readonly ILogger<BroadcastPositionCommandHandler> _logger;

    private const double SPEED_THRESHOLD = 10.0; // km/h threshold for "moving"

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
        var ignitionOn = request.IgnitionOn ?? false;
        var speed = request.SpeedKph ?? 0;

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

        _logger.LogInformation(
            "📡 SignalR Broadcast: Device={DeviceUid}, Vehicle={VehicleName}, VehicleId={VehicleId}, CompanyId={CompanyId}, Speed={Speed}km/h, IsMoving={IsMoving}",
            request.DeviceUid, device.Vehicle?.Name, device.Vehicle?.Id, device.CompanyId, displaySpeed, positionUpdate.IsMoving);

        return new BroadcastPositionResult(
            Broadcasted: true,
            VehicleId: device.Vehicle?.Id,
            SkipReason: null
        );
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
