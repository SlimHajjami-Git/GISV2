using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Services;

/// <summary>
/// Ce que ferait un remplacement de boîtier (POST /api/admin/vehicles/{id}/replace-device),
/// calculé SANS rien modifier : mode choisi, fiches supprimées, ou raison du refus.
///
/// POURQUOI — constat du 14/09/2026 (HTZ 278, 255, 292) : l'écran proposait le
/// remplacement sur n'importe quel « Doublon refusé » et le message annonçait une
/// issue que le serveur ne tenait pas toujours (remplacement voué à l'échec rejoué
/// en boucle, rattachement promis là où le serveur renommait). Le même plan sert
/// désormais à la commande de remplacement ET au message de doublon : l'écran ne
/// propose la confirmation que si le plan aboutit, et le message décrit exactement
/// ce que la confirmation fera.
/// </summary>
public sealed class GpsDeviceReplacementPlan
{
    public const string ModeRename = "rename";
    public const string ModeAttach = "attach";

    /// <summary>Raison du refus ; null si le remplacement peut se faire.</summary>
    public string? Refusal { get; init; }
    public bool CanProceed => Refusal == null;
    public string Mode { get; init; } = ModeRename;

    public string NewImei { get; init; } = "";
    public int VehicleId { get; init; }
    public string VehicleLabel { get; init; } = "";
    public int VehicleCompanyId { get; init; }

    /// <summary>Fiche boîtier actuelle du véhicule.</summary>
    public int CurrentDeviceId { get; init; }
    public string CurrentImei { get; init; } = "";

    /// <summary>Rattachement : fiche conservée (celle qui porte le nouvel IMEI et l'historique).</summary>
    public int? TargetDeviceId { get; init; }
    public string? TargetImei { get; init; }
    public string? TargetHistory { get; init; }

    /// <summary>Rattachement : positions de la fiche conservée (« 352 position(s) »), null s'il n'y en a pas.</summary>
    public string? TargetPositions { get; init; }

    /// <summary>
    /// Rattachement : autres données de la fiche conservée (alertes, arrêts, relevés
    /// carburant…), null s'il n'y en a pas. Elles ne suivent PAS le véhicule : l'ingestion
    /// les a enregistrées avec le véhicule et la société de la fiche à l'époque (vehicle_id
    /// 0, société par défaut), et le rattachement ne les réaffecte pas.
    /// </summary>
    public string? TargetOtherData { get; init; }

    /// <summary>Rattachement entre deux sociétés : noms d'origine et de destination.</summary>
    public string? TransferFromCompany { get; init; }
    public string? TransferToCompany { get; init; }

    /// <summary>Autres fiches, strictement vides, supprimées pour libérer un identifiant.</summary>
    public IReadOnlyList<int> ReleasedDeviceIds { get; init; } = Array.Empty<int>();

    /// <summary>Toutes les fiches que l'exécution supprimera.</summary>
    public IReadOnlyList<int> DeletedDeviceIds => Mode == ModeAttach
        ? ReleasedDeviceIds.Append(CurrentDeviceId).ToList()
        : ReleasedDeviceIds;

    /// <summary>Toutes les fiches lues ou modifiées par l'exécution.</summary>
    public IReadOnlyList<int> TouchedDeviceIds
    {
        get
        {
            var ids = new List<int> { CurrentDeviceId };
            if (TargetDeviceId.HasValue) ids.Add(TargetDeviceId.Value);
            ids.AddRange(ReleasedDeviceIds);
            return ids.Distinct().OrderBy(i => i).ToList();
        }
    }

