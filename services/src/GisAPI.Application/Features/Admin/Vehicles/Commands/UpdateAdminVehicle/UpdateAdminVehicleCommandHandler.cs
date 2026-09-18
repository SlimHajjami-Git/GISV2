using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Commands.UpdateAdminVehicle;

public class UpdateAdminVehicleCommandHandler : IRequestHandler<UpdateAdminVehicleCommand, UpdateAdminVehicleResult>
{
    private readonly IGisDbContext _context;

    public UpdateAdminVehicleCommandHandler(IGisDbContext context) => _context = context;

    public async Task<UpdateAdminVehicleResult> Handle(UpdateAdminVehicleCommand r, CancellationToken ct)
    {
        var vehicle = await _context.Vehicles
            .Include(v => v.Societe)
            .Include(v => v.GpsDevice)
            .Include(v => v.AssignedDriver)
            .FirstOrDefaultAsync(v => v.Id == r.Id, ct);

        if (vehicle == null)
            return new UpdateAdminVehicleResult(false, "not_found");

        // Société enregistrée en base, avant la modification (le plan de remplacement la relit).
        var originalCompanyId = vehicle.CompanyId;

        // Matricule unique dans la société (DEF-037), contrôlé avant toute modification.
        // Changement de société : le matricule, même inchangé, est contrôlé dans la
        // société d'arrivée. Sinon seul un matricule modifié l'est, pour qu'un doublon
        // déjà en base reste modifiable.
        var movesCompany = r.CompanyId.HasValue && r.CompanyId.Value != originalCompanyId;
        var plateClash = await VehicleWriteRules.FindPlateClashAsync(
            _context, r.CompanyId ?? originalCompanyId, r.Plate ?? vehicle.Plate, vehicle.Id,
            movesCompany ? null : vehicle.Plate, ct);
        if (plateClash != null)
            return new UpdateAdminVehicleResult(false, VehicleWriteRules.AdminPlateClashMessage(plateClash));

        if (!string.IsNullOrEmpty(r.Name)) vehicle.Name = r.Name;
        if (r.Type != null) vehicle.Type = r.Type;
        if (r.Brand != null) vehicle.Brand = r.Brand;
        if (r.Model != null) vehicle.Model = r.Model;
        if (r.Plate != null) vehicle.Plate = r.Plate;
        if (r.Year.HasValue) vehicle.Year = r.Year.Value;
        if (r.Color != null) vehicle.Color = r.Color;
        if (r.Status != null) vehicle.Status = r.Status;
        if (r.HasGps.HasValue) vehicle.HasGps = r.HasGps.Value;
        if (r.Mileage.HasValue) vehicle.Mileage = r.Mileage.Value;
        if (r.FuelType != null) vehicle.FuelType = r.FuelType;
        if (r.FuelTankCapacity.HasValue) vehicle.FuelTankCapacity = r.FuelTankCapacity.Value;
        if (r.CompanyId.HasValue) vehicle.CompanyId = r.CompanyId.Value;

        var targetCompanyId = r.CompanyId ?? vehicle.CompanyId;

        // When changing company, move the existing GPS device to the new company too
        if (r.CompanyId.HasValue && vehicle.GpsDevice != null)
            vehicle.GpsDevice.CompanyId = r.CompanyId.Value;

        // Idem pour l'échéancier d'acquisition : sans ce recalage, les échéances
        // resteraient dans le coût de l'ancienne société, et la synchronisation
        // les croirait absentes (l'unicité en base ignore la société).
        if (r.CompanyId.HasValue)
        {
            var movedPayments = await _context.AcquisitionPayments
                .IgnoreQueryFilters()
                .Where(p => p.VehicleId == vehicle.Id && p.CompanyId != r.CompanyId.Value)
                .ToListAsync(ct);
            foreach (var payment in movedPayments)
            {
                payment.CompanyId = r.CompanyId.Value;
                payment.UpdatedAt = DateTime.UtcNow;
            }
        }

        if (r.HasGps == false)
        {
            await GpsDeviceResolver.ReleaseAsync(_context, vehicle.GpsDeviceId);
            vehicle.GpsDeviceId = null;
            vehicle.HasGps = false;
        }
        else if (r.HasGps == true ||
                 r.GpsDeviceId.HasValue ||
                 !string.IsNullOrWhiteSpace(r.GpsImei) ||
                 !string.IsNullOrWhiteSpace(r.GpsMat) ||
                 !string.IsNullOrWhiteSpace(r.GpsFuelSensorMode))
        {
            GpsDevice? gpsDevice = null;

            // Boîtier du véhicule AVANT la modification : sert à reconnaître le cas où
            // le doublon détecté est ce boîtier-là (voir DescribeConflict).
            var currentDeviceId = vehicle.GpsDeviceId;

            // Use existing device if the sent GPS identifiers match the current device
            // (comparaison normalisée, comme le garde-fou : espaces et casse ignorés).
            var existingDevice = vehicle.GpsDevice;
            var isSameDevice = vehicle.GpsDeviceId.HasValue && existingDevice != null && (
                (r.GpsDeviceId.HasValue && r.GpsDeviceId.Value == existingDevice.Id) ||
                (!r.GpsDeviceId.HasValue &&
                    (string.IsNullOrWhiteSpace(r.GpsImei) ||
                        GpsDeviceUniquenessGuard.Normalize(r.GpsImei) == GpsDeviceUniquenessGuard.Normalize(existingDevice.DeviceUid)) &&
                    (string.IsNullOrWhiteSpace(r.GpsMat) ||
                        GpsDeviceUniquenessGuard.Normalize(r.GpsMat) == GpsDeviceUniquenessGuard.Normalize(existingDevice.Mat))));

            if (isSameDevice)
            {
                gpsDevice = existingDevice;
            }
            else if (!r.GpsDeviceId.HasValue &&
                string.IsNullOrWhiteSpace(r.GpsImei) &&
                string.IsNullOrWhiteSpace(r.GpsMat) &&
                vehicle.GpsDeviceId.HasValue)
            {
                gpsDevice = existingDevice;
            }
            else
            {
                if (r.GpsDeviceId.HasValue && currentDeviceId.HasValue && targetCompanyId == originalCompanyId)
                {
                    var foreignChoice = await DescribeIngestDefaultDeviceChoiceAsync(vehicle.Id, targetCompanyId, r, ct);
                    if (foreignChoice != null) return foreignChoice;
                }

                var (resolvedDevice, error) = await GpsDeviceResolver.ResolveAsync(
                    _context, targetCompanyId, r.GpsDeviceId, r.GpsImei, r.GpsMat);
                if (error != null) return new UpdateAdminVehicleResult(false, error);
                gpsDevice = resolvedDevice;
            }

            if (gpsDevice != null)
            {
                if (vehicle.GpsDeviceId.HasValue && vehicle.GpsDeviceId != gpsDevice.Id)
                    await GpsDeviceResolver.ReleaseAsync(_context, vehicle.GpsDeviceId);

                vehicle.GpsDeviceId = gpsDevice.Id;
                vehicle.HasGps = true;
                gpsDevice.Status = "assigned";
                gpsDevice.Vehicle = vehicle;

                // Identifiants (l'ingestion compare IMEI et MAT à l'identique) :
                // - IMEI d'un boîtier qui a déjà communiqué : c'est, par construction, la valeur
                //   exacte sous laquelle l'ingestion le retrouve ; une saisie qui n'en diffère que
                //   par la casse ou des espaces ne la réécrit pas ;
                // - MAT : la saisie de l'opérateur gagne sur la fiche affichée dans son formulaire
                //   (il doit pouvoir corriger une casse ou une espace). L'orthographe stockée
                //   n'est gardée que sur une AUTRE fiche retrouvée par son IMEI, qui a communiqué.
                var hasCommunicated = gpsDevice.LastCommunication != null;
                if (!string.IsNullOrWhiteSpace(r.GpsImei))
                    gpsDevice.DeviceUid = GpsDeviceUniquenessGuard.StoredValueFor(
                        gpsDevice.DeviceUid, r.GpsImei, keepDeviceSpelling: hasCommunicated);
                if (!string.IsNullOrWhiteSpace(r.GpsMat))
                    gpsDevice.Mat = GpsDeviceUniquenessGuard.StoredValueFor(
                        gpsDevice.Mat, r.GpsMat, keepDeviceSpelling: hasCommunicated && !isSameDevice);
                if (!string.IsNullOrWhiteSpace(r.GpsBrand)) gpsDevice.Brand = r.GpsBrand;
                if (!string.IsNullOrWhiteSpace(r.GpsModel)) gpsDevice.Model = r.GpsModel;
                if (!string.IsNullOrWhiteSpace(r.GpsFirmwareVersion)) gpsDevice.FirmwareVersion = r.GpsFirmwareVersion;
                if (!string.IsNullOrWhiteSpace(r.GpsFuelSensorMode)) gpsDevice.FuelSensorMode = r.GpsFuelSensorMode;
                // SIM : jamais lue par l'ingestion, la saisie gagne.
                if (!string.IsNullOrWhiteSpace(r.GpsSimNumber)) gpsDevice.SimNumber = r.GpsSimNumber.Trim();
                if (!string.IsNullOrWhiteSpace(r.GpsSimOperator)) gpsDevice.SimOperator = r.GpsSimOperator;
                if (r.GpsInstallationDate.HasValue) gpsDevice.InstallationDate = r.GpsInstallationDate;

                // Anti-doublons IMEI/MAT/SIM sur les valeurs FINALES du boîtier.
                // Rien n'est persisté avant SaveChanges : retourner ici annule tout.
                var conflict = await GpsDeviceUniquenessGuard.FindConflictDetailAsync(
                    _context, gpsDevice.Id, gpsDevice.DeviceUid, gpsDevice.Mat, gpsDevice.SimNumber, ct);
                if (conflict != null)
                {
                    // Ce que ferait le remplacement proposé par l'écran, avec les valeurs que
                    // l'écran lui enverra (IMEI, MAT et SIM saisis). Lecture seule.
                    GpsDeviceReplacementPlan? plan = null;
                    if (currentDeviceId.HasValue && !string.IsNullOrWhiteSpace(r.GpsImei))
                        plan = await GpsDeviceReplacementPlanner.PlanAsync(
                            _context, vehicle.Id, r.GpsImei, r.GpsMat, r.GpsSimNumber, ct);

                    var (message, replaceSuggested) = DescribeConflict(conflict, currentDeviceId, gpsDevice.DeviceUid, plan);
                    return new UpdateAdminVehicleResult(false, message, ReplaceSuggested: replaceSuggested);
                }
            }
        }

        vehicle.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return new UpdateAdminVehicleResult(true, Vehicle: GpsDeviceResolver.MapToDto(vehicle));
    }

