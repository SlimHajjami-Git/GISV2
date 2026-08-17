using GisAPI.Application.Common.Interfaces;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Détecte qu'un véhicule <b>n'arrive plus à démarrer</b>, en lisant le
/// démarreur plutôt que la batterie.
///
/// <para><b>Pourquoi pas la tension.</b> Cinq formulations ont été testées sur
/// le canal <c>power_voltage</c> et toutes réfutées par les chiffres : seuil
/// absolu, seuil par véhicule, bit d'alimentation de secours, pente de
/// décroissance après arrêt, et fréquence des valeurs basses. La raison est
/// physique : la tension au repos mesure l'état de CHARGE, pas la capacité à
/// délivrer 300 A à un démarreur. Une batterie sulfatée affiche 12,6 V et ne
/// démarre pas — c'est exactement ce qui est arrivé aux cinq pannes du 14/08.
/// S'ajoute une résolution de 0,3 V par unité, plus grossière que les écarts
/// qu'il faudrait distinguer.</para>
///
/// <para><b>Le signal retenu.</b> Un conducteur qui n'arrive pas à démarrer
/// tourne la clé plusieurs fois : le contact se ferme et retombe en quelques
/// secondes, à l'arrêt, sans que le véhicule ne parte jamais. Ce motif seul est
/// du bruit — 96 046 impulsions en 7 jours sur la flotte. C'est la CONJONCTION
/// avec l'immobilité de la journée et le fait d'avoir roulé la veille qui
/// discrimine : 16 jours-véhicule sur 8 jours mesurés, soit environ 2 alertes
/// par jour pour 250 véhicules.</para>
///
/// <para><b>Ce que ça fait, et ne fait pas.</b> Ça constate la panne le jour
/// même, ça ne l'anticipe pas. Sur les cinq pannes connues, cela aurait prévenu
/// le client avant qu'il n'appelle. Étalonné sur un petit nombre de cas : les
/// seuils sont à revoir quand d'autres pannes auront été confirmées.</para>
/// </summary>
public class StartFailureDetectionService : BackgroundService
{
    private const int CycleMinutes = 15;
    private const int StartupDelayMinutes = 5;
    private const int CooldownHours = 24;

    // Une impulsion = contact fermé puis rouvert en moins de 10 s, à l'arrêt.
    private const int PulseMaxSeconds = 10;
    private const int PulseMaxSpeedKph = 5;

    // Deux impulsions à 2 s d'intervalle, c'est un contact qui scintille, pas
    // deux tentatives. On ne compte une tentative distincte qu'au-delà de ce délai.
    private const int DistinctAttemptSeconds = 20;

    // En deçà on ne conclut pas : une tentative isolée arrive sur un véhicule sain.
    private const int MinAttempts = 2;

    // Le véhicule ne doit pas avoir roulé de la journée...
    private const int ImmobileMaxKph = 6;

    // ...mais doit avoir roulé la veille, sinon c'est un véhicule au repos
    // prolongé et non une panne.
    private const int DroveYesterdayMinKph = 20;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StartFailureDetectionService> _logger;

    public StartFailureDetectionService(
        IServiceProvider serviceProvider,
        ILogger<StartFailureDetectionService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), ct); }
        catch (TaskCanceledException) { return; }

        _logger.LogInformation(
            "StartFailureDetectionService started (cycle={Cycle}min, min {Min} tentatives espacées de {Gap}s, temporisation={Cool}h)",
            CycleMinutes, MinAttempts, DistinctAttemptSeconds, CooldownHours);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "StartFailureDetectionService cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(CycleMinutes), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var notifService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        // Tout est calculé par Postgres, sur une fenêtre de 2 jours seulement :
        // le balayage reste borné. (L'audit du capteur, lui, saturait la base
        // dès qu'on lui demandait 60 jours — voir VoltageSensorAuditService.)
        const string sql = @"
WITH seq AS (
    SELECT p.device_id, p.recorded_at, p.ignition_on, p.speed_kph,
           LEAD(p.ignition_on) OVER w AS next_ign,
           LEAD(p.recorded_at) OVER w AS next_at
    FROM gps_positions p
    WHERE p.recorded_at >= {0}
    WINDOW w AS (PARTITION BY p.device_id ORDER BY p.recorded_at)
),
impulsions AS (
    SELECT device_id, recorded_at,
           LAG(recorded_at) OVER (PARTITION BY device_id ORDER BY recorded_at) AS prev_imp
    FROM seq
    WHERE ignition_on AND next_ign = FALSE
      AND next_at - recorded_at <= make_interval(secs => {1})
      AND COALESCE(speed_kph, 0) <= {2}
),
tentatives AS (
    SELECT device_id, COUNT(*) AS n, MAX(recorded_at) AS derniere
    FROM impulsions
    WHERE recorded_at >= {3}
      AND (prev_imp IS NULL OR recorded_at - prev_imp >= make_interval(secs => {4}))
    GROUP BY device_id
),
aujourdhui AS (
    SELECT device_id, MAX(speed_kph) AS vmax
    FROM gps_positions WHERE recorded_at >= {3} GROUP BY device_id
),
veille AS (
    SELECT device_id, MAX(speed_kph) AS vmax
    FROM gps_positions WHERE recorded_at >= {0} AND recorded_at < {3} GROUP BY device_id
)
SELECT t.device_id AS ""DeviceId"",
       t.n::int    AS ""Attempts"",
       t.derniere  AS ""LastAttemptAt""
