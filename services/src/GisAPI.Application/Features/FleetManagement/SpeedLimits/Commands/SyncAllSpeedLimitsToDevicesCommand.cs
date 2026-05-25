using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.FleetManagement.SpeedLimits.Commands;

/// <summary>
/// One-shot backfill (Calypso 9 p6): re-pushes every vehicle's already-stored
/// km/h speed limit to its physical boitier. Needed because limits set before
/// the AJ+CONFN wiring existed only ever updated the DB, never the hardware.
///
/// Platform-wide: ignores tenant filters and processes ALL companies. Intended
/// to be triggered by a super admin via POST /api/admin/speed-limits/sync-devices.
/// </summary>
public record SyncAllSpeedLimitsToDevicesCommand() : IRequest<SyncSpeedLimitsResult>;

public record SyncSpeedLimitsResult(
    int TotalVehiclesWithLimit,
    int Queued,
    int PushedLive,
    int Offline,
    int Failed,
    int SkippedNonNems
);

public class SyncAllSpeedLimitsToDevicesCommandHandler
    : IRequestHandler<SyncAllSpeedLimitsToDevicesCommand, SyncSpeedLimitsResult>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IRustCommandPusher _commandPusher;
    private readonly ILogger<SyncAllSpeedLimitsToDevicesCommandHandler> _logger;

    public SyncAllSpeedLimitsToDevicesCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IRustCommandPusher commandPusher,
        ILogger<SyncAllSpeedLimitsToDevicesCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _commandPusher = commandPusher;
        _logger = logger;
    }

    public async Task<SyncSpeedLimitsResult> Handle(SyncAllSpeedLimitsToDevicesCommand request, CancellationToken ct)
    {
        var actorUserId = _tenantService.UserId ?? 0;

        // Platform-wide: IgnoreQueryFilters so we reach every company's
        // vehicles regardless of the caller's tenant context. Only vehicles
        // that actually have a speed limit AND an attached boitier matter.
        var vehicles = await _context.Vehicles
            .IgnoreQueryFilters()
            .Include(v => v.GpsDevice)
            .Where(v => v.SpeedLimit != null
                     && v.SpeedLimit > 0
                     && v.GpsDeviceId != null)
            .ToListAsync(ct);

        var total = vehicles.Count;
        if (total == 0)
        {
            _logger.LogInformation("Speed-limit device sync: no vehicles with a limit + boitier found.");
            return new SyncSpeedLimitsResult(0, 0, 0, 0, 0, 0);
        }

        // 1) Build + persist all pending DeviceCommand rows in one batch.
        //    NEMS-only: Noron / non-AJ+ devices are skipped (AJ+CONFN would
        //    be mis-handled). Their km/h limit is already in the DB for the
        //    in-app alert; only hardware programming is skipped.
        var skippedNonNems = 0;
        var commands = new List<(GpsDevice Device, DeviceCommand Cmd)>(total);
        foreach (var v in vehicles)
        {
            var device = v.GpsDevice!;
            if (!SpeedLimitCommandBuilder.IsNemsDevice(device))
            {
                skippedNonNems++;
                continue;
            }
            var commandText = SpeedLimitCommandBuilder.Build(v.SpeedLimit!.Value, device.CommandGo);
            var cmd = new DeviceCommand
            {
                DeviceId = device.Id,
                VehicleId = v.Id,
                UserId = actorUserId,
                CommandType = "SPEED_LIMIT",
                CommandText = commandText,
                Status = "pending",
                Source = "manual",
                CompanyId = v.CompanyId
            };
            _context.DeviceCommands.Add(cmd);
            commands.Add((device, cmd));
        }
        await _context.SaveChangesAsync(ct);
        var queued = commands.Count;

        // 2) Push sequentially for ~ms live delivery. Sequential (not
        //    Task.WhenAll) so we don't flood Rust / the TCP layer with a
        //    burst across the whole fleet. Offline devices keep their
        //    pending row — Rust's per-frame DB poll delivers on reconnect.
        var pushedLive = 0;
        var offline = 0;
        var failed = 0;
        foreach (var (device, cmd) in commands)
        {
            try
            {
                var push = await _commandPusher.PushAsync(device.Id, cmd.CommandText, ct);
                switch (push.Outcome)
                {
                    case RustPushOutcome.Pushed: pushedLive++; break;
                    case RustPushOutcome.DeviceNotConnected: offline++; break;
                    default: failed++; break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Speed-limit sync: push failed for device {DeviceId} (stays pending).", device.Id);
            }
        }

        _logger.LogInformation(
            "🚦 Speed-limit device sync complete: total={Total}, queued={Queued}, live={Live}, offline={Offline}, failed={Failed}, skippedNonNems={Skipped}",
            total, queued, pushedLive, offline, failed, skippedNonNems);

        return new SyncSpeedLimitsResult(total, queued, pushedLive, offline, failed, skippedNonNems);
    }
}
