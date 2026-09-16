using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Commands.ReplaceVehicleDevice;

/// <summary>
/// Remplacement du boîtier GPS d'un véhicule (matériel changé sur le terrain), ou
/// correction d'un IMEI mal saisi.
///
/// POURQUOI CETTE COMMANDE — le garde-fou anti-doublons (GpsDeviceUniquenessGuard)
/// refuse à juste titre deux boîtiers partageant un IMEI, un MAT ou une SIM. Mais
/// il n'offrait AUCUNE issue au cas le plus banal de l'exploitation : on change
/// physiquement le boîtier d'un véhicule, le nouvel IMEI a souvent déjà été
/// pré-enregistré (fiche vide créée à la réception du matériel), et l'écran
/// refusait alors la modification sans rien proposer. L'opérateur devait appeler
/// pour une intervention en base — c'est arrivé le 30/07/2026 sur HTZ 139.
///
/// DEUX MODES, choisis automatiquement selon la fiche qui porte les données
/// (voir <see cref="GpsDeviceReplacementPlanner"/>, qui sert aussi au message de doublon) :
///
/// 1. RENOMMAGE (historique) — la fiche DU VÉHICULE porte l'historique : on la
///    renomme EN PLACE plutôt que de basculer le véhicule vers une autre fiche. Les
///    positions sont indexées par device_id et le lien véhicule→boîtier est unique :
///    un basculement ferait DISPARAÎTRE l'historique du véhicule (GetVehicleHistory,
///    rapports kilométriques et carburant partent tous de vehicle.GpsDeviceId).
///
/// 2. RATTACHEMENT (correction d'une saisie erronée) — situation inverse, constatée
///    le 14/09/2026 sur HTZ 278, 255 et 292 : la fiche du véhicule avait été créée
///    avec un IMEI mal recopié et n'a jamais reçu une trame, pendant que le vrai
///    boîtier émettait sous une fiche créée automatiquement par l'ingestion (même
///    MAT, société par défaut, aucun véhicule). Ici, c'est la fiche OCCUPANTE qu'on
///    garde : le véhicule y est rattaché, et la fiche vide du véhicule est supprimée.
///
/// Dans les deux modes, une fiche n'est supprimée que si elle est strictement vide
/// dans toutes les tables qui référencent un boîtier. Si les deux fiches portent
/// des données, on refuse : la fusion d'historiques n'est pas prise en charge.
/// </summary>
public record ReplaceVehicleDeviceCommand(
    int VehicleId,
    string NewImei,
    string? NewSimNumber = null,
    string? NewMat = null,
    string? NewSimOperator = null,
    string? NewFuelSensorMode = null
) : IRequest<ReplaceVehicleDeviceResult>;

public record ReplaceVehicleDeviceResult(
    bool Success,
    string Message,
    int? DeviceId = null,
    string? PreviousImei = null,
    int? ReleasedDeviceId = null,
    string? Mode = null);

