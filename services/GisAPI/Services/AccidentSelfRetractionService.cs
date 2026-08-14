using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GisAPI.Services;

/// <summary>
/// Retire automatiquement une détection d'accident quand le véhicule reprend
/// la route normalement — la preuve la plus simple qu'il n'y a pas eu de choc.
///
/// <para><b>Pourquoi ce service existe.</b> Le 14/08/2026, 251 TU 8875 a été
/// signalé « choc très violent, frontal et latéral » à 12:34 alors qu'il
/// roulait à 68 km/h moins d'une heure plus tard. Le client a reçu l'alerte et
/// nous a appelés. Aucun véhicule accidenté ne repart comme ça.</para>
///
/// <para><b>Pourquoi ce critère et pas un seuil plus strict.</b> J'ai d'abord
/// voulu comparer le pic d'accélération au bruit de fond du véhicule. Mesuré
/// sur les détections passées, ça ne discrimine rien : le faux positif avait un
/// rapport de 4,7 quand deux autres événements plafonnaient à 2,5 et 2,7. Un
/// tel seuil aurait supprimé les vrais en gardant le faux. Durcir la détection
/// est d'ailleurs dangereux ici — elle rate déjà de vrais accidents.</para>
///
/// <para>La reprise de route, elle, est un fait, pas une heuristique. Et comme
/// elle s'observe APRÈS coup, elle ne retarde pas l'alerte initiale d'une
/// seconde : on prévient vite, puis on se dédit vite si on s'est trompé.</para>
///
/// <para>Ne touche QUE les événements <c>pending</c> : dès qu'un humain a
/// tranché, sa décision prime. L'événement n'est jamais supprimé — il passe en
/// <c>dismissed</c> avec le motif ajouté, donc reste consultable.</para>
/// </summary>
public class AccidentSelfRetractionService : BackgroundService
{
    private const int CycleMinutes = 5;
    private const int StartupDelayMinutes = 4;

    // On ne surveille que les détections récentes : au-delà, un opérateur a eu
    // le temps de trancher et la reprise de route n'a plus valeur de preuve
    // (le véhicule peut avoir été réparé).
    private const int WatchHours = 6;

    // Laisser passer le creux qui suit tout incident : un vrai accidenté peut
    // être déplacé de quelques mètres pour dégager la voie.
    private const int GraceMinutes = 15;

    // Vitesse au-delà de laquelle on ne « dégage la chaussée » plus : on roule.
    private const double ResumedDrivingKph = 40.0;

    // Une trame isolée à 40 km/h peut être une aberration GPS. Il en faut
    // plusieurs pour parler de reprise de route.
    private const int ResumedFramesRequired = 5;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AccidentSelfRetractionService> _logger;

    public AccidentSelfRetractionService(
        IServiceProvider serviceProvider,
        ILogger<AccidentSelfRetractionService> logger)
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
                _logger.LogError(ex, "AccidentSelfRetractionService cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(CycleMinutes), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();

        var cutoff = DateTime.UtcNow.AddHours(-WatchHours);

        var events = await context.AccidentEvents
            .IgnoreQueryFilters()
            .Where(e => e.Status == "pending"
                     && e.IncidentAt >= cutoff
                     && e.GpsDeviceId != null)
            .ToListAsync(ct);

        if (events.Count == 0) return;

        int retracted = 0;
        foreach (var ev in events)
        {
            try
            {
                var resumedAt = await FindResumedDrivingAsync(
                    context, ev.GpsDeviceId!.Value, ev.IncidentAt, ct);
                if (resumedAt == null) continue;

                var minutes = (int)Math.Round((resumedAt.Value - ev.IncidentAt).TotalMinutes);

                ev.Status = "dismissed";
                ev.DecidedAt = DateTime.UtcNow;   // DecidedByUserId reste nul : décision automatique
                ev.UpdatedAt = DateTime.UtcNow;
                ev.ReasonsJson = AppendRetractionReason(ev.ReasonsJson, minutes);

                await context.SaveChangesAsync(ct);
                retracted++;

                _logger.LogInformation(
                    "AccidentSelfRetraction: événement {Id} ({Plate}) retiré — le véhicule roulait à nouveau {Min} min après",
                    ev.Id, ev.VehicleLabel ?? "?", minutes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "AccidentSelfRetractionService: échec sur l'événement {Id}", ev.Id);
            }
        }

        if (retracted > 0)
        {
            _logger.LogInformation(
                "AccidentSelfRetraction: {Retracted} détection(s) retirée(s) sur {Total} en attente",
                retracted, events.Count);
        }
    }

    /// <summary>
    /// Instant de la N-ième trame au-dessus du seuil après le délai de grâce,
    /// ou null si le véhicule n'a pas repris la route.
    /// </summary>
    private static async Task<DateTime?> FindResumedDrivingAsync(
        GisDbContext context, int deviceId, DateTime incidentAt, CancellationToken ct)
    {
        var from = incidentAt.AddMinutes(GraceMinutes);

        var frames = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= from
                     && p.IsValid
                     && p.SpeedKph >= ResumedDrivingKph)
            .OrderBy(p => p.RecordedAt)
            .Select(p => p.RecordedAt)
            .Take(ResumedFramesRequired)
            .ToListAsync(ct);

        return frames.Count >= ResumedFramesRequired ? frames[^1] : null;
    }

    /// <summary>
    /// Ajoute le motif du retrait à la liste des raisons, sans écraser celles
    /// qui ont motivé la détection — l'opérateur doit pouvoir lire les deux.
    /// </summary>
    private static string? AppendRetractionReason(string? reasonsJson, int minutes)
    {
        var reasons = new List<Dictionary<string, string>>();

        if (!string.IsNullOrWhiteSpace(reasonsJson))
        {
            try
            {
                reasons = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(reasonsJson)
                          ?? new List<Dictionary<string, string>>();
            }
            catch (JsonException)
            {
                // Raisons illisibles : on repart d'une liste vide plutôt que de
                // perdre le retrait lui-même, qui est l'information utile.
                reasons = new List<Dictionary<string, string>>();
            }
        }

        reasons.Add(new Dictionary<string, string>
        {
            ["title"] = "Détection retirée automatiquement",
            ["text"] = $"Le véhicule a repris la route à plus de {ResumedDrivingKph:F0} km/h " +
                       $"{minutes} minutes après l'événement. Un véhicule réellement accidenté " +
                       "ne repart pas ainsi : la détection est annulée."
        });

        return JsonSerializer.Serialize(reasons);
    }
}
