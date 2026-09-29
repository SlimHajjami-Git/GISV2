using GisAPI.Domain.Common;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Retient, pour chaque boîtier NEMS, la tension batterie relevée au DERNIER
/// DÉMARRAGE du véhicule, et l'écrit sur le boîtier
/// (<c>gps_devices.battery_start_raw</c> / <c>battery_start_at</c>).
///
/// <para><b>Pourquoi le démarrage, et lui seul</b> (Slim, 29/09/2026). L'octet
/// « Batterie » (34-36) des trames NEMS mélange deux grandeurs : moteur tournant
/// il porte l'alternateur — 13,5 à 14,4 V, qui ne dit rien de la batterie. Seul
/// l'instant du démarrage montre dans quel état elle est. La valeur retenue reste
/// donc affichée jusqu'au démarrage suivant.</para>
///
/// <para><b>Pourquoi un service de fond plutôt qu'un calcul à la lecture.</b>
/// <c>/vehicles/with-positions</c> est pollé toutes les ~30 s par page et par
/// utilisateur. Une version intermédiaire agrégeait la batterie dans sa requête de
/// statistiques : mesuré le 28/09/2026 sur les 307 véhicules HERTZ, 674 ms → 788
/// à 944 ms par appel. Ici le chemin chaud lit deux colonnes déjà chargées avec le
/// véhicule, et ne paie rien.</para>
///
/// <para><b>Ce que « démarrage » veut dire.</b> La première trame moteur allumé
/// après <see cref="VoltageScale.StartQuietPeriod"/> sans aucune trame moteur
/// allumé — et surtout PAS la transition <c>ignition_on</c> faux → vrai : sur un
/// NEMS à l'arrêt, ce drapeau bascule toutes les une à deux secondes (vérifié le
/// 28/09/2026 sur 243 TU 7247). La valeur est la médiane des
/// <see cref="VoltageScale.StartSampleFrames"/> premières trames exploitables des
/// <see cref="VoltageScale.StartSampleWindow"/> qui suivent : trois valeurs
/// suffisent à écarter la trame isolée aberrante.</para>
///
/// <para><b>Garde « l'octet bouge-t-il ».</b> Rien n'est écrit — et la valeur
/// précédente est effacée — si sur 24 h l'octet est strictement constant alors que
/// le véhicule a roulé ET s'est arrêté (<see cref="VoltageScale.NemsByteMoves"/>).
/// C'est la signature de l'incident du 14/08/2026, où l'application affichait
/// « 12,9 V / 100 % » sur un véhicule incapable de démarrer. Mesuré sur 891
/// journées-boîtier du 24 au 29/09/2026 : se déclenche sur UNE seule.</para>
/// </summary>
public class BatteryStartReadingService : BackgroundService
{
    // Un démarrage doit apparaître à l'écran en quelques minutes, pas en temps réel :
    // la batterie ne change pas d'état entre deux cycles.
    private const int CycleMinutes = 5;
    private const int StartupDelayMinutes = 4;

    // Âge maximal d'un démarrage retenu par un cycle. Cycle de 5 min pour une
    // détection sur 20 min : chaque démarrage est vu par quatre cycles, donc un cycle
    // raté ne perd rien. Les trames, elles, sont lues depuis StartQuietPeriod avant
    // cette borne, pour que la période de silence de chaque démarrage candidat soit
    // entièrement dans la fenêtre lue.
    private const int StartMaxAgeMinutes = 20;

    // PREMIER cycle après le démarrage du pod : on balaie 24 h pour repeupler tout le
    // parc d'un coup, sinon l'écran afficherait N/A jusqu'à ce que chaque véhicule
    // redémarre. Mesuré sur TN : ~7 s, une fois, contre ~350 ms pour un cycle normal.
    private const int BackfillMinutes = 24 * 60;

    // Fenêtre de la garde « l'octet bouge-t-il ». 24 h : assez pour qu'un véhicule ait
    // roulé et stationné, assez court pour qu'un boîtier reflashé soit repris le
    // lendemain — là où le verdict sur 7 jours de l'audit le laissait masqué des jours.
    private const int FrozenCheckHours = 24;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BatteryStartReadingService> _logger;