public class ReplaceVehicleDeviceCommandHandler
    : IRequestHandler<ReplaceVehicleDeviceCommand, ReplaceVehicleDeviceResult>
{
    public const string ModeRename = GpsDeviceReplacementPlan.ModeRename;
    public const string ModeAttach = GpsDeviceReplacementPlan.ModeAttach;

    /// <summary>Valeur par défaut de gps_devices.fuel_sensor_mode (entité, base et ingestion).</summary>
    private const string DefaultFuelSensorMode = "raw_255";

    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private readonly IGisDbContext _context;

    public ReplaceVehicleDeviceCommandHandler(IGisDbContext context) => _context = context;

    public async Task<ReplaceVehicleDeviceResult> Handle(
        ReplaceVehicleDeviceCommand request, CancellationToken ct)
    {
        var plan = await PlanAsync(request, ct);
        if (!plan.CanProceed)
            return new(false, plan.Refusal!);

        // Rien à supprimer : un simple SaveChanges suffit (renommage sans fiche à libérer).
        if (plan.DeletedDeviceIds.Count == 0)
            return await ExecuteAsync(plan, request, ct);

        // Des fiches vont être supprimées. En base, gps_positions est en ON DELETE
        // CASCADE : une position insérée par l'ingestion entre le contrôle « strictement
        // vide » et la suppression serait effacée sans trace. On verrouille donc les
        // fiches concernées (FOR UPDATE bloque les insertions qui les référencent), puis
        // on refait le plan sous verrou, dans la même transaction que les écritures.
        // Enveloppé dans la stratégie d'exécution : l'API est configurée avec
        // EnableRetryOnFailure, qui refuse une transaction ouverte hors stratégie.
        var strategy = _context.Database.CreateExecutionStrategy();
        var attempt = 0;
        return await strategy.ExecuteAsync(async () =>
        {
            if (attempt++ > 0)
            {
                // Nouvelle tentative après une erreur transitoire : repartir de la base.
                _context.ChangeTracker.Clear();

                // L'erreur a pu survenir PENDANT le COMMIT alors que la base l'avait validé
                // (réponse perdue). Rejouer replanifierait sur l'état final et répondrait
                // « IMEI X → X » en taisant la suppression : on reconnaît l'état atteint et on
                // rend le résultat de l'opération réellement effectuée.
                var applied = await AlreadyAppliedAsync(plan, request, ct);
                if (applied != null) return applied;
            }

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            if (_context.Database.ProviderName == NpgsqlProvider)
                await _context.Database.ExecuteSqlRawAsync(
                    "SELECT id FROM gps_devices WHERE id = ANY({0}) ORDER BY id FOR UPDATE",
                    new object[] { plan.TouchedDeviceIds.ToArray() }, ct);

            var locked = await PlanAsync(request, ct);
            if (!locked.CanProceed)
            {
                await tx.RollbackAsync(ct);
                return new ReplaceVehicleDeviceResult(false, locked.Refusal!);
            }
            if (!locked.TouchedDeviceIds.All(plan.TouchedDeviceIds.Contains))
            {
                await tx.RollbackAsync(ct);
                return new ReplaceVehicleDeviceResult(false,
                    "Les boîtiers concernés ont changé pendant l'opération : aucune modification n'a été faite. Réessayez.");
            }

            var result = await ExecuteAsync(locked, request, ct);
            await tx.CommitAsync(ct);
            return result;
        });
    }

    private Task<GpsDeviceReplacementPlan> PlanAsync(ReplaceVehicleDeviceCommand request, CancellationToken ct) =>
        GpsDeviceReplacementPlanner.PlanAsync(
            _context, request.VehicleId, request.NewImei, request.NewMat, request.NewSimNumber, ct);

    /// <summary>
    /// Le plan (qui supprime au moins une fiche) est-il déjà appliqué en base ? Si oui,
    /// renvoie le résultat de cette opération ; sinon null. Toutes les fiches à supprimer
    /// doivent avoir disparu, et :
    /// - rattachement : le véhicule pointe sur la fiche conservée ;
    /// - renommage : la fiche du véhicule porte le nouvel IMEI (et la SIM / le MAT demandés).
    /// Avant un COMMIT effectif, les fiches à supprimer existent encore (elles ont été vues
    /// par le plan) : une tentative échouée plus tôt n'est pas prise pour un succès.
    /// </summary>
    private async Task<ReplaceVehicleDeviceResult?> AlreadyAppliedAsync(
        GpsDeviceReplacementPlan plan, ReplaceVehicleDeviceCommand request, CancellationToken ct)
    {
        var deletedIds = plan.DeletedDeviceIds.ToArray();
        if (deletedIds.Length == 0) return null;
        if (await _context.GpsDevices.AsNoTracking().AnyAsync(d => deletedIds.Contains(d.Id), ct)) return null;

        var vehicleDeviceId = await _context.Vehicles.AsNoTracking()
            .Where(v => v.Id == plan.VehicleId)
            .Select(v => v.GpsDeviceId)
            .FirstOrDefaultAsync(ct);

        if (plan.Mode == GpsDeviceReplacementPlan.ModeAttach)
            return vehicleDeviceId == plan.TargetDeviceId ? AttachResult(plan) : null;

        if (vehicleDeviceId != plan.CurrentDeviceId) return null;
        var device = await _context.GpsDevices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == plan.CurrentDeviceId, ct);
        if (device == null) return null;
        bool Same(string? stored, string? wanted) =>
            string.IsNullOrWhiteSpace(wanted)
            || GpsDeviceUniquenessGuard.Normalize(stored) == GpsDeviceUniquenessGuard.Normalize(wanted);
        return Same(device.DeviceUid, plan.NewImei) && Same(device.SimNumber, request.NewSimNumber) && Same(device.Mat, request.NewMat)
            ? RenameResult(plan, plan.CurrentImei, device.DeviceUid, plan.ReleasedDeviceIds)
            : null;
    }

    private async Task<ReplaceVehicleDeviceResult> ExecuteAsync(
        GpsDeviceReplacementPlan plan, ReplaceVehicleDeviceCommand request, CancellationToken ct)
    {
        var vehicle = await _context.Vehicles.FirstAsync(v => v.Id == plan.VehicleId, ct);
        var current = await _context.GpsDevices.FirstAsync(d => d.Id == plan.CurrentDeviceId, ct);
        var released = new List<GpsDevice>();
        foreach (var id in plan.ReleasedDeviceIds)
            released.Add(await _context.GpsDevices.FirstAsync(d => d.Id == id, ct));

        return plan.Mode == GpsDeviceReplacementPlan.ModeAttach
            ? await AttachAsync(plan, request, vehicle, current, released, ct)
            : await RenameAsync(plan, request, current, released, ct);
    }

    private async Task<ReplaceVehicleDeviceResult> RenameAsync(
        GpsDeviceReplacementPlan plan, ReplaceVehicleDeviceCommand request,
        GpsDevice device, List<GpsDevice> released, CancellationToken ct)
    {
        var previousImei = device.DeviceUid;

        // Fiches strictement vides : on les retire pour libérer l'identifiant.
        foreach (var r in released)
            _context.GpsDevices.Remove(r);

        // La fiche renommée est celle du véhicule, affichée dans le formulaire : la saisie
        // gagne pour le MAT et la SIM. Seul l'IMEI d'un boîtier qui a déjà communiqué garde
        // son orthographe exacte quand la saisie n'en diffère que par la casse ou des espaces.
        device.DeviceUid = GpsDeviceUniquenessGuard.StoredValueFor(
            device.DeviceUid, plan.NewImei, keepDeviceSpelling: device.LastCommunication != null);
        if (!string.IsNullOrWhiteSpace(request.NewMat)) device.Mat = request.NewMat.Trim();
        if (!string.IsNullOrWhiteSpace(request.NewSimNumber)) device.SimNumber = request.NewSimNumber.Trim();
        if (!string.IsNullOrWhiteSpace(request.NewSimOperator)) device.SimOperator = request.NewSimOperator.Trim();
        if (!string.IsNullOrWhiteSpace(request.NewFuelSensorMode)) device.FuelSensorMode = request.NewFuelSensorMode.Trim();
        device.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return RenameResult(plan, previousImei, device.DeviceUid, released.Select(r => r.Id).ToList());
    }

    /// <summary>
    /// Résultat du renommage. Quand l'IMEI ne change pas (fiche de réserve vide qui portait
    /// la SIM ou le MAT saisis), le message ne parle pas de boîtier remplacé.
    /// </summary>
    private static ReplaceVehicleDeviceResult RenameResult(
        GpsDeviceReplacementPlan plan, string previousImei, string newImei, IReadOnlyList<int> releasedIds)
    {
        var imeiChanged = GpsDeviceUniquenessGuard.Normalize(previousImei) != GpsDeviceUniquenessGuard.Normalize(newImei);
        var ids = string.Join(", ", releasedIds.Select(id => "#" + id));
        var msg = imeiChanged
            ? $"Boîtier remplacé : IMEI {previousImei} → {newImei}."
              + (releasedIds.Count > 0 ? $" Fiche vide {ids} libérée." : "")
            : $"Identifiants mis à jour sur le boîtier #{plan.CurrentDeviceId} (IMEI inchangé : {newImei})."
              + (releasedIds.Count > 0 ? $" Fiche vide {ids} supprimée." : "");
        msg += " L'historique du véhicule est conservé.";
        int? releasedId = releasedIds.Count > 0 ? releasedIds[0] : null;
        return new(true, msg, plan.CurrentDeviceId, previousImei, releasedId, ModeRename);
    }

    /// <summary>
    /// Rattache le véhicule à la fiche qui porte le nouvel IMEI et l'historique, et
    /// supprime sa fiche actuelle (strictement vide, vérifié par le plan). Tout se fait
    /// en un seul SaveChanges.
    /// </summary>
    private async Task<ReplaceVehicleDeviceResult> AttachAsync(
        GpsDeviceReplacementPlan plan, ReplaceVehicleDeviceCommand request,
        Vehicle vehicle, GpsDevice current, List<GpsDevice> released, CancellationToken ct)
    {
        var target = await _context.GpsDevices.FirstAsync(d => d.Id == plan.TargetDeviceId!.Value, ct);
        var now = DateTime.UtcNow;

        // L'IMEI de la fiche conservée n'est PAS réécrit : c'est la valeur exacte sous
        // laquelle l'ingestion reconnaît le boîtier. Pour le MAT, les valeurs du formulaire
        // viennent de l'AUTRE fiche (celle du véhicule) : une saisie qui ne diffère que par la
        // casse ou des espaces garde l'orthographe de la fiche qui émet (l'ingestion compare
        // le MAT à l'identique et coupe la connexion sinon). La SIM n'est pas lue par
        // l'ingestion : la saisie gagne.
        target.CompanyId = vehicle.CompanyId;
        target.Status = "assigned";
        target.Mat = !string.IsNullOrWhiteSpace(request.NewMat)
            ? GpsDeviceUniquenessGuard.StoredValueFor(target.Mat, request.NewMat, keepDeviceSpelling: true)
            : GpsDeviceReplacementPlanner.FirstNonBlank(target.Mat, current.Mat);
        target.SimNumber = !string.IsNullOrWhiteSpace(request.NewSimNumber)
            ? request.NewSimNumber.Trim()
            : GpsDeviceReplacementPlanner.FirstNonBlank(target.SimNumber, current.SimNumber);
        target.SimOperator = GpsDeviceReplacementPlanner.FirstNonBlank(
            request.NewSimOperator, target.SimOperator, current.SimOperator);

        // Mode capteur carburant : la fiche créée par l'ingestion a toujours la valeur par
        // défaut, alors que la fiche du véhicule porte le réglage choisi par l'opérateur
        // (« liters » sur les trois véhicules du 14/09/2026). L'ingestion le relit à chaque
        // trame. La valeur demandée gagne : l'écran envoie le mode affiché dans le formulaire,
        // qui garde celui du véhicule quand l'appareil choisi n'a que la valeur par défaut
        // (fuelSensorModeForChosenDevice, vehicle-gps-save.helpers.ts). Sans valeur demandée
        // (autre appelant de l'API), le réglage explicite de la fiche supprimée est repris.
        if (!string.IsNullOrWhiteSpace(request.NewFuelSensorMode))
            target.FuelSensorMode = request.NewFuelSensorMode.Trim();
        else if ((string.IsNullOrWhiteSpace(target.FuelSensorMode) || target.FuelSensorMode == DefaultFuelSensorMode)
                 && !string.IsNullOrWhiteSpace(current.FuelSensorMode))
            target.FuelSensorMode = current.FuelSensorMode;

        if (string.IsNullOrWhiteSpace(target.Brand)) target.Brand = current.Brand;
        if (string.IsNullOrWhiteSpace(target.Model)) target.Model = current.Model;
        if (string.IsNullOrWhiteSpace(target.FirmwareVersion)) target.FirmwareVersion = current.FirmwareVersion;
        if (string.IsNullOrWhiteSpace(target.Label)) target.Label = current.Label;
        target.InstallationDate ??= current.InstallationDate;
        target.UpdatedAt = now;

        // Ordre EF sûr pour le lien unique véhicule↔boîtier : on déplace d'abord le
        // véhicule (clé ET navigation), on laisse EF recaler les navigations, puis
        // seulement on supprime l'ancienne fiche. Sans ce recalage, la suppression
        // pourrait remettre à NULL le lien du véhicule qu'EF croirait encore dépendant.
        vehicle.GpsDevice = target;
        vehicle.GpsDeviceId = target.Id;
        vehicle.HasGps = true;
        vehicle.UpdatedAt = now;
        _context.ChangeTracker.DetectChanges();

        foreach (var other in released)
            _context.GpsDevices.Remove(other);
        _context.GpsDevices.Remove(current);

        await _context.SaveChangesAsync(ct);

        return AttachResult(plan);
    }

    /// <summary>
    /// Résultat du rattachement, construit sur le plan exécuté (fiche conservée, fiche
    /// supprimée : identifiants inchangés par l'exécution). Le message ne dit pas que tout
    /// l'historique suit le véhicule : seules les positions le suivent.
    /// </summary>
    private static ReplaceVehicleDeviceResult AttachResult(GpsDeviceReplacementPlan plan)
    {
        var msg = $"Boîtier corrigé : « {plan.VehicleLabel} » est rattaché au boîtier #{plan.TargetDeviceId} (IMEI {plan.TargetImei}), " +
                  (plan.TargetPositions != null
                      ? $"dont les positions suivent désormais le véhicule ({plan.TargetPositions}). "
                      : "qui n'a encore aucune position. ") +
                  $"La fiche #{plan.CurrentDeviceId} (IMEI {plan.CurrentImei}, jamais utilisée) a été supprimée.";
        if (plan.TargetOtherData != null)
            msg += " " + plan.OtherDataNotice(future: false);
        if (plan.ReleasedDeviceIds.Count > 0)
            msg += $" Fiche(s) vide(s) libérée(s) : {string.Join(", ", plan.ReleasedDeviceIds.Select(id => "#" + id))}.";
        if (plan.TransferFromCompany != null)
            msg += $" Le boîtier a été transféré de la société « {plan.TransferFromCompany} » vers « {plan.TransferToCompany} ».";

        return new(true, msg, plan.TargetDeviceId, plan.CurrentImei, plan.CurrentDeviceId, ModeAttach);
    }
}