    /// <summary>Phrase qui dit, avant confirmation, ce que le remplacement fera.</summary>
    public string DescribeOutcome()
    {
        if (!CanProceed) return Refusal!;

        string Ids(IEnumerable<int> ids) => string.Join(", ", ids.Select(i => "#" + i));

        if (Mode == ModeAttach)
        {
            var s = $"Si vous confirmez le remplacement, « {VehicleLabel} » sera rattaché au boîtier #{TargetDeviceId} " +
                    $"(IMEI {TargetImei}), qui porte l'historique ({TargetHistory}), et sa fiche actuelle #{CurrentDeviceId} " +
                    $"(IMEI {CurrentImei}), strictement vide, sera supprimée.";
            if (TargetOtherData != null)
                s += " " + OtherDataNotice(future: true);
            if (ReleasedDeviceIds.Count > 0)
                s += $" Fiche(s) strictement vide(s) également supprimée(s) : {Ids(ReleasedDeviceIds)}.";
            if (TransferFromCompany != null)
                s += $" Le boîtier passera de la société « {TransferFromCompany} » à « {TransferToCompany} ».";
            return s;
        }

        var imeiChanged = GpsDeviceUniquenessGuard.Normalize(NewImei) != GpsDeviceUniquenessGuard.Normalize(CurrentImei);
        var r = imeiChanged
            ? $"Si vous confirmez le remplacement, le boîtier actuel #{CurrentDeviceId} prendra l'IMEI {NewImei} " +
              $"(au lieu de {CurrentImei}) et gardera tout son historique."
            : $"Si vous confirmez le remplacement, le boîtier actuel #{CurrentDeviceId} garde son IMEI et son historique.";
        if (ReleasedDeviceIds.Count > 0)
            r += $" Fiche(s) strictement vide(s) supprimée(s) pour libérer l'identifiant : {Ids(ReleasedDeviceIds)}.";
        return r;
    }

    /// <summary>
    /// Ce que le rattachement ne fait PAS : réaffecter au véhicule les données autres que
    /// les positions (contre-relecture du 14/09/2026). Les positions sont indexées par
    /// boîtier et suivent le lien véhicule→boîtier ; les arrêts, relevés carburant et
    /// alertes portent le véhicule et la société enregistrés par l'ingestion.
    /// </summary>
    public string OtherDataNotice(bool future) =>
        (future ? "Seules les positions suivront le véhicule : " : "Seules les positions suivent le véhicule : ") +
        $"les autres données déjà enregistrées sous ce boîtier ({TargetOtherData}) ne sont pas réaffectées " +
        "et gardent le véhicule et la société enregistrés à l'époque.";

    internal static GpsDeviceReplacementPlan Refuse(string message) => new() { Refusal = message };
}

/// <summary>
/// Calcule un <see cref="GpsDeviceReplacementPlan"/> en lecture seule (AsNoTracking) :
/// les valeurs lues sont celles de la base, pas les modifications en attente d'un
/// handler qui l'appelle en cours de route.
///
/// Deux modes, choisis selon la fiche qui porte les données :
/// 1. RENOMMAGE — la fiche DU VÉHICULE porte l'historique : on la renomme en place ;
///    les fiches qui occupaient l'identifiant sont supprimées si strictement vides.
/// 2. RATTACHEMENT — une fiche porte EXACTEMENT le nouvel IMEI et de l'historique,
///    et la fiche du véhicule est strictement vide (IMEI mal saisi, 14/09/2026) :
///    le véhicule est rattaché à cette fiche et la sienne est supprimée.
/// </summary>
public static class GpsDeviceReplacementPlanner
{
    /// <summary>
    /// Société où l'ingestion crée la fiche de tout IMEI inconnu
    /// (DEFAULT_COMPANY_ID dans gps-ingest-rust/src/db.rs). Un rattachement ne transfère
    /// un boîtier et son historique que depuis cette société ou la société du véhicule :
    /// prendre le boîtier d'un autre client doit rester une décision explicite.
    /// </summary>
    public const int IngestDefaultCompanyId = 1;

    /// <summary>
    /// Plafond des comptages : on veut savoir SI une fiche porte des données et en
    /// donner l'ordre de grandeur, pas compter des millions de positions.
    /// </summary>
    private const int CountCap = 100_000;

