using GisAPI.Application.Common.Helpers;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Gps.Commands.BroadcastPosition;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Admin.GpsDevices;

/// <summary>
/// Suppression DÉFINITIVE d'un boîtier et de tout ce qui en dépend — y compris le véhicule
/// rattaché et toutes ses données (demande de Slim du 09/10/2026). Réservée à l'administration
/// de la plateforme ; l'opérateur confirme en retapant l'IMEI.
/// </summary>
/// <param name="Confirmation">L'IMEI retapé (ou « SUPPRIMER » si l'identifiant n'est pas lisible).</param>
public record DeleteGpsDeviceCommand(int DeviceId, string? Confirmation) : IRequest<DeleteGpsDeviceResult>;

public record DeleteGpsDeviceResult(
    bool Success, string Message, bool NotFound = false,
    IReadOnlyList<DeletionRowCountDto>? Deleted = null, long TotalRows = 0,
    bool VehicleDeleted = false, long DurationMs = 0, string? Imei = null);

/// <summary>
/// Déroulé, dans cet ordre, et pourquoi :
/// <list type="number">
///   <item>inventaire + confirmation : rien n'est touché tant que l'IMEI retapé ne correspond
///     pas, ou que la fiche porte l'historique d'un autre véhicule vivant (refus) ;</item>
///   <item>liste de blocage AVANT tout : l'ingestion recrée une fiche pour tout IMEI inconnu
///     qui émet (société 1) — sans ce verrou, le boîtier revenait tout seul ;</item>
///   <item>les grosses tables par LOTS (positions, alertes, arrêts, relevés carburant : jusqu'à
///     784 000 positions par boîtier sur TN), chaque lot dans sa propre transaction courte,
///     pour ne jamais tenir un verrou long ni dépasser le délai de commande ;</item>
///   <item>le véhicule rattaché : pleins saisis à la main et notifications (que la cascade
///     standard conserve, mais que Slim veut voir partir), tournées, puis la cascade de
///     <see cref="VehicleDeletionHelper"/> (dossiers de sinistre conservés, détachés) ;</item>
///   <item>une transaction finale courte, fiche verrouillée (FOR UPDATE bloque les insertions
///     qui la référencent) : petites tables, sinistres détachés du boîtier, retardataires
///     arrivés pendant les lots, puis la fiche elle-même ;</item>
///   <item>une ligne d'audit explicite avec l'identité de la fiche et les comptages — l'audit
///     automatique ne garde que l'id.</item>
/// </list>
/// Chaque étape est idempotente : une coupure se rejoue sans dégât.
/// </summary>
public class DeleteGpsDeviceCommandHandler : IRequestHandler<DeleteGpsDeviceCommand, DeleteGpsDeviceResult>
{
    private const string NpgsqlProvider = GpsDeviceDeletionInventory.NpgsqlProvider;
    private const int CommandTimeoutSeconds = 120;

    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenant;
    private readonly ILogger<DeleteGpsDeviceCommandHandler> _logger;

    /// <summary>Taille des lots sur les grosses tables ; réduite par les tests pour prouver la boucle.</summary>
    public int BatchSize { get; set; } = 50_000;