    /// <summary>
    /// Message de doublon qui dit la vérité sur la situation, et s'il faut proposer le
    /// remplacement de boîtier. Le préfixe « Doublon refusé » est conservé.
    ///
    /// POURQUOI — constat du 14/09/2026 (HTZ 278, 255, 292) : la fiche boîtier du
    /// véhicule avait été créée avec un IMEI mal recopié, et le vrai boîtier émettait
    /// sous une autre fiche portant le même MAT. Le message générique accusait tantôt
    /// un « boîtier non affecté », tantôt le véhicule lui-même (« véhicule HTZ 278 »
    /// alors qu'on éditait HTZ 278), et l'écran proposait un remplacement voué à
    /// l'échec. Désormais le remplacement n'est proposé que si le plan aboutit, et la
    /// phrase ajoutée décrit ce qu'il fera réellement (renommage ou rattachement).
    /// </summary>
    private static (string Message, bool ReplaceSuggested) DescribeConflict(
        GpsDeviceConflict conflict, int? currentDeviceId, string? requestedImei, GpsDeviceReplacementPlan? plan)
    {
        // Le doublon est porté par le boîtier ACTUEL de ce véhicule : l'opérateur est
        // en train de lui attribuer une autre fiche (IMEI corrigé, boîtier remplacé).
        var ownDevice = currentDeviceId.HasValue && conflict.DeviceId == currentDeviceId.Value;
        var message = ownDevice
            ? $"Doublon refusé : {conflict.IdentifierLabel} « {conflict.Value} » est aussi porté par le boîtier actuel " +
              $"de ce véhicule (#{conflict.DeviceId}, IMEI {conflict.DeviceImei})."
            : conflict.Message;

        // MAT ou SIM partagé avec un boîtier NON AFFECTÉ d'IMEI différent, alors que
        // l'IMEI du véhicule a la forme d'un IMEI mais une clé de Luhn fausse et que
        // celui de l'autre boîtier est valide : très probablement une faute de recopie
        // (fiche créée par l'ingestion, sans véhicule). Le contrôle de Luhn ne bloque
        // jamais la saisie ; il empêche seulement de proposer un remplacement qui
        // garderait l'IMEI faux en supprimant la fiche du vrai boîtier.
        if (!ownDevice
            && conflict.Identifier != GpsDeviceConflict.Imei
            && conflict.VehicleId == null
            && !string.IsNullOrWhiteSpace(requestedImei)
            && GpsDeviceUniquenessGuard.Normalize(conflict.DeviceImei) != GpsDeviceUniquenessGuard.Normalize(requestedImei)
            && IsFifteenDigits(requestedImei)
            && !GpsDeviceUniquenessGuard.IsValidImei(requestedImei)
            && GpsDeviceUniquenessGuard.IsValidImei(conflict.DeviceImei))
        {
            var same = conflict.Identifier == GpsDeviceConflict.Mat ? "le même MAT" : "le même numéro SIM";
            message += $" L'IMEI de ce véhicule ({requestedImei.Trim()}) semble mal saisi (chiffre de contrôle invalide) ; " +
                       $"le boîtier #{conflict.DeviceId} porte l'IMEI {conflict.DeviceImei} avec {same}. " +
                       "Pour rattacher le vrai boîtier : dans la section « Appareil GPS » du formulaire, choisissez " +
                       "« Ajouter un nouvel appareil » (les champs restent préremplis), remplacez l'IMEI par " +
                       $"{conflict.DeviceImei}, enregistrez puis confirmez le remplacement.";
            return (message, false);
        }

        if (plan == null) return (message, false);
        if (plan.CanProceed) return ($"{message} {plan.DescribeOutcome()}", true);

        // Doublon sur le boîtier actuel sans remplacement possible : dire pourquoi.
        return (ownDevice ? $"{message} {plan.Refusal}" : message, false);
    }

