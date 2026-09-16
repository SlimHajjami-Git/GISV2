using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.FleetManagement.SpeedLimits;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Admin.DeviceCommands;

/// <summary>
/// Envoi d'UNE commande AJ+ à un, plusieurs ou tous les boîtiers d'une société,
/// depuis l'écran admin. Même mécanique que la synchro des limites de vitesse :
/// une ligne device_commands par boîtier (trace d'audit : qui, quoi, quand), puis
/// un push immédiat vers le socket tenu par l'ingest Rust ; un boîtier hors ligne
/// garde sa ligne « pending » et la reçoit à sa prochaine trame.
///
/// <para>Portée : les boîtiers de <see cref="CompanyId"/> uniquement — un identifiant
/// d'une autre société passé dans <see cref="DeviceIds"/> est ignoré, pas une erreur,
/// pour qu'un écran mal rafraîchi ne puisse jamais toucher un autre client.</para>
/// </summary>
public record SendAdminDeviceCommandCommand(
    int CompanyId,
    string CommandText,
    IReadOnlyList<int>? DeviceIds,
    bool AllFleet) : IRequest<AdminDeviceCommandResult>;

/// <summary>Sort d'un boîtier : pushed / offline / failed / skipped_non_nems / blocked.</summary>
public record AdminDeviceCommandTargetResult(
    int DeviceId,
    string Imei,
    string? Plate,
    string? VehicleName,
    string Outcome,
    string? Detail);

public record AdminDeviceCommandResult(
    bool Accepted,
    string? Error,
    string CommandText,
    int Targeted,
    int PushedLive,
    int Offline,
    int Failed,
    int SkippedNonNems,
    int Blocked,
    IReadOnlyList<AdminDeviceCommandTargetResult> Details)
{
    public static AdminDeviceCommandResult Refused(string error, string text) =>
        new(false, error, text, 0, 0, 0, 0, 0, 0, Array.Empty<AdminDeviceCommandTargetResult>());
}