    public static async Task<GpsDeviceReplacementPlan> PlanAsync(
        IGisDbContext context, int vehicleId, string? newImei, string? newMat, string? newSim, CancellationToken ct)
    {
        var imei = (newImei ?? "").Trim();
        if (imei.Length == 0)
            return GpsDeviceReplacementPlan.Refuse("L'IMEI du nouveau boîtier est obligatoire.");

        var vehicle = await context.Vehicles
            .AsNoTracking()
            .Where(v => v.Id == vehicleId)
            .Select(v => new { v.Id, Label = v.Plate ?? v.Name, v.CompanyId, v.GpsDeviceId })
            .FirstOrDefaultAsync(ct);
        if (vehicle == null)
            return GpsDeviceReplacementPlan.Refuse("Véhicule introuvable.");
        if (!vehicle.GpsDeviceId.HasValue)
            return GpsDeviceReplacementPlan.Refuse(
                "Ce véhicule n'a aucun boîtier à remplacer. Utilisez « Ajouter un appareil ».");

        // gps_devices tient en quelques centaines de lignes : on compare en mémoire
        // pour appliquer la MÊME normalisation que le garde-fou (espaces, casse).
        var devices = await context.GpsDevices.AsNoTracking().ToListAsync(ct);
        var current = devices.FirstOrDefault(d => d.Id == vehicle.GpsDeviceId.Value);
        if (current == null)
            return GpsDeviceReplacementPlan.Refuse("Le boîtier actuel du véhicule est introuvable.");

        var targetMat = string.IsNullOrWhiteSpace(newMat) ? current.Mat : newMat.Trim();
        var targetSim = string.IsNullOrWhiteSpace(newSim) ? current.SimNumber : newSim.Trim();
        var conflicts = FindConflicting(devices, new[] { current.Id }, imei, targetMat, targetSim);

        var nImei = GpsDeviceUniquenessGuard.Normalize(imei);
        var holder = conflicts.FirstOrDefault(d => GpsDeviceUniquenessGuard.Normalize(d.DeviceUid) == nImei);
        if (holder != null)
        {
            var holderData = await InspectAsync(context, holder.Id, ignoreVehicleId: vehicle.Id, ct);
            if (holderData.HasHistory)
                return await PlanAttachAsync(context, devices, imei, newMat, newSim,
                    vehicle.Id, vehicle.Label, vehicle.CompanyId, current, holder, holderData, ct);
        }

        var released = new List<int>();
        foreach (var conflict in conflicts)
        {
            var data = await InspectAsync(context, conflict.Id, ignoreVehicleId: null, ct);
            if (!data.IsStrictlyEmpty)
                return GpsDeviceReplacementPlan.Refuse(
                    $"Remplacement impossible : le boîtier #{conflict.Id} porte déjà cet identifiant " +
                    $"et contient des données ({data.Describe()}). Traitez-le d'abord — un boîtier avec de " +
                    "l'historique ne doit pas être supprimé à l'aveugle.");
            released.Add(conflict.Id);
        }

        return new GpsDeviceReplacementPlan
        {
            Mode = GpsDeviceReplacementPlan.ModeRename,
            NewImei = imei,
            VehicleId = vehicle.Id,
            VehicleLabel = vehicle.Label,
            VehicleCompanyId = vehicle.CompanyId,
            CurrentDeviceId = current.Id,
            CurrentImei = current.DeviceUid,
            ReleasedDeviceIds = released
        };
    }

    private static async Task<GpsDeviceReplacementPlan> PlanAttachAsync(
        IGisDbContext context, List<GpsDevice> devices, string imei, string? newMat, string? newSim,
        int vehicleId, string vehicleLabel, int vehicleCompanyId,
        GpsDevice current, GpsDevice target, DeviceDataReport targetData, CancellationToken ct)
    {
        string? otherVehicle = null;
        if (targetData.LinkedVehicles > 0)
            otherVehicle = await context.Vehicles
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(v => v.GpsDeviceId == target.Id && v.Id != vehicleId)
                .Select(v => v.Plate ?? v.Name)
                .FirstOrDefaultAsync(ct) ?? "?";

        // D'abord la fiche du véhicule : si elle a de l'historique, on est dans un
        // remplacement ordinaire entre deux fiches pleines. Le dire tout de suite évite
        // d'envoyer l'opérateur détacher un autre véhicule pour se heurter ensuite à ce
        // même refus.
        var currentData = await InspectAsync(context, current.Id, ignoreVehicleId: vehicleId, ct);
        if (!currentData.IsStrictlyEmpty)
            return GpsDeviceReplacementPlan.Refuse(
                "Remplacement impossible : les deux fiches contiennent des données. " +
                $"Le boîtier actuel de « {vehicleLabel} » (#{current.Id}, IMEI {current.DeviceUid}) porte {currentData.Describe()} ; " +
                $"le boîtier #{target.Id} (IMEI {target.DeviceUid}) porte {targetData.DescribeHistory()}" +
                (otherVehicle != null ? $" et est rattaché au véhicule {otherVehicle}" : "") + ". " +
                "La fusion de deux historiques n'est pas prise en charge : aucune modification n'a été faite.");

        // La fiche qui émet appartient déjà à un autre véhicule : ce n'est pas une
        // erreur de saisie qu'on peut corriger ici.
        if (otherVehicle != null)
            return GpsDeviceReplacementPlan.Refuse(
                $"Rattachement impossible : le boîtier #{target.Id} (IMEI {target.DeviceUid}) est déjà rattaché " +
                $"au véhicule {otherVehicle}. Détachez-le d'abord de ce véhicule.");

        var companyIds = new[] { target.CompanyId, vehicleCompanyId };
        var companyNames = await context.Societes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => companyIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);
        string NameOf(int id) => companyNames.FirstOrDefault(n => n.Id == id)?.Name ?? $"#{id}";

