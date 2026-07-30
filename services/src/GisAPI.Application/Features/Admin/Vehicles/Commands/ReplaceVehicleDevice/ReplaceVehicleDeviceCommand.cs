using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Commands.ReplaceVehicleDevice;

/// <summary>
/// Remplacement du boîtier GPS d'un véhicule (matériel changé sur le terrain).
///
/// POURQUOI CETTE COMMANDE — le garde-fou anti-doublons (GpsDeviceUniquenessGuard)
/// refuse à juste titre deux boîtiers partageant un IMEI, un MAT ou une SIM. Mais
/// il n'offrait AUCUNE issue au cas le plus banal de l'exploitation : on change
/// physiquement le boîtier d'un véhicule, le nouvel IMEI a souvent déjà été
/// pré-enregistré (fiche vide créée à la réception du matériel), et l'écran
/// refusait alors la modification sans rien proposer. L'opérateur devait appeler
/// pour une intervention en base — c'est arrivé le 30/07/2026 sur HTZ 139.
///
/// SÉMANTIQUE — on renomme le boîtier EN PLACE plutôt que de basculer le véhicule
/// vers une autre fiche. C'est volontaire : les positions sont indexées par
/// device_id et le lien véhicule→boîtier est unique, si bien qu'un basculement
/// vers une nouvelle fiche ferait DISPARAÎTRE tout l'historique du véhicule
/// (GetVehicleHistory, rapports kilométriques et carburant partent tous de
/// vehicle.GpsDeviceId). Le renommage préserve la continuité.
///
/// La fiche qui occupait le nouvel identifiant n'est supprimée que si elle est
/// STRICTEMENT VIDE. Si elle porte la moindre donnée, on refuse en le disant.
/// </summary>
public record ReplaceVehicleDeviceCommand(
    int VehicleId,
    string NewImei,
    string? NewSimNumber = null,
    string? NewMat = null,
    string? NewSimOperator = null
) : IRequest<ReplaceVehicleDeviceResult>;

public record ReplaceVehicleDeviceResult(
    bool Success,
    string Message,
    int? DeviceId = null,
    string? PreviousImei = null,
    int? ReleasedDeviceId = null);

public class ReplaceVehicleDeviceCommandHandler
    : IRequestHandler<ReplaceVehicleDeviceCommand, ReplaceVehicleDeviceResult>
{
    private readonly IGisDbContext _context;

    public ReplaceVehicleDeviceCommandHandler(IGisDbContext context) => _context = context;

    public async Task<ReplaceVehicleDeviceResult> Handle(
        ReplaceVehicleDeviceCommand request, CancellationToken ct)
    {
        var newImei = (request.NewImei ?? "").Trim();
        if (newImei.Length == 0)
            return new(false, "L'IMEI du nouveau boîtier est obligatoire.");

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId, ct);
        if (vehicle == null)
            return new(false, "Véhicule introuvable.");

        if (!vehicle.GpsDeviceId.HasValue)
            return new(false,
                "Ce véhicule n'a aucun boîtier à remplacer. Utilisez « Ajouter un appareil ».");

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.Id == vehicle.GpsDeviceId.Value, ct);
        if (device == null)
            return new(false, "Le boîtier actuel du véhicule est introuvable.");

        var previousImei = device.DeviceUid;

        var targetMat = string.IsNullOrWhiteSpace(request.NewMat) ? device.Mat : request.NewMat!.Trim();
        var targetSim = string.IsNullOrWhiteSpace(request.NewSimNumber) ? device.SimNumber : request.NewSimNumber!.Trim();

        // ── Fiches occupant déjà l'un des nouveaux identifiants ──
        var conflicts = await FindConflictingDevicesAsync(device.Id, newImei, targetMat, targetSim, ct);

        int? released = null;
        foreach (var conflict in conflicts)
        {
            var blocker = await DescribeDataAsync(conflict.Id, ct);
            if (blocker != null)
                return new(false,
                    $"Remplacement impossible : le boîtier #{conflict.Id} porte déjà cet identifiant " +
                    $"et contient des données ({blocker}). Traitez-le d'abord — un boîtier avec de " +
                    "l'historique ne doit pas être supprimé à l'aveugle.");

            // Fiche strictement vide : on la retire pour libérer l'identifiant.
            _context.GpsDevices.Remove(conflict);
            released = conflict.Id;
        }

        device.DeviceUid = newImei;
        if (!string.IsNullOrWhiteSpace(request.NewMat)) device.Mat = request.NewMat!.Trim();
        if (!string.IsNullOrWhiteSpace(request.NewSimNumber)) device.SimNumber = request.NewSimNumber!.Trim();
        if (!string.IsNullOrWhiteSpace(request.NewSimOperator)) device.SimOperator = request.NewSimOperator!.Trim();
        device.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        var msg = $"Boîtier remplacé : IMEI {previousImei} → {newImei}."
                + (released.HasValue ? $" Fiche vide #{released} libérée." : "")
                + " L'historique du véhicule est conservé.";
        return new(true, msg, device.Id, previousImei, released);
    }

    /// <summary>Fiches (autres que celle du véhicule) portant l'un des identifiants visés.</summary>
    private async Task<List<Domain.Entities.GpsDevice>> FindConflictingDevicesAsync(
        int excludeId, string imei, string? mat, string? sim, CancellationToken ct)
    {
        var nImei = GpsDeviceUniquenessGuard.Normalize(imei);
        var nMat = GpsDeviceUniquenessGuard.Normalize(mat);
        var nSim = GpsDeviceUniquenessGuard.Normalize(sim);

        // gps_devices tient en quelques centaines de lignes : on compare en mémoire
        // pour appliquer la MÊME normalisation que le garde-fou (espaces, casse).
        var all = await _context.GpsDevices.ToListAsync(ct);
        return all.Where(d => d.Id != excludeId && (
                (nImei.Length > 0 && GpsDeviceUniquenessGuard.Normalize(d.DeviceUid) == nImei) ||
                (nMat.Length > 0 && GpsDeviceUniquenessGuard.Normalize(d.Mat) == nMat) ||
                (nSim.Length > 0 && GpsDeviceUniquenessGuard.Normalize(d.SimNumber) == nSim)))
            .ToList();
    }

    /// <summary>
    /// Décrit ce que porte une fiche boîtier, ou null si elle est strictement vide.
    /// Sert de garde-fou avant suppression : on ne supprime jamais une fiche qui
    /// détient de l'historique.
    /// </summary>
    private async Task<string?> DescribeDataAsync(int deviceId, CancellationToken ct)
    {
        var parts = new List<string>();

        var vehicles = await _context.Vehicles.CountAsync(v => v.GpsDeviceId == deviceId, ct);
        if (vehicles > 0) parts.Add($"{vehicles} véhicule(s) rattaché(s)");

        var positions = await _context.GpsPositions.CountAsync(p => p.DeviceId == deviceId, ct);
        if (positions > 0) parts.Add($"{positions} position(s)");

        var alerts = await _context.GpsAlerts.CountAsync(a => a.DeviceId == deviceId, ct);
        if (alerts > 0) parts.Add($"{alerts} alerte(s)");

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