    public BatteryStartReadingService(
        IServiceProvider serviceProvider,
        ILogger<BatteryStartReadingService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), ct); }
        catch (TaskCanceledException) { return; }

        var backfill = true;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(backfill, ct);
                backfill = false;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un cycle raté n'est pas grave : le suivant revoit la même fenêtre.
                // En revanche, si c'est le rattrapage qui a échoué, on le rejouera.
                _logger.LogError(ex, "BatteryStartReadingService cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(CycleMinutes), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(bool backfill, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();

        var now = DateTime.UtcNow;
        var startFloor = now.AddMinutes(-(backfill ? BackfillMinutes : StartMaxAgeMinutes));
        var windowStart = startFloor - VoltageScale.StartQuietPeriod;

        // Une seule requête :
        //  - `dem`  : le dernier démarrage de chaque boîtier NEMS dans la fenêtre ;
        //  - `v`    : la médiane des premières trames exploitables de ce démarrage ;
        //  - `b`    : de quoi juger si l'octet a bougé sur 24 h.
        // Les deux LATERAL ne tournent que sur les boîtiers qui viennent de démarrer
        // (une trentaine par cycle sur TN), pas sur toute la flotte.
        const string sql = @"
WITH nems AS (
    SELECT d.id FROM gps_devices d WHERE lower(d.protocol_type) = {0}
),
f AS (
    SELECT p.device_id, p.recorded_at, p.ignition_on,
           max(CASE WHEN p.ignition_on THEN p.recorded_at END)
             OVER (PARTITION BY p.device_id ORDER BY p.recorded_at
                   ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS prev_ign_at
    FROM gps_positions p JOIN nems n ON n.id = p.device_id
    WHERE p.recorded_at >= {1}
),
dem AS (
    SELECT DISTINCT ON (device_id) device_id, recorded_at AS start_at
    FROM f
    WHERE ignition_on
      AND recorded_at >= {2}
      AND (prev_ign_at IS NULL OR recorded_at - prev_ign_at >= make_interval(secs => {3}))
    ORDER BY device_id, recorded_at DESC
)
SELECT d.device_id                        AS ""DeviceId"",
       d.start_at                         AS ""StartAt"",
       v.raw::int                         AS ""StartRaw"",
       b.mn::int                          AS ""MinRaw"",
       b.mx::int                          AS ""MaxRaw"",
       b.n_moving                         AS ""MovingFrames"",
       b.n_resting                        AS ""RestingFrames""
FROM dem d
CROSS JOIN LATERAL (
    SELECT percentile_disc(0.5) WITHIN GROUP (ORDER BY q.battery_raw) AS raw
    FROM (
        SELECT battery_raw FROM gps_positions
        WHERE device_id = d.device_id
          AND recorded_at >= d.start_at
          AND recorded_at <  d.start_at + make_interval(secs => {4})
          AND battery_raw BETWEEN {5} AND {6}
        ORDER BY recorded_at
        LIMIT {7}
    ) q
) v
CROSS JOIN LATERAL (
    SELECT min(battery_raw) AS mn, max(battery_raw) AS mx,
           count(*) FILTER (WHERE speed_kph > 5)                          AS n_moving,
           count(*) FILTER (WHERE speed_kph IS NULL OR speed_kph <= 5)    AS n_resting
    FROM gps_positions
    WHERE device_id = d.device_id
      AND recorded_at >= {8}
      AND battery_raw BETWEEN {5} AND {6}
) b;
";

        var rows = await context.Database.SqlQueryRaw<StartReadingRow>(
            sql,
            VoltageScale.NemsProtocol,
            windowStart,
            startFloor,
            VoltageScale.StartQuietPeriod.TotalSeconds,
            VoltageScale.StartSampleWindow.TotalSeconds,
            VoltageScale.NemsBatteryMeaningfulMinRaw,
            VoltageScale.NemsBatteryMeaningfulMaxRaw,
            VoltageScale.StartSampleFrames,
            now.AddHours(-FrozenCheckHours))
            .ToListAsync(ct);

        if (rows.Count == 0) return;

        var deviceIds = rows.Select(r => r.DeviceId).ToList();
        var devices = await context.GpsDevices.IgnoreQueryFilters()
            .Where(d => deviceIds.Contains(d.Id))
            .ToListAsync(ct);
        var byId = devices.ToDictionary(d => d.Id);

        // Démarrages déjà journalisés : chaque démarrage est vu par plusieurs cycles,
        // et l'index unique (device_id, start_at) refuserait le doublon en exception.
        var seenStarts = rows.Select(r => r.StartAt).ToList();
        var already = await context.BatteryStartReadings
            .Where(r => deviceIds.Contains(r.DeviceId) && seenStarts.Contains(r.StartAt))
            .Select(r => new { r.DeviceId, r.StartAt })
            .ToListAsync(ct);
        var alreadyKeys = already.Select(a => (a.DeviceId, a.StartAt)).ToHashSet();

        int retained = 0, frozen = 0;
        var touched = new List<int>();

        foreach (var row in rows)
        {
            if (!byId.TryGetValue(row.DeviceId, out var device)) continue;

            if (!VoltageScale.NemsByteMoves(row.MinRaw, row.MaxRaw, row.MovingFrames, row.RestingFrames))
            {
                // Octet figé moteur tournant ET moteur coupé : le capteur ne mesure
                // rien. On efface, y compris ce qui avait été retenu avant la panne, et
                // on ne journalise pas ce démarrage — il polluerait la médiane.
                if (device.BatteryStartRaw != null || device.BatteryStartAt != null
                    || device.BatteryStartMedianRaw != null)
                {
                    device.BatteryStartRaw = null;
                    device.BatteryStartAt = null;
                    device.BatteryStartMedianRaw = null;
                    device.UpdatedAt = now;
                }
                frozen++;
                _logger.LogWarning(
                    "BatteryStartReading: octet figé à {Raw} sur le boîtier {DeviceId} " +
                    "({Moving} trames en mouvement, {Resting} à l'arrêt sur {Hours} h) — batterie masquée",
                    row.MinRaw, row.DeviceId, row.MovingFrames, row.RestingFrames, FrozenCheckHours);
                continue;
            }

            if (row.StartRaw == null) continue;   // démarrage sans trame exploitable

            var raw = (short)row.StartRaw.Value;

            if (alreadyKeys.Add((row.DeviceId, row.StartAt)))
            {
                context.BatteryStartReadings.Add(new BatteryStartReading
                {
                    DeviceId = row.DeviceId,
                    StartAt = row.StartAt,
                    BatteryRaw = raw,
                    CreatedAt = now
                });
                touched.Add(row.DeviceId);
            }

            // La valeur AFFICHÉE est la dernière mesure : on ne recule jamais dessus.
            if (device.BatteryStartAt.HasValue && row.StartAt <= device.BatteryStartAt.Value)
                continue;

            device.BatteryStartRaw = raw;
            device.BatteryStartAt = row.StartAt;
            device.UpdatedAt = now;
            retained++;
        }

        if (retained > 0 || frozen > 0 || touched.Count > 0)
        {
            await context.SaveChangesAsync(ct);
        }

        var medianed = touched.Count > 0
            ? await RefreshMediansAsync(context, touched.Distinct().ToList(), byId, now, ct)
            : 0;

        if (retained > 0 || frozen > 0 || medianed > 0)
        {
            _logger.LogInformation(
                "BatteryStartReading: {Retained} tension(s) de démarrage retenue(s), " +
                "{Medianed} médiane(s) recalculée(s), {Frozen} boîtier(s) à octet figé{Backfill}",
                retained, medianed, frozen, backfill ? " (rattrapage initial sur 24 h)" : "");
        }
    }

    /// <summary>
    /// Recalcule, pour les boîtiers qui viennent d'enregistrer un démarrage, la médiane
    /// des <see cref="VoltageScale.StartHistoryWindow"/> derniers — c'est elle qui allume
    /// le témoin et déclenche la notification, jamais le dernier démarrage seul.
    ///
    /// <para>Les valeurs sont ramenées en mémoire (au plus 20 par boîtier, une trentaine
    /// de boîtiers par cycle) et la médiane est calculée par
    /// <see cref="VoltageScale.MedianStartRaw"/> : une seule définition de la règle,
    /// partagée avec les tests, plutôt qu'un <c>percentile_disc</c> en SQL qu'il faudrait
    /// garder d'accord avec le C#.</para>
    /// </summary>
    private static async Task<int> RefreshMediansAsync(
        GisDbContext context,
        List<int> deviceIds,
        Dictionary<int, Domain.Entities.GpsDevice> byId,
        DateTime now,
        CancellationToken ct)
    {
        const string sql = @"
SELECT device_id AS ""DeviceId"", battery_raw AS ""BatteryRaw""
FROM (
    SELECT device_id, battery_raw, start_at,
           row_number() OVER (PARTITION BY device_id ORDER BY start_at DESC) AS rn
    FROM battery_start_readings
    WHERE device_id = ANY({0})
) t
WHERE rn <= {1}
ORDER BY device_id, rn;
";
        var readings = await context.Database
            .SqlQueryRaw<RecentReadingRow>(sql, deviceIds.ToArray(), VoltageScale.StartHistoryWindow)
            .ToListAsync(ct);

        int updated = 0;
        foreach (var group in readings.GroupBy(r => r.DeviceId))
        {
            if (!byId.TryGetValue(group.Key, out var device)) continue;

            // La requête rend les valeurs du démarrage le plus récent au plus ancien.
            var median = VoltageScale.MedianStartRaw(group.Select(r => r.BatteryRaw).ToList());
            if (device.BatteryStartMedianRaw == median) continue;

            device.BatteryStartMedianRaw = median;
            device.UpdatedAt = now;
            updated++;
        }

        if (updated > 0) await context.SaveChangesAsync(ct);
        return updated;
    }

    /// <summary>Porteur des valeurs récentes — Npgsql exige un type public concret.</summary>
    public class RecentReadingRow
    {
        public int DeviceId { get; set; }
        public short BatteryRaw { get; set; }
    }

    /// <summary>
    /// Porteur des résultats — Npgsql exige un type public concret pour
    /// <c>SqlQueryRaw&lt;T&gt;</c>.
    /// </summary>
    public class StartReadingRow
    {
        public int DeviceId { get; set; }
        public DateTime StartAt { get; set; }
        /// <summary>Médiane des premières trames du démarrage, ou null si aucune n'est exploitable.</summary>
        public int? StartRaw { get; set; }
        public int? MinRaw { get; set; }
        public int? MaxRaw { get; set; }
        public long MovingFrames { get; set; }
        public long RestingFrames { get; set; }
    }
}