        // Transfert d'un boîtier (et de son historique) depuis un autre client : refusé.
        // Seule la fiche créée par l'ingestion dans sa société par défaut est reprise.
        if (target.CompanyId != vehicleCompanyId && target.CompanyId != IngestDefaultCompanyId)
            return GpsDeviceReplacementPlan.Refuse(
                $"Rattachement refusé : le boîtier #{target.Id} (IMEI {target.DeviceUid}) appartient à la société " +
                $"« {NameOf(target.CompanyId)} », qui n'est pas celle du véhicule (« {NameOf(vehicleCompanyId)} »). " +
                "Transférer un boîtier et son historique d'un client à un autre doit rester une décision explicite : " +
                "réaffectez d'abord ce boîtier à la société du véhicule. Aucune modification n'a été faite.");

        // Autres fiches (ni celle du véhicule, ni celle conservée) qui porteraient les
        // mêmes identifiants finaux : supprimables seulement si strictement vides.
        var finalMat = FirstNonBlank(newMat, target.Mat, current.Mat);
        var finalSim = FirstNonBlank(newSim, target.SimNumber, current.SimNumber);
        var others = FindConflicting(devices, new[] { current.Id, target.Id }, target.DeviceUid, finalMat, finalSim);
        var released = new List<int>();
        foreach (var other in others)
        {
            var data = await InspectAsync(context, other.Id, ignoreVehicleId: null, ct);
            if (!data.IsStrictlyEmpty)
                return GpsDeviceReplacementPlan.Refuse(
                    $"Rattachement impossible : le boîtier #{other.Id} (IMEI {other.DeviceUid}) porte aussi l'un de ces " +
                    $"identifiants (MAT ou SIM) et contient des données ({data.Describe()}). Traitez-le d'abord — un boîtier " +
                    "avec de l'historique ne doit pas être supprimé à l'aveugle.");
            released.Add(other.Id);
        }