public class SendAdminDeviceCommandCommandHandler
    : IRequestHandler<SendAdminDeviceCommandCommand, AdminDeviceCommandResult>
{
    public const string CommandType = "ADMIN";
    public const string Source = "admin";

    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IRustCommandPusher _commandPusher;
    private readonly ILogger<SendAdminDeviceCommandCommandHandler> _logger;

    public SendAdminDeviceCommandCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IRustCommandPusher commandPusher,
        ILogger<SendAdminDeviceCommandCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _commandPusher = commandPusher;
        _logger = logger;
    }

    public async Task<AdminDeviceCommandResult> Handle(SendAdminDeviceCommandCommand request, CancellationToken ct)
    {
        var check = DeviceCommandSafety.Check(request.CommandText);
        if (!check.Ok)
            return AdminDeviceCommandResult.Refused(check.Reason!, request.CommandText ?? string.Empty);

        var commandText = check.Normalized;

        if (!request.AllFleet && (request.DeviceIds == null || request.DeviceIds.Count == 0))
            return AdminDeviceCommandResult.Refused("Aucun boîtier sélectionné.", commandText);

        // IgnoreQueryFilters : l'écran admin n'a pas de tenant ; la société est
        // épinglée explicitement, et c'est la SEULE frontière qui compte ici.
        var devicesQuery = _context.GpsDevices
            .IgnoreQueryFilters()
            .Include(d => d.Vehicle)
            .Where(d => d.CompanyId == request.CompanyId);

        if (!request.AllFleet)
        {
            var ids = request.DeviceIds!.Distinct().ToList();
            devicesQuery = devicesQuery.Where(d => ids.Contains(d.Id));
        }

        var devices = await devicesQuery
            .OrderBy(d => d.Vehicle != null ? d.Vehicle.Plate : d.Label)
            .ToListAsync(ct);

        if (devices.Count == 0)
            return AdminDeviceCommandResult.Refused("Aucun boîtier trouvé pour cette société avec cette sélection.", commandText);

        var actorUserId = _tenantService.UserId ?? 0;
        var details = new List<AdminDeviceCommandTargetResult>(devices.Count);
        var queued = new List<(GpsDevice Device, DeviceCommand Cmd, int DetailIndex)>();
        int skippedNonNems = 0, blocked = 0;

        foreach (var device in devices)
        {
            var plate = device.Vehicle?.Plate;
            var vehicleName = device.Vehicle?.Name;

            // Le protocole AJ+ n'existe que sur les NEMS : un Teltonika ou un Noron
            // interpréterait le texte comme du bruit, au mieux.
            if (!SpeedLimitCommandBuilder.IsNemsDevice(device))
            {
                skippedNonNems++;
                details.Add(new AdminDeviceCommandTargetResult(device.Id, device.DeviceUid, plate, vehicleName,
                    "skipped_non_nems", $"Protocole {device.ProtocolType ?? device.Brand ?? "inconnu"} : pas de commandes AJ+"));
                continue;
            }

            // Deuxième verrou, propre au boîtier : sa commande STOP configurée, même
            // si elle ne contenait pas le mot (mot de passe différent, texte custom).
            if (string.Equals(commandText.Trim(), (device.CommandStop ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                blocked++;
                details.Add(new AdminDeviceCommandTargetResult(device.Id, device.DeviceUid, plate, vehicleName,
                    "blocked", "Texte identique à la commande d'immobilisation du boîtier"));
                continue;
            }

            var cmd = new DeviceCommand
            {
                DeviceId = device.Id,
                VehicleId = device.Vehicle?.Id,
                UserId = actorUserId,
                CommandType = CommandType,
                CommandText = commandText,
                Status = "pending",
                Source = Source,
                CompanyId = device.CompanyId
            };
            _context.DeviceCommands.Add(cmd);
            details.Add(new AdminDeviceCommandTargetResult(device.Id, device.DeviceUid, plate, vehicleName, "pending", null));
            queued.Add((device, cmd, details.Count - 1));
        }

        await _context.SaveChangesAsync(ct);

        // Push séquentiel, comme la synchro vitesse : pas de rafale sur la couche TCP.
        int pushedLive = 0, offline = 0, failed = 0;
        foreach (var (device, cmd, idx) in queued)
        {
            string outcome;
            string? detail;
            try
            {
                var push = await _commandPusher.PushAsync(device.Id, cmd.CommandText, ct);
                switch (push.Outcome)
                {
                    case RustPushOutcome.Pushed:
                        pushedLive++;
                        cmd.Status = "sent";
                        cmd.SentAt = DateTime.UtcNow;
                        cmd.Attempts += 1;
                        outcome = "pushed";
                        detail = "Écrite sur le socket du boîtier";
                        break;
                    case RustPushOutcome.DeviceNotConnected:
                        offline++;
                        outcome = "offline";
                        detail = "Boîtier hors ligne : livrée à sa prochaine trame";
                        break;
                    default:
                        failed++;
                        outcome = "failed";
                        detail = string.IsNullOrWhiteSpace(push.Message) ? "Push refusé par l'ingest ; livrée à la prochaine trame" : push.Message;
                        break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                outcome = "failed";
                detail = "Ingest injoignable ; livrée à la prochaine trame";
                _logger.LogWarning(ex, "Admin command push failed for device {DeviceId} (stays pending).", device.Id);
            }
            details[idx] = details[idx] with { Outcome = outcome, Detail = detail };
        }

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Admin device command by user {User} to company {Company}: text={Text} targeted={Targeted} live={Live} offline={Offline} failed={Failed} skippedNonNems={Skipped} blocked={Blocked}",
            actorUserId, request.CompanyId, commandText.TrimEnd('\n'), devices.Count, pushedLive, offline, failed, skippedNonNems, blocked);

        return new AdminDeviceCommandResult(
            true, null, commandText, devices.Count, pushedLive, offline, failed, skippedNonNems, blocked, details);
    }
}
