using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.FleetManagement.SpeedLimits.Commands;

public class SetVehicleSpeedLimitCommandHandler : IRequestHandler<SetVehicleSpeedLimitCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IRustCommandPusher _commandPusher;
    private readonly ILogger<SetVehicleSpeedLimitCommandHandler> _logger;

    // km/h → MPH. The NEMS boitier expects the over-speed threshold in
    // TENTHS of a mile per hour (operator-confirmed: command field 377 = 37.7 MPH).
    private const double KmhToMph = 0.621371;

    public SetVehicleSpeedLimitCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IRustCommandPusher commandPusher,
        ILogger<SetVehicleSpeedLimitCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _commandPusher = commandPusher;
        _logger = logger;
    }

    public async Task Handle(SetVehicleSpeedLimitCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        if (request.SpeedLimit < 0 || request.SpeedLimit > 300)
            throw new InvalidOperationException("Speed limit must be between 0 and 300 km/h");

        // Load WITH the GPS device so we can program the boitier in the same pass.
        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, cancellationToken)
            ?? throw new InvalidOperationException($"Vehicle with ID {request.VehicleId} not found");

        // 1) Persist the km/h limit — this is the source of truth for the
        //    in-app over-speed alert (BroadcastPositionCommandHandler compares
        //    against SpeedLimit + 20 in km/h).
        vehicle.SpeedLimit = request.SpeedLimit;
        vehicle.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        // 2) Program the physical boitier. Calypso 9 p6 — setting the limit
        //    must actually reconfigure the device, not just the DB. The NEMS
        //    config command is:
        //        AJ+CONFN=101,3,2,<speed>,0,0,<password>
        //    where <speed> is tenths of MPH and the only field that varies;
        //    101,3,2 and the trailing 0,0 are constant (operator-confirmed).
        //    The password is reused from the device's stored CommandGo so a
        //    device with a non-default code still works.
        var device = vehicle.GpsDevice;
        if (device == null)
        {
            _logger.LogInformation(
                "Speed limit {Limit} km/h saved for vehicle {VehicleId} but no GPS device is attached — boitier not programmed.",
                request.SpeedLimit, vehicle.Id);
            return;
        }

        var speedTenthsMph = (int)Math.Round(request.SpeedLimit * KmhToMph * 10.0, MidpointRounding.AwayFromZero);
        var password = ExtractPassword(device.CommandGo);
        var commandText = $"AJ+CONFN=101,3,2,{speedTenthsMph},0,0,{password}\n";

        var cmd = new DeviceCommand
        {
            DeviceId = device.Id,
            VehicleId = vehicle.Id,
            UserId = _tenantService.UserId ?? 0,
            CommandType = "SPEED_LIMIT",
            CommandText = commandText,
            Status = "pending",
            Source = "manual",
            CompanyId = companyId
        };
        _context.DeviceCommands.Add(cmd);
        await _context.SaveChangesAsync(cancellationToken);

        // Push for ~ms delivery. Failure is non-fatal — Rust's per-frame DB
        // poll re-delivers the pending row on the device's next reconnect.
        try
        {
            var push = await _commandPusher.PushAsync(device.Id, commandText, cancellationToken);
            _logger.LogInformation(
                "🚦 Speed limit programmed: vehicle {VehicleId}, device {DeviceId}, {Limit} km/h → {Tenths} (0.1 MPH), push={Outcome}",
                vehicle.Id, device.Id, request.SpeedLimit, speedTenthsMph, push.Outcome);
        }
        catch (Exception ex)
        {
            // Pending row stays in DB; Rust fallback poll handles delivery.
            _logger.LogWarning(ex,
                "Speed limit command queued for device {DeviceId} but immediate push failed — will deliver on next frame.",
                device.Id);
        }
    }

    /// <summary>
    /// Pull the protection code (e.g. "#9999") out of the device's stored
    /// CommandGo ("AJ+GO#9999\n"). Falls back to the platform default so a
    /// freshly-seeded device without a custom code still gets a valid command.
    /// </summary>
    private static string ExtractPassword(string? commandGo)
    {
        if (string.IsNullOrWhiteSpace(commandGo)) return "#9999";
        var hash = commandGo.IndexOf('#');
        if (hash < 0) return "#9999";
        return commandGo.Substring(hash).Trim();
    }
}
