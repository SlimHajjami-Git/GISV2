using System.Text.RegularExpressions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using Microsoft.EntityFrameworkCore;
using GisAPI.Domain.Common;

namespace GisAPI.Application.Features.Admin.GpsDevices;

/// <param name="Table">Nom de la table en base.</param>
/// <param name="Label">Libellé français, au pluriel optionnel (« position(s) GPS »).</param>
/// <param name="Rows">Lignes comptées (plafonnées, voir <paramref name="Capped"/>).</param>
/// <param name="Capped">Vrai si le comptage a atteint le plafond : « plus de … ».</param>
public record DeletionRowCountDto(string Table, string Label, int Rows, bool Capped);

/// <summary>Véhicule emporté avec le boîtier, et ce qu'il porte.</summary>
public record DeletedVehicleSummaryDto(
    int Id, string Name, string? Plate, int CompanyId, string? CompanyName,
    int AccidentFilesKept, int Repairs, int Costs, int FuelEntries, int Trips, int Tours, int Notifications);

/// <summary>
/// Ce que la suppression d'un boîtier emporte, ligne par ligne, et ce qu'elle refuse.
/// </summary>
/// <param name="ActiveRecently">Le boîtier a communiqué il y a moins de 48 h.</param>
/// <param name="AccidentFilesKept">Dossiers de sinistre CONSERVÉS, détachés du boîtier et du véhicule.</param>
/// <param name="Refusal">Motif de refus, ou null : la suppression n'aura pas lieu tant qu'il est posé.</param>
/// <param name="ConfirmationToken">Ce que l'opérateur doit taper : l'IMEI, ou « SUPPRIMER » quand l'identifiant n'est pas lisible.</param>
public record GpsDeviceDeletionImpactDto(
    int DeviceId, string Imei, string? Mat, int CompanyId, string? CompanyName,
    DateTime? LastCommunication, bool ActiveRecently,
    DeletedVehicleSummaryDto? Vehicle,
    IReadOnlyList<DeletionRowCountDto> Rows, long TotalRows, bool Capped,
    int AccidentFilesKept,
    IReadOnlyList<string> Warnings, string? Refusal, string ConfirmationToken);

/// <summary>
/// Inventaire de ce qui dépend d'un boîtier — directement (positions, alertes, arrêts,
/// relevés carburant, commandes, événements, remorquages, relevés batterie, événements
/// de zone, passages POI, notifications) et par son véhicule.
///
/// POURQUOI UNE LISTE EXPLICITE et pas seulement le catalogue : six colonnes qui
/// référencent un boîtier n'ont AUCUNE clé étrangère sur TN (geofence_events.device_id,
/// poi_visits.device_id, accident_events.gps_device_id et device_uid, frame_debug_log,
/// tow_events.device_uid) — un plan découvert par les clés les manquerait.
///
/// Les comptages sont plafonnés (<see cref="CountCap"/>) : la fenêtre de confirmation ne
/// doit pas attendre, et « plus de 100 000 positions » suffit pour décider.
/// </summary>
public static class GpsDeviceDeletionInventory
{
    public const int CountCap = 100_000;
    public const string ConfirmationPhrase = "SUPPRIMER";
    public const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
    public static readonly TimeSpan ActiveWindow = TimeSpan.FromHours(48);
    private static readonly Regex IdentifiantLisible = new("^[A-Za-z0-9]{4,20}$", RegexOptions.Compiled);

    /// <summary>
    /// L'IMEI quand il se tape sans ambiguïté ; sinon « SUPPRIMER » — 16 fiches de TN
    /// portent un identifiant imprononçable (« 860141076690214at+cclk="26/10/09 »).
    /// </summary>
    public static string ConfirmationTokenFor(string? deviceUid) =>
        deviceUid != null && IdentifiantLisible.IsMatch(deviceUid) ? deviceUid : ConfirmationPhrase;

