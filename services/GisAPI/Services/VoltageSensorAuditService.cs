using GisAPI.Domain.Common;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Décide, boîtier par boîtier, si l'octet de tension mesure réellement la
/// batterie — et donc si l'interface a le droit de l'afficher.
///
/// <para><b>Le test : l'alternateur.</b> Dès qu'un moteur tourne, la tension
/// monte de ~12,6 V à ~14,2 V. C'est systématique et sans exception mécanique.
/// Un capteur qui rapporte la même valeur à l'arrêt et à 20 km/h ne mesure
/// donc rien. Constat sur la flotte TN au 14/08/2026 : <b>213 véhicules sur
/// 243</b> dans ce cas, écart médian nul. L'application affichait pourtant
/// « 12,9 V / 100 % » sur un véhicule qui ne démarrait pas.</para>
///
/// <para>Ce service ne produit aucune alerte : il ne fait qu'autoriser ou
/// interdire un affichage. Le verdict est écrit sur
/// <see cref="GisAPI.Domain.Entities.GpsDevice.VoltageSensorReliable"/> et lu
/// par le chemin de lecture du monitoring, qui est en polling permanent et ne
/// peut donc pas recalculer ça à chaque appel.</para>
///
/// <para><b>Par défaut on n'affiche pas.</b> <c>null</c> (jamais audité, ou
/// véhicule qui n'a pas assez roulé pour conclure) vaut « non affichable » :
/// devant une batterie, une absence de valeur est honnête, un « 100 % » faux
/// ne l'est pas.</para>
/// </summary>
public class VoltageSensorAuditService : BackgroundService
{
    // Le comportement d'un capteur ne change pas d'une heure à l'autre : une
    // passe par jour suffit largement, et la requête balaie 7 jours de
    // positions pour toute la flotte.
    private const int CycleHours = 24;
    private const int StartupDelayMinutes = 6;

    // 60 jours, et non 7 : cet audit ne juge pas l'état du moment mais une
    // propriété de l'installation — le capteur est-il relié à quelque chose ?
    // Cela ne change pas d'une semaine à l'autre, donc rien ne justifie de se
    // priver de l'historique disponible. La fenêtre de 7 j, reprise sans
    // réfléchir du service voisin (qui, lui, cherche un état récent), laissait
    // 15 véhicules sans verdict faute d'avoir assez roulé cette semaine-là.
    private const int WindowDays = 60;

    // Au-dessus de cette vitesse, le moteur tourne à coup sûr et l'alternateur
    // débite : c'est la fenêtre où la tension DOIT être haute.
    private const int DrivingSpeedKph = 20;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VoltageSensorAuditService> _logger;

    public VoltageSensorAuditService(
        IServiceProvider serviceProvider,
        ILogger<VoltageSensorAuditService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), ct); }
        catch (TaskCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "VoltageSensorAuditService cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromHours(CycleHours), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();

        // Médianes calculées PAR POSTGRES : ramener 7 jours de positions en
        // mémoire coûterait des millions de lignes. On ne rapatrie qu'une ligne
        // par boîtier.
        const string sql = @"
SELECT p.device_id AS ""DeviceId"",
       percentile_disc(0.5) WITHIN GROUP (ORDER BY p.power_voltage)
           FILTER (WHERE p.ignition_on AND p.speed_kph > {1})            AS ""DrivingMedian"",
       percentile_disc(0.5) WITHIN GROUP (ORDER BY p.power_voltage)
           FILTER (WHERE p.ignition_on = false)                          AS ""RestingMedian"",
       count(*) FILTER (WHERE p.ignition_on AND p.speed_kph > {1})       AS ""DrivingFrames"",
       count(*) FILTER (WHERE p.ignition_on = false)                     AS ""RestingFrames""
FROM gps_positions p
WHERE p.recorded_at >= {0}
  AND p.power_voltage IS NOT NULL
  AND p.power_voltage > 0
GROUP BY p.device_id;
";
        var since = DateTime.UtcNow.AddDays(-WindowDays);
        var rows = await context.Database
            .SqlQueryRaw<SensorSample>(sql, since, DrivingSpeedKph)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            _logger.LogInformation("VoltageSensorAudit: aucune trame de tension sur {Days} j", WindowDays);
            return;
        }

        var samples = rows.ToDictionary(r => r.DeviceId);
        var devices = await context.GpsDevices.IgnoreQueryFilters().ToListAsync(ct);

        var now = DateTime.UtcNow;
        int reliable = 0, unreliable = 0, undecided = 0;

        foreach (var device in devices)
        {
            samples.TryGetValue(device.Id, out var sample);
            var verdict = VoltageScale.EvaluateSensor(
                device.ProtocolType,
                sample?.DrivingMedian,
                sample?.RestingMedian,
                sample?.DrivingFrames ?? 0,
                sample?.RestingFrames ?? 0);

            device.VoltageSensorReliable = verdict;
            device.VoltageSensorCheckedAt = now;

            if (verdict == true) reliable++;
            else if (verdict == false) unreliable++;
            else undecided++;
        }

        await context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "VoltageSensorAudit: {Reliable} capteur(s) fiable(s), {Unreliable} plat(s) — masqué(s), {Undecided} sans conclusion",
            reliable, unreliable, undecided);
    }

    internal sealed class SensorSample
    {
        public int DeviceId { get; set; }
        public int? DrivingMedian { get; set; }
        public int? RestingMedian { get; set; }
        public long DrivingFrames { get; set; }
        public long RestingFrames { get; set; }
    }
}
