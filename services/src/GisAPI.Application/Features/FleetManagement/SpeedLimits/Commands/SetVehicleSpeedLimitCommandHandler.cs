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

        // AJ+CONFN is a NEMS-only command. Noron (and any non-AJ+ device)
        // must not receive it — the km/h limit is still stored for the
        // in-app alert, only the hardware programming is skipped.
        if (!SpeedLimitCommandBuilder.IsNemsDevice(device))
        {
            _logger.LogInformation(
                "Speed limit {Limit} km/h saved for vehicle {VehicleId} but device {DeviceId} is not NEMS ({Brand}/{Model}/{Protocol}) — AJ+CONFN skipped.",
                request.SpeedLimit, vehicle.Id, device.Id, device.Brand, device.Model, device.ProtocolType);
            return;
        }

        var speedTenthsMph = SpeedLimitCommandBuilder.ToDeviceTenthsMph(request.SpeedLimit);
        var commandText = SpeedLimitCommandBuilder.Build(request.SpeedLimit, device.CommandGo);

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
        //
        // The Rust push path writes to the socket but does NOT update the
        // command status, so we mark it 'sent' here when the push is
        // accepted. This is the signal the /fleet speed-limits page uses to
        // decide whether the limit is actually programmed on the boitier
        // (a still-'pending' command → the page shows an empty field).
        try
        {
            var push = await _commandPusher.PushAsync(device.Id, commandText, cancellationToken);
            if (push.Outcome == RustPushOutcome.Pushed)
            {
                cmd.Status = "sent";
                cmd.SentAt = DateTime.UtcNow;
                cmd.Attempts += 1;
                await _context.SaveChangesAsync(cancellationToken);
            }
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
}