    /// <summary>Comparaison normalisée : espaces ignorés, casse ignorée (règle du garde anti-doublons).</summary>
    public static bool ConfirmationMatches(string? typed, string token) =>
        GpsDeviceUniquenessGuard.Normalize(typed).Length > 0
        && GpsDeviceUniquenessGuard.Normalize(typed) == GpsDeviceUniquenessGuard.Normalize(token);

    public static async Task<GpsDeviceDeletionImpactDto?> BuildAsync(IGisDbContext context, int deviceId, CancellationToken ct)
    {
        var device = await context.GpsDevices.AsNoTracking().IgnoreQueryFilters()
            .Where(d => d.Id == deviceId)
            .Select(d => new { d.Id, d.DeviceUid, d.Mat, d.CompanyId, d.LastCommunication })
            .FirstOrDefaultAsync(ct);
        if (device == null) return null;

        var companyName = await context.Societes.AsNoTracking().IgnoreQueryFilters()
            .Where(s => s.Id == device.CompanyId).Select(s => s.Name).FirstOrDefaultAsync(ct);

        var vehicle = await context.Vehicles.AsNoTracking().IgnoreQueryFilters()
            .Where(v => v.GpsDeviceId == deviceId)
            .OrderBy(v => v.Id)
            .Select(v => new { v.Id, v.Name, v.Plate, v.CompanyId })
            .FirstOrDefaultAsync(ct);

        var rows = new List<DeletionRowCountDto>();
        var warnings = new List<string>();

        // ── Ce que porte le boîtier lui-même ──
        rows.Add(await CountAsync("gps_positions", "position(s) GPS",
            context.GpsPositions.IgnoreQueryFilters().Where(p => p.DeviceId == deviceId), ct));
        rows.Add(await CountAsync("gps_alerts", "alerte(s) GPS",
            context.GpsAlerts.IgnoreQueryFilters().Where(a => a.DeviceId == deviceId), ct));
        rows.Add(await CountAsync("vehicle_stops", "arrêt(s)",
            context.VehicleStops.IgnoreQueryFilters().Where(s => s.DeviceId == deviceId), ct));
        rows.Add(await CountAsync("fuel_records", "relevé(s) carburant du boîtier",
            context.FuelRecords.IgnoreQueryFilters().Where(f => f.DeviceId == deviceId), ct));
        rows.Add(await CountAsync("device_commands", "commande(s) boîtier",
            context.DeviceCommands.IgnoreQueryFilters().Where(c => c.DeviceId == deviceId), ct));
        rows.Add(await CountAsync("device_events", "événement(s) boîtier",
            context.DeviceEvents.IgnoreQueryFilters().Where(e => e.DeviceId == deviceId), ct));
        rows.Add(await CountAsync("tow_events", "remorquage(s)",
            context.TowEvents.IgnoreQueryFilters().Where(e => e.DeviceId == deviceId), ct));
        rows.Add(await CountAsync("battery_start_readings", "relevé(s) de tension au démarrage",
            context.BatteryStartReadings.IgnoreQueryFilters().Where(b => b.DeviceId == deviceId), ct));
        rows.Add(await CountAsync("geofence_events", "événement(s) de zone",
            context.GeofenceEvents.IgnoreQueryFilters().Where(g => g.DeviceId == deviceId), ct));
        rows.Add(await CountAsync("poi_visits", "passage(s) sur point d'intérêt",
            context.PoiVisits.IgnoreQueryFilters().Where(v => v.DeviceId == deviceId), ct));
        rows.Add(await CountAsync("notifications", "notification(s) du boîtier",
            context.Notifications.IgnoreQueryFilters().Where(n => n.ReferenceType == "device" && n.ReferenceId == deviceId), ct));
        var journal = await CountFrameDebugLogAsync(context, deviceId, device.DeviceUid, ct);
        rows.Add(journal > CountCap
            ? new DeletionRowCountDto("frame_debug_log", "ligne(s) du journal technique", CountCap, true)
            : new DeletionRowCountDto("frame_debug_log", "ligne(s) du journal technique", journal, false));

        var accidentsBoitier = await context.AccidentEvents.AsNoTracking().IgnoreQueryFilters()
            .CountAsync(a => a.GpsDeviceId == deviceId, ct);

        // ── Historique d'un AUTRE véhicule vivant (boîtier remplacé) : refus ──
        // Arrêts, relevés carburant, remorquages, événements de zone et passages POI portent un
        // vehicle_id figé à l'ingestion : un boîtier remplacé garde l'historique de son ancien
        // véhicule. Le supprimer l'amputerait — on refuse plutôt que d'effacer.
        var autresVehicules = await context.VehicleStops.AsNoTracking().IgnoreQueryFilters()
            .Where(s => s.DeviceId == deviceId && (vehicle == null || s.VehicleId != vehicle.Id))
            .Select(s => s.VehicleId)
            .Union(context.FuelRecords.AsNoTracking().IgnoreQueryFilters()
                .Where(f => f.DeviceId == deviceId && (vehicle == null || f.VehicleId != vehicle.Id))
                .Select(f => f.VehicleId))
            .Union(context.TowEvents.AsNoTracking().IgnoreQueryFilters()
                .Where(t => t.DeviceId == deviceId && (vehicle == null || t.VehicleId != vehicle.Id))
                .Select(t => t.VehicleId))
            .Union(context.GeofenceEvents.AsNoTracking().IgnoreQueryFilters()
                .Where(g => g.DeviceId == deviceId && (vehicle == null || g.VehicleId != vehicle.Id))
                .Select(g => g.VehicleId))
            .Union(context.PoiVisits.AsNoTracking().IgnoreQueryFilters()
                .Where(p => p.DeviceId == deviceId && (vehicle == null || p.VehicleId != vehicle.Id))
                .Select(p => p.VehicleId))
            .Distinct()
            .Take(5)
            .ToListAsync(ct);
        string? refusal = null;
        if (autresVehicules.Count > 0)
        {
            var vivant = await context.Vehicles.AsNoTracking().IgnoreQueryFilters()
                .Where(v => autresVehicules.Contains(v.Id))
                .OrderBy(v => v.Id)
                .Select(v => new { v.Id, v.Name, v.Plate, v.GpsDeviceId })
                .FirstOrDefaultAsync(ct);
            if (vivant != null)
            {
                var libelle = string.IsNullOrWhiteSpace(vivant.Plate) ? vivant.Name : vivant.Plate;
                var actuel = vivant.GpsDeviceId.HasValue ? $"boîtier actuel #{vivant.GpsDeviceId}" : "sans boîtier aujourd'hui";
                refusal = $"Ce boîtier porte l'historique d'un autre véhicule encore présent : {libelle} (#{vivant.Id}, {actuel}). " +
                          "Le supprimer effacerait des arrêts et des relevés carburant de ce véhicule. Traitez d'abord ce véhicule " +
                          "(supprimez-le, ou acceptez de perdre ce passé en le supprimant lui aussi).";
            }
        }

        // ── Le véhicule rattaché et ce qu'il porte ──
        DeletedVehicleSummaryDto? vehicule = null;
        var accidentsVehicule = 0;
        if (vehicle != null)
        {
            var societeVehicule = vehicle.CompanyId == device.CompanyId
                ? companyName
                : await context.Societes.AsNoTracking().IgnoreQueryFilters()
                    .Where(s => s.Id == vehicle.CompanyId).Select(s => s.Name).FirstOrDefaultAsync(ct);

            accidentsVehicule = await context.AccidentEvents.AsNoTracking().IgnoreQueryFilters()
                .CountAsync(a => a.VehicleId == vehicle.Id, ct);
            var repairs = await context.Repairs.AsNoTracking().IgnoreQueryFilters().CountAsync(r => r.VehicleId == vehicle.Id, ct);
            var costs = await context.VehicleCosts.AsNoTracking().IgnoreQueryFilters().CountAsync(c => c.VehicleId == vehicle.Id, ct);
            var fuelEntries = await context.FuelEntries.AsNoTracking().IgnoreQueryFilters().CountAsync(f => f.VehicleId == vehicle.Id, ct);
            var trips = await context.Trips.AsNoTracking().IgnoreQueryFilters().CountAsync(t => t.VehicleId == vehicle.Id, ct);
            var tours = await context.Tours.AsNoTracking().IgnoreQueryFilters().CountAsync(t => t.VehicleId == vehicle.Id, ct);
            var notifications = await context.Notifications.AsNoTracking().IgnoreQueryFilters()
                .CountAsync(n => n.ReferenceType == NotificationAudience.ReferenceVehicule && n.ReferenceId == vehicle.Id, ct);

            vehicule = new DeletedVehicleSummaryDto(vehicle.Id, vehicle.Name, vehicle.Plate, vehicle.CompanyId, societeVehicule,
                accidentsVehicule, repairs, costs, fuelEntries, trips, tours, notifications);

            rows.Add(new DeletionRowCountDto("vehicles", "véhicule", 1, false));
            rows.Add(new DeletionRowCountDto("fuel_entries", "plein(s) saisi(s) à la main", fuelEntries, false));
            rows.Add(new DeletionRowCountDto("trips", "trajet(s)", trips, false));
            rows.Add(new DeletionRowCountDto("tours", "tournée(s)", tours, false));
            rows.Add(new DeletionRowCountDto("vehicle_costs", "dépense(s)", costs, false));
            rows.Add(new DeletionRowCountDto("repairs", "réparation(s)", repairs, false));
            rows.Add(new DeletionRowCountDto("notifications_vehicle", "notification(s) du véhicule", notifications, false));

            var libelle = string.IsNullOrWhiteSpace(vehicle.Plate) ? vehicle.Name : $"{vehicle.Plate} ({vehicle.Name})";
            warnings.Add($"Le véhicule {libelle} et TOUTES ses données (entretiens, planning, échéancier, documents, " +
                         "affectations, réservations…) sont supprimés avec le boîtier.");
        }

        var accidentsConserves = accidentsBoitier + accidentsVehicule;
        if (accidentsConserves > 0)
            warnings.Add($"{accidentsConserves} dossier(s) de sinistre sont conservés, détachés du boîtier et du véhicule.");

        var activeRecently = device.LastCommunication.HasValue && DateTime.UtcNow - device.LastCommunication.Value < ActiveWindow;
        if (activeRecently)
            warnings.Add("Ce boîtier a communiqué il y a moins de 48 h : s'il est encore branché, il est mis en liste de blocage " +
                         "et ses trames sont ignorées tant qu'il n'est pas débloqué.");

        var capped = rows.Any(r => r.Capped);
        if (capped) warnings.Add($"Les comptages sont plafonnés à {FormatFr.N(CountCap, 0)} lignes par table.");

        var total = rows.Sum(r => (long)r.Rows);
        return new GpsDeviceDeletionImpactDto(
            device.Id, device.DeviceUid, device.Mat, device.CompanyId, companyName,
            device.LastCommunication, activeRecently,
            vehicule, rows, total, capped, accidentsConserves, warnings, refusal,
            ConfirmationTokenFor(device.DeviceUid));
    }

    private static async Task<DeletionRowCountDto> CountAsync<T>(string table, string label, IQueryable<T> query, CancellationToken ct) where T : class
    {
        var n = await query.AsNoTracking().Take(CountCap + 1).CountAsync(ct);
        return n > CountCap
            ? new DeletionRowCountDto(table, label, CountCap, true)
            : new DeletionRowCountDto(table, label, n, false);
    }

    /// <summary>frame_debug_log n'a pas d'entité EF : comptage en SQL brut, PostgreSQL seulement.</summary>
    private static async Task<int> CountFrameDebugLogAsync(IGisDbContext context, int deviceId, string deviceUid, CancellationToken ct)
    {
        if (context.Database.ProviderName != NpgsqlProvider) return 0;
        // Borné comme les autres comptages : la table n'est indexée que depuis la migration 057.
        var rows = await context.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM (SELECT 1 FROM frame_debug_log WHERE device_id = {0} OR device_uid = {1} LIMIT {2}) t",
                deviceId, deviceUid, CountCap + 1)
            .ToListAsync(ct);
        return rows.FirstOrDefault();
    }
}