    public DeleteGpsDeviceCommandHandler(IGisDbContext context, ICurrentTenantService tenant, ILogger<DeleteGpsDeviceCommandHandler> logger)
    {
        _context = context;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<DeleteGpsDeviceResult> Handle(DeleteGpsDeviceCommand r, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        _context.Database.SetCommandTimeout(CommandTimeoutSeconds);

        var impact = await GpsDeviceDeletionInventory.BuildAsync(_context, r.DeviceId, ct);
        if (impact == null)
            return new DeleteGpsDeviceResult(false, "Fiche boîtier introuvable.", NotFound: true);
        if (impact.Refusal != null)
            return new DeleteGpsDeviceResult(false, impact.Refusal, Imei: impact.Imei);
        if (!GpsDeviceDeletionInventory.ConfirmationMatches(r.Confirmation, impact.ConfirmationToken))
            return new DeleteGpsDeviceResult(false,
                $"Confirmation incorrecte : tapez « {impact.ConfirmationToken} » pour supprimer ce boîtier. Rien n'a été modifié.",
                Imei: impact.Imei);

        // Passé ce point, un client qui ferme l'onglet (F5, Précédent, délai du proxy) ne doit
        // pas laisser la suppression à mi-chemin — IMEI bloqué, lots partiels, fiche encore là,
        // aucun audit. La phase destructrice ignore donc l'annulation de la requête : chaque
        // étape est idempotente, mais c'est au serveur de finir ce que l'opérateur a confirmé.
        ct = CancellationToken.None;

        var deviceId = impact.DeviceId;
        var imei = impact.Imei;
        var pg = _context.Database.ProviderName == NpgsqlProvider;

        // 2. Liste de blocage, avant la première suppression.
        await BloquerAsync(impact, ct);

        // 3. Grosses tables, par lots.
        var deleted = new List<DeletionRowCountDto>
        {
            await DeleteInBatchesAsync<GpsPosition>("position(s) GPS", nameof(GpsPosition.DeviceId), nameof(GpsPosition.RecordedAt), deviceId, ct),
            await DeleteInBatchesAsync<GpsAlert>("alerte(s) GPS", nameof(GpsAlert.DeviceId), nameof(GpsAlert.Id), deviceId, ct),
            await DeleteInBatchesAsync<VehicleStop>("arrêt(s)", nameof(VehicleStop.DeviceId), nameof(VehicleStop.Id), deviceId, ct),
            await DeleteInBatchesAsync<FuelRecord>("relevé(s) carburant du boîtier", nameof(FuelRecord.DeviceId), nameof(FuelRecord.Id), deviceId, ct),
        };

        // 4. Le véhicule rattaché et ce que la cascade standard conserverait.
        var vehicleDeleted = false;
        if (impact.Vehicle != null)
        {
            var vid = impact.Vehicle.Id;
            deleted.Add(new DeletionRowCountDto("fuel_entries", "plein(s) saisi(s) à la main",
                await _context.FuelEntries.IgnoreQueryFilters().Where(f => f.VehicleId == vid).ExecuteDeleteAsync(ct), false));
            deleted.Add(new DeletionRowCountDto("notifications_vehicle", "notification(s) du véhicule",
                await _context.Notifications.IgnoreQueryFilters()
                    .Where(n => n.ReferenceType == NotificationAudience.ReferenceVehicule && n.ReferenceId == vid)
                    .ExecuteDeleteAsync(ct), false));
            var tourIds = await _context.Tours.IgnoreQueryFilters().Where(t => t.VehicleId == vid).Select(t => t.Id).ToListAsync(ct);
            if (tourIds.Count > 0)
                await _context.TourWaypoints.IgnoreQueryFilters().Where(w => tourIds.Contains(w.TourId)).ExecuteDeleteAsync(ct);
            deleted.Add(new DeletionRowCountDto("tours", "tournée(s)",
                await _context.Tours.IgnoreQueryFilters().Where(t => t.VehicleId == vid).ExecuteDeleteAsync(ct), false));

            await VehicleDeletionHelper.CascadeDeleteAsync(_context, vid, ct);
            deleted.Add(new DeletionRowCountDto("vehicles", "véhicule", 1, false));
            vehicleDeleted = true;
        }

        // 4 bis. Journal technique (sans clé étrangère ni entité EF) : hors transaction, et en
        // gardant les traces « blocked_deleted » que l'ingestion vient peut-être d'écrire — ce
        // sont les seules qui disent, après coup, qu'un boîtier supprimé émet encore.
        if (pg)
            deleted.Add(new DeletionRowCountDto("frame_debug_log", "ligne(s) du journal technique",
                await _context.Database.ExecuteSqlRawAsync(
                    "DELETE FROM frame_debug_log WHERE (device_id = {0} OR device_uid = {1}) AND reason IS DISTINCT FROM 'blocked_deleted'",
                    new object[] { deviceId, imei }, ct), false));

        // 5. Transaction finale courte, fiche verrouillée.
        var strategy = _context.Database.CreateExecutionStrategy();
        var finales = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            if (pg)
                await _context.Database.ExecuteSqlRawAsync(
                    "SELECT id FROM gps_devices WHERE id = {0} FOR UPDATE", new object[] { deviceId }, ct);

            var rows = new List<DeletionRowCountDto>
            {
                new("device_commands", "commande(s) boîtier",
                    await _context.DeviceCommands.IgnoreQueryFilters().Where(c => c.DeviceId == deviceId).ExecuteDeleteAsync(ct), false),
                new("device_events", "événement(s) boîtier",
                    await _context.DeviceEvents.IgnoreQueryFilters().Where(e => e.DeviceId == deviceId).ExecuteDeleteAsync(ct), false),
                new("tow_events", "remorquage(s)",
                    await _context.TowEvents.IgnoreQueryFilters().Where(e => e.DeviceId == deviceId).ExecuteDeleteAsync(ct), false),
                new("battery_start_readings", "relevé(s) de tension au démarrage",
                    await _context.BatteryStartReadings.IgnoreQueryFilters().Where(b => b.DeviceId == deviceId).ExecuteDeleteAsync(ct), false),
                new("geofence_events", "événement(s) de zone",
                    await _context.GeofenceEvents.IgnoreQueryFilters().Where(g => g.DeviceId == deviceId).ExecuteDeleteAsync(ct), false),
                new("poi_visits", "passage(s) sur point d'intérêt",
                    await _context.PoiVisits.IgnoreQueryFilters().Where(v => v.DeviceId == deviceId).ExecuteDeleteAsync(ct), false),
                new("notifications", "notification(s) du boîtier",
                    await _context.Notifications.IgnoreQueryFilters().Where(n => n.ReferenceType == "device" && n.ReferenceId == deviceId).ExecuteDeleteAsync(ct), false),
            };

            // Dossiers de sinistre : conservés, détachés du boîtier (l'IMEI reste en texte dans device_uid).
            await _context.AccidentEvents.IgnoreQueryFilters().Where(a => a.GpsDeviceId == deviceId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.GpsDeviceId, (int?)null), ct);

            // Retardataires arrivés pendant les lots (quelques lignes au plus).
            rows.Add(new DeletionRowCountDto("gps_positions", "position(s) GPS (retardataires)",
                await _context.GpsPositions.IgnoreQueryFilters().Where(p => p.DeviceId == deviceId).ExecuteDeleteAsync(ct), false));
            rows.Add(new DeletionRowCountDto("gps_alerts", "alerte(s) GPS (retardataires)",
                await _context.GpsAlerts.IgnoreQueryFilters().Where(a => a.DeviceId == deviceId).ExecuteDeleteAsync(ct), false));
            rows.Add(new DeletionRowCountDto("vehicle_stops", "arrêt(s) (retardataires)",
                await _context.VehicleStops.IgnoreQueryFilters().Where(s => s.DeviceId == deviceId).ExecuteDeleteAsync(ct), false));
            rows.Add(new DeletionRowCountDto("fuel_records", "relevé(s) carburant (retardataires)",
                await _context.FuelRecords.IgnoreQueryFilters().Where(f => f.DeviceId == deviceId).ExecuteDeleteAsync(ct), false));

            await _context.GpsDevices.IgnoreQueryFilters().Where(d => d.Id == deviceId).ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);
            return rows;
        });

        // Les retardataires s'ajoutent aux lots de la même table ; les lignes à zéro n'encombrent pas le résultat.
        var fusion = deleted.Concat(finales)
            .GroupBy(d => d.Table)
            .Select(g => new DeletionRowCountDto(g.Key, g.First().Label, g.Sum(d => d.Rows), false))
            .Where(d => d.Rows > 0 || d.Table == "vehicles")
            .ToList();
        var total = fusion.Sum(d => (long)d.Rows);
        var duration = (long)(DateTime.UtcNow - started).TotalMilliseconds;

        // 6. Trace explicite : l'audit automatique ne garde que « DELETE /api/… id ».
        var vehicule = impact.Vehicle == null ? "aucun véhicule" : $"véhicule {impact.Vehicle.Plate ?? impact.Vehicle.Name} (#{impact.Vehicle.Id})";
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _tenant.UserId,
            CompanyId = impact.CompanyId,
            Action = "gps_device_deleted",
            EntityType = "gps_devices",
            EntityId = deviceId,
            EntityName = imei,
            Description = $"Suppression définitive du boîtier #{deviceId} (IMEI {imei}, MAT {impact.Mat ?? "—"}, société #{impact.CompanyId}), {vehicule} : " +
                          $"{total} lignes dans {fusion.Count} tables, {impact.AccidentFilesKept} dossier(s) de sinistre conservé(s), {duration} ms.",
            OldValues = new Dictionary<string, object>
            {
                ["imei"] = imei, ["mat"] = impact.Mat ?? "", ["companyId"] = impact.CompanyId,
                ["lastCommunication"] = impact.LastCommunication?.ToString("u") ?? "",
                ["vehicle"] = vehicule,
            },
            NewValues = fusion.ToDictionary(d => d.Table, d => (object)d.Rows),
            Timestamp = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync(ct);

        // Le diffuseur temps réel garde une fiche par IMEI en mémoire (10 min) : sans cela, un
        // boîtier supprimé encore branché serait diffusé à son ancienne société jusqu'à expiration.
        BroadcastPositionCommandHandler.InvalidateDevice(imei);

        _logger.LogWarning(
            "Boîtier #{DeviceId} (IMEI {Imei}, MAT {Mat}, société {CompanyId}) supprimé par l'utilisateur {UserId} : {Rows} lignes dans {Tables} tables, {Vehicle}, {Ms} ms",
            deviceId, imei, impact.Mat, impact.CompanyId, _tenant.UserId, total, fusion.Count, vehicule, duration);

        return new DeleteGpsDeviceResult(true,
            $"Boîtier IMEI {imei} supprimé : {total} lignes dans {fusion.Count} tables" + (vehicleDeleted ? ", véhicule compris." : "."),
            Deleted: fusion, TotalRows: total, VehicleDeleted: vehicleDeleted, DurationMs: duration, Imei: imei);
    }

    private async Task BloquerAsync(GpsDeviceDeletionImpactDto impact, CancellationToken ct)
    {
        var existing = await _context.GpsDeviceBlocklist.FirstOrDefaultAsync(b => b.DeviceUid == impact.Imei, ct);
        if (existing == null)
        {
            _context.GpsDeviceBlocklist.Add(new GpsDeviceBlocklistEntry
            {
                DeviceUid = impact.Imei,
                BlockedAt = DateTime.UtcNow,
                BlockedByUserId = _tenant.UserId,
                DeviceIdBefore = impact.DeviceId,
                Mat = impact.Mat,
                CompanyIdBefore = impact.CompanyId,
                Reason = impact.Vehicle == null
                    ? "Suppression du boîtier depuis l'administration"
                    : $"Suppression du boîtier et du véhicule {impact.Vehicle.Plate ?? impact.Vehicle.Name} depuis l'administration",
            });
        }
        else
        {
            existing.BlockedAt = DateTime.UtcNow;
            existing.BlockedByUserId = _tenant.UserId;
            existing.DeviceIdBefore = impact.DeviceId;
            existing.Mat = impact.Mat;
            existing.CompanyIdBefore = impact.CompanyId;
        }
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Supprime par lots les lignes d'une table qui référencent le boîtier, chaque lot
    /// dans sa propre transaction implicite (rejouable). Noms de table et de colonnes lus
    /// dans le modèle EF : le même code tourne sur PostgreSQL (ctid) et SQLite (rowid).
    /// </summary>
    private async Task<DeletionRowCountDto> DeleteInBatchesAsync<TEntity>(
        string label, string deviceIdProperty, string orderProperty, int deviceId, CancellationToken ct) where TEntity : class
    {
        var entity = _context.Model.FindEntityType(typeof(TEntity))
                     ?? throw new InvalidOperationException($"Entité {typeof(TEntity).Name} absente du modèle.");
        var table = entity.GetTableName() ?? throw new InvalidOperationException($"Table de {typeof(TEntity).Name} inconnue.");
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        var column = entity.FindProperty(deviceIdProperty)!.GetColumnName(store)!;
        var order = entity.FindProperty(orderProperty)!.GetColumnName(store)!;
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

        var sql = _context.Database.ProviderName == NpgsqlProvider
            // ctid = ANY(tableau) : Tid Scan garanti, quel que soit l'estimateur (un IN (sous-requête)
            // peut se planifier en jointure de hachage avec parcours complet de la table).
            ? $"DELETE FROM {Q(table)} WHERE ctid = ANY (ARRAY(SELECT ctid FROM {Q(table)} WHERE {Q(column)} = {{0}} ORDER BY {Q(order)} LIMIT {{1}}))"
            : $"DELETE FROM {Q(table)} WHERE rowid IN (SELECT rowid FROM {Q(table)} WHERE {Q(column)} = {{0}} ORDER BY {Q(order)} LIMIT {{1}})";

        var total = 0;
        int n;
        do
        {
            n = await _context.Database.ExecuteSqlRawAsync(sql, new object[] { deviceId, BatchSize }, ct);
            total += n;
        } while (n >= BatchSize);

        return new DeletionRowCountDto(table, label, total, false);
    }
}