FROM tentatives t
JOIN aujourdhui a ON a.device_id = t.device_id
JOIN veille v     ON v.device_id = t.device_id
WHERE t.n >= {5}
  AND COALESCE(a.vmax, 0) < {6}
  AND COALESCE(v.vmax, 0) > {7};
";
        var startOfToday = DateTime.UtcNow.Date;
        var startOfYesterday = startOfToday.AddDays(-1);

        var hits = await context.Database
            .SqlQueryRaw<StartFailureHit>(sql,
                startOfYesterday, PulseMaxSeconds, PulseMaxSpeedKph,
                startOfToday, DistinctAttemptSeconds,
                MinAttempts, ImmobileMaxKph, DroveYesterdayMinKph)
            .ToListAsync(ct);

        if (hits.Count == 0) return;

        var cooldownCutoff = DateTime.UtcNow.AddHours(-CooldownHours);
        var deviceIds = hits.Select(h => h.DeviceId).ToList();

        var devices = await context.GpsDevices
            .IgnoreQueryFilters()
            .Include(d => d.Vehicle)
            .Where(d => deviceIds.Contains(d.Id)
                     && (d.LastStartFailureAlertAt == null
                         || d.LastStartFailureAlertAt < cooldownCutoff))
            .ToListAsync(ct);

        int notified = 0;
        foreach (var device in devices)
        {
            var hit = hits.First(h => h.DeviceId == device.Id);
            try
            {
                await NotifyAsync(context, notifService, device, hit, ct);
                device.LastStartFailureAlertAt = DateTime.UtcNow;
                device.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(ct);
                notified++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "StartFailureDetection: échec de notification pour le boîtier {DeviceId}", device.Id);
            }
        }

        if (notified > 0)
        {
            _logger.LogInformation(
                "StartFailureDetection: {Notified} véhicule(s) signalé(s) sur {Total} détecté(s)",
                notified, hits.Count);
        }
    }

    private async Task NotifyAsync(
        GisDbContext context,
        INotificationService notifService,
        Domain.Entities.GpsDevice device,
        StartFailureHit hit,
        CancellationToken ct)
    {
        var admins = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.CompanyId == device.CompanyId
                     && u.Status == "active"
                     && u.Role != null
                     && u.Role.IsCompanyAdmin)
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (admins.Count == 0) return;

        var label = device.Vehicle?.Plate ?? device.Vehicle?.Name ?? "Véhicule";
        var heure = hit.LastAttemptAt.ToString("HH'h'mm");

        const string title = "Véhicule qui ne démarre pas";
        var message =
            $"Le {label} a fait {hit.Attempts} tentatives de démarrage sans succès " +
            $"(dernière à {heure} UTC) et n'a pas roulé aujourd'hui, alors qu'il roulait hier. " +
            "Batterie ou démarreur à vérifier.";

        var metadata = new Dictionary<string, object>
        {
            ["deviceId"] = device.Id,
            ["vehicleId"] = device.Vehicle?.Id ?? 0,
            ["attempts"] = hit.Attempts,
            ["lastAttemptAt"] = hit.LastAttemptAt.ToString("O"),
        };

        foreach (var adminId in admins)
        {
            await notifService.CreateAndSendAsync(
                companyId: device.CompanyId,
                userId: adminId,
                type: "start_failure",
                title: title,
                message: message,
                priority: "high",
                referenceType: "vehicle",
                referenceId: device.Vehicle?.Id,
                actionUrl: device.Vehicle != null ? $"/vehicules/{device.Vehicle.Id}" : null,
                metadata: metadata,
                ct: ct);
        }

        _logger.LogInformation(
            "StartFailureDetection: {Plate} — {N} tentatives, dernière à {Heure} UTC",
            label, hit.Attempts, heure);
    }

    internal sealed class StartFailureHit
    {
        public int DeviceId { get; set; }
        public int Attempts { get; set; }
        public DateTime LastAttemptAt { get; set; }
    }
}