    /// <summary>
    /// Appareil choisi par son id dans la liste « appareil existant », appartenant à la
    /// société où l'ingestion crée les boîtiers inconnus, pour un véhicule d'une autre
    /// société qui a déjà un boîtier. Le Resolver refuse (« autre société ») sans issue ;
    /// on calcule ici ce que ferait le remplacement de boîtier avec cet IMEI, pour le
    /// proposer s'il aboutit. Renvoie null quand ce cas ne s'applique pas (le Resolver
    /// donne alors sa réponse habituelle). Lecture seule.
    ///
    /// POURQUOI — constat du 14/09/2026 (HTZ 278, 255, 292) : le vrai boîtier émettait
    /// sous une fiche créée par l'ingestion en société 1 et apparaissait dans la liste.
    /// Le désigner par son IMEI pour contourner le refus passait par le chemin IMEI du
    /// Resolver, qui transfère la fiche de société et détache sans rien dire le véhicule
    /// qui la portait (contre-relecture du 14/09/2026). Ici, rien n'est transféré ni
    /// détaché : seul le plan, qui refuse une fiche rattachée ailleurs ou d'un autre
    /// client, décide si la confirmation est proposée.
    /// </summary>
    private async Task<UpdateAdminVehicleResult?> DescribeIngestDefaultDeviceChoiceAsync(
        int vehicleId, int vehicleCompanyId, UpdateAdminVehicleCommand r, CancellationToken ct)
    {
        var chosen = await _context.GpsDevices
            .AsNoTracking()
            .Where(d => d.Id == r.GpsDeviceId!.Value)
            .Select(d => new { d.CompanyId, d.DeviceUid })
            .FirstOrDefaultAsync(ct);
        if (chosen == null
            || chosen.CompanyId == vehicleCompanyId
            || chosen.CompanyId != GpsDeviceReplacementPlanner.IngestDefaultCompanyId)
            return null;

        const string foreign = "Cet appareil GPS appartient à une autre société.";
        var plan = await GpsDeviceReplacementPlanner.PlanAsync(
            _context, vehicleId, chosen.DeviceUid, r.GpsMat, r.GpsSimNumber, ct);
        return plan.CanProceed
            ? new UpdateAdminVehicleResult(false, $"{foreign} {plan.DescribeOutcome()}", ReplaceSuggested: true)
            : new UpdateAdminVehicleResult(false, $"{foreign} {plan.Refusal}");
    }

    private static bool IsFifteenDigits(string value)
    {
        var n = GpsDeviceUniquenessGuard.Normalize(value);
        return n.Length == 15 && n.All(char.IsAsciiDigit);
    }
}