        var transfer = target.CompanyId != vehicleCompanyId;
        return new GpsDeviceReplacementPlan
        {
            Mode = GpsDeviceReplacementPlan.ModeAttach,
            NewImei = imei,
            VehicleId = vehicleId,
            VehicleLabel = vehicleLabel,
            VehicleCompanyId = vehicleCompanyId,
            CurrentDeviceId = current.Id,
            CurrentImei = current.DeviceUid,
            TargetDeviceId = target.Id,
            TargetImei = target.DeviceUid,
            TargetHistory = targetData.DescribeHistory(),
            TargetPositions = targetData.Positions > 0 ? $"{Format(targetData.Positions)} position(s)" : null,
            TargetOtherData = targetData.Others.Count > 0 ? string.Join(", ", targetData.Others) : null,
            TransferFromCompany = transfer ? NameOf(target.CompanyId) : null,
            TransferToCompany = transfer ? NameOf(vehicleCompanyId) : null,
            ReleasedDeviceIds = released
        };
    }

    internal static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    /// <summary>Fiches (hors <paramref name="excludeIds"/>) portant l'un des identifiants visés.</summary>
    private static List<GpsDevice> FindConflicting(
        IEnumerable<GpsDevice> devices, IReadOnlyCollection<int> excludeIds, string imei, string? mat, string? sim)
    {
        var nImei = GpsDeviceUniquenessGuard.Normalize(imei);
        var nMat = GpsDeviceUniquenessGuard.Normalize(mat);
        var nSim = GpsDeviceUniquenessGuard.Normalize(sim);

        return devices.Where(d => !excludeIds.Contains(d.Id) && (
                (nImei.Length > 0 && GpsDeviceUniquenessGuard.Normalize(d.DeviceUid) == nImei) ||
                (nMat.Length > 0 && GpsDeviceUniquenessGuard.Normalize(d.Mat) == nMat) ||
                (nSim.Length > 0 && GpsDeviceUniquenessGuard.Normalize(d.SimNumber) == nSim)))
            .OrderBy(d => d.Id)
            .ToList();
    }

    /// <summary>
    /// Inventaire de ce qui référence une fiche boîtier, utilisé par les DEUX modes
    /// avant toute suppression : on ne supprime jamais une fiche qui détient la
    /// moindre donnée. Couvre toutes les entités qui pointent vers gps_devices
    /// (en base, gps_positions est même en ON DELETE CASCADE : une suppression trop
    /// confiante effacerait l'historique sans bruit). Les filtres multi-société sont
    /// ignorés : une ligne d'une autre société compte aussi.
    ///
    /// Hors inventaire : frame_debug_log (journal technique écrit par l'ingestion,
    /// sans clé étrangère ni entité EF) — ses lignes survivent à la suppression.
    /// </summary>
    /// <param name="ignoreVehicleId">Véhicule dont le lien avec la fiche ne compte pas.</param>
    private static async Task<DeviceDataReport> InspectAsync(
        IGisDbContext context, int deviceId, int? ignoreVehicleId, CancellationToken ct)
    {
        var linkedVehicles = await CountAsync(context.Vehicles.IgnoreQueryFilters()
            .Where(v => v.GpsDeviceId == deviceId && (!ignoreVehicleId.HasValue || v.Id != ignoreVehicleId.Value)), ct);

        var positions = await CountAsync(context.GpsPositions.IgnoreQueryFilters().Where(p => p.DeviceId == deviceId), ct);

        var others = new List<string>();
        async Task Add<T>(IQueryable<T> query, string label)
        {
            var n = await CountAsync(query, ct);
            if (n > 0) others.Add($"{Format(n)} {label}");
        }

        await Add(context.GpsAlerts.IgnoreQueryFilters().Where(a => a.DeviceId == deviceId), "alerte(s)");
        await Add(context.DeviceCommands.IgnoreQueryFilters().Where(c => c.DeviceId == deviceId), "commande(s) boîtier");
        await Add(context.DeviceEvents.IgnoreQueryFilters().Where(e => e.DeviceId == deviceId), "événement(s) boîtier");
        await Add(context.TowEvents.IgnoreQueryFilters().Where(e => e.DeviceId == deviceId), "remorquage(s)");
        await Add(context.FuelRecords.IgnoreQueryFilters().Where(f => f.DeviceId == deviceId), "relevé(s) carburant");
        await Add(context.VehicleStops.IgnoreQueryFilters().Where(s => s.DeviceId == deviceId), "arrêt(s)");
        await Add(context.AccidentEvents.IgnoreQueryFilters().Where(a => a.GpsDeviceId == deviceId), "accident(s)");
        await Add(context.GeofenceEvents.IgnoreQueryFilters().Where(g => g.DeviceId == deviceId), "événement(s) de zone");
        await Add(context.PoiVisits.IgnoreQueryFilters().Where(v => v.DeviceId == deviceId), "passage(s) sur point d'intérêt");

        return new DeviceDataReport(linkedVehicles, positions, others);
    }

    private static async Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct) =>
        await query.Take(CountCap + 1).CountAsync(ct);

    private static string Format(int count) =>
        count > CountCap ? $"plus de {CountCap}" : count.ToString();

    /// <param name="Positions">Positions (plafonnées à CountCap + 1).</param>
    /// <param name="Others">Autres tables, déjà libellées (« 2 alerte(s) »).</param>
    private sealed record DeviceDataReport(int LinkedVehicles, int Positions, IReadOnlyList<string> Others)
    {
        /// <summary>Positions puis autres données, libellées.</summary>
        public IReadOnlyList<string> History =>
            (Positions > 0 ? new[] { $"{Format(Positions)} position(s)" } : Array.Empty<string>())
            .Concat(Others).ToList();

        public bool HasHistory => Positions > 0 || Others.Count > 0;
        public bool IsStrictlyEmpty => LinkedVehicles == 0 && !HasHistory;

        public string DescribeHistory() => HasHistory ? string.Join(", ", History) : "aucune donnée";

        public string Describe()
        {
            var parts = new List<string>();
            if (LinkedVehicles > 0) parts.Add($"{LinkedVehicles} véhicule(s) rattaché(s)");
            parts.AddRange(History);
            return parts.Count == 0 ? "aucune donnée" : string.Join(", ", parts);
        }
    }
}
