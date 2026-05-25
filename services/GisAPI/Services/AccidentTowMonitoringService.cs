using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Background watcher that picks up every <c>confirmed</c>
/// <c>accident_events</c> row (without a <c>tow_detected_at</c> yet) and
/// scans the vehicle's subsequent GPS frames for resumed motion.
///
/// <para>Heuristic: the event is considered towed away as soon as three
/// consecutive frames post-<c>incident_at</c> report a speed above
/// <see cref="TowSpeedThresholdKph"/> km/h (via the
/// <c>LAG(speed, 1..2) OVER (ORDER BY recorded_at)</c> window). The first
/// qualifying frame's timestamp is written to <c>tow_detected_at</c> and a
/// &quot;Remorquage détecté&quot; notification is pushed to every company admin.</para>
///
/// <para>Watch window: <see cref="TowWatchDays"/> days from the incident.
/// Past that, the row is silently dropped from the active set — the vehicle
/// is either permanently out of service or the event never resulted in a
/// tow (both acceptable outcomes).</para>
///
/// <para>Cycle: runs every <see cref="CycleMinutes"/> minutes. We don't use
/// Redis or RabbitMQ — the <c>gps_positions</c> table is already indexed
/// on <c>(device_id, recorded_at)</c>, so a targeted window-function scan
/// per confirmed event is cheap.</para>
///
/// <para>The service is tenant-agnostic: it runs with
/// <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters{T}"/>
/// because there is no HTTP request scope at background-service time.</para>
/// </summary>
public class AccidentTowMonitoringService : BackgroundService
{
    private const int CycleMinutes = 5;
    private const int StartupDelayMinutes = 2;
    private const int TowWatchDays = 14;
    private const double TowSpeedThresholdKph = 5.0;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AccidentTowMonitoringService> _logger;

    public AccidentTowMonitoringService(
        IServiceProvider serviceProvider,
        ILogger<AccidentTowMonitoringService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Give the app a moment to finish wiring DI + migrations before we
        // start hammering the DB.
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), ct); }
        catch (TaskCanceledException) { return; }

        _logger.LogInformation(
            "AccidentTowMonitoringService started (cycle={CycleMin}min, watch={WatchDays}d, threshold={Threshold}km/h)",
            CycleMinutes, TowWatchDays, TowSpeedThresholdKph);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "AccidentTowMonitoringService cycle failed");
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

        var cutoff = DateTime.UtcNow.AddDays(-TowWatchDays);

        // Candidate events: admin said "yes it's a real accident" (status
        // "confirmed" or, per Calypso 6 P9, "awaiting_details" — the
        // post-confirm state where the admin has not yet filled the
        // damages form: tow monitoring kicks in immediately). Not yet
        // marked towed, still inside the 14-day watch window. We also
        // exclude events whose vehicle has been manually immobilised —
        // when a damaged vehicle is sitting in the mechanic's yard the
        // boîtier emits spurious motion frames (someone moves the car
        // in the parking, the engine is bench-tested, …) that would
        // falsely trigger "tow detected".
        //
        // We need the tracked entity back to stamp tow_detected_at, so
        // no AsNoTracking().
        var events = await context.AccidentEvents
            .IgnoreQueryFilters()
            .Where(e => (e.Status == "confirmed" || e.Status == "awaiting_details")
                     && e.TowDetectedAt == null
                     && e.IncidentAt >= cutoff
                     && e.GpsDeviceId != null
                     && !context.Vehicles.Any(v => v.Id == e.VehicleId && v.IsImmobilized))
            .ToListAsync(ct);

        if (events.Count == 0) return;

        _logger.LogDebug(
            "AccidentTowMonitoringService: scanning {Count} confirmed accident event(s) for resumed motion",
            events.Count);

        int stamped = 0;
        foreach (var ev in events)
        {
            try
            {
                var towAt = await FindTowMomentAsync(context, ev.GpsDeviceId!.Value, ev.IncidentAt, ct);
                if (towAt == null) continue;

                ev.TowDetectedAt = towAt;
                // Append the tow to the report timeline — ONLY now that it is
                // really detected, using the REAL timestamp. Never hardcoded:
                // no detection ⇒ no story line (better no info than wrong info).
                ev.StoryJson = AppendTowEventToStory(ev.StoryJson, towAt.Value, ev.IncidentAt);
                ev.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(ct);
                stamped++;

                _logger.LogInformation(
                    "AccidentTowMonitoringService: tow detected for accident {AccidentId} ({Plate}) at {TowAt}",
                    ev.Id, ev.VehicleLabel, towAt);

                await NotifyTowDetectedAsync(context, notifService, ev.CompanyId, ev.Id, ev.VehicleLabel, towAt.Value, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "AccidentTowMonitoringService: failed to process accident {AccidentId}",
                    ev.Id);
            }
        }

        if (stamped > 0)
        {
            _logger.LogInformation(
                "AccidentTowMonitoringService: stamped {Stamped}/{Total} events with tow_detected_at",
                stamped, events.Count);
        }
    }

    /// <summary>
    /// Looks for the first frame in <c>gps_positions</c> where the device
    /// has logged three consecutive speeds above the threshold. Returns
    /// the moment that triggering frame was recorded, or <c>null</c> if
    /// nothing qualifies within the 14-day window.
    ///
    /// <para>The LAG window function is evaluated inside Postgres — we only
    /// ever transfer a single timestamp back to .NET.</para>
    /// </summary>
    private static async Task<DateTime?> FindTowMomentAsync(
        GisDbContext context,
        int deviceId,
        DateTime incidentAt,
        CancellationToken ct)
    {
        const string sql = @"
SELECT recorded_at AS ""Value""
FROM (
    SELECT recorded_at,
           speed_kph,
           LAG(speed_kph, 1) OVER (ORDER BY recorded_at) AS prev1,
           LAG(speed_kph, 2) OVER (ORDER BY recorded_at) AS prev2
    FROM gps_positions
    WHERE device_id = {0}
      AND recorded_at >  {1}
      AND recorded_at <= {2}
      AND is_valid = TRUE
) p
WHERE speed_kph > {3}
  AND prev1     > {3}
  AND prev2     > {3}
ORDER BY recorded_at
LIMIT 1;
";

        var windowEnd = incidentAt.AddDays(TowWatchDays);

        var moments = await context.Database
            .SqlQueryRaw<TowMomentRow>(
                sql,
                deviceId,
                incidentAt,
                windowEnd,
                TowSpeedThresholdKph)
            .ToListAsync(ct);

        return moments.Count > 0 ? moments[0].Value : null;
    }

    /// <summary>
    /// Appends a single, factual tow event to the report timeline JSON using
    /// the REAL detection timestamp. Safety-first:
    /// <list type="bullet">
    ///   <item>Called only when a tow has actually been detected — there is
    ///   no hardcoded / fallback tow line anywhere.</item>
    ///   <item>If the stored story isn't a JSON array (corrupt / unexpected),
    ///   we return it unchanged rather than risk replacing the real
    ///   chronology with a lone tow line.</item>
    ///   <item>If the date differs from the impact day, the label carries the
    ///   date so a multi-day gap is never shown as if it were same-day.</item>
    /// </list>
    /// </summary>
    private static string AppendTowEventToStory(string? storyJson, DateTime towAt, DateTime incidentAt)
    {
        try
        {
            JsonArray arr;
            if (string.IsNullOrWhiteSpace(storyJson))
            {
                arr = new JsonArray();
            }
            else
            {
                // Unexpected shape → leave the real story untouched.
                if (JsonNode.Parse(storyJson) is not JsonArray parsed) return storyJson;
                arr = parsed;
            }

            var sameDay = towAt.Date == incidentAt.Date;
            var timeLabel = sameDay
                ? towAt.ToString("HH'h' mm")
                : towAt.ToString("dd/MM HH'h' mm");

            arr.Add(new JsonObject
            {
                ["time"] = timeLabel,
                ["title"] = "Reprise de mouvement (remorquage probable)",
                ["body"] =
                    "Le véhicule s'est remis à se déplacer (plus de 5 km/h sur plusieurs relevés "
                  + "consécutifs) après une période d'immobilisation. Ce mouvement est compatible avec "
                  + "un chargement sur dépanneuse ou un déplacement du véhicule par un tiers.",
                ["severity"] = "neutral",
            });

            return arr.ToJsonString(new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch
        {
            // Never block / corrupt on an append failure — keep the original.
            return storyJson ?? "";
        }
    }

    private static async Task NotifyTowDetectedAsync(
        GisDbContext context,
        INotificationService notifService,
        int companyId,
        int accidentEventId,
        string? vehicleLabel,
        DateTime towAt,
        CancellationToken ct)
    {
        var admins = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.CompanyId == companyId
                     && u.Status == "active"
                     && u.Role != null
                     && u.Role.IsCompanyAdmin)
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (admins.Count == 0) return;

        var label = string.IsNullOrWhiteSpace(vehicleLabel) ? "véhicule" : vehicleLabel;
        var localTime = towAt.ToString("dd/MM/yyyy 'à' HH'h'mm");
        var title = "Remorquage détecté";
        var message = $"Le {label} a recommencé à bouger le {localTime} UTC — il semble avoir été remorqué.";
        var actionUrl = $"/rapport-accident/{accidentEventId}";

        var metadata = new Dictionary<string, object>
        {
            ["accidentEventId"] = accidentEventId,
            ["vehicleLabel"] = label,
            ["towDetectedAt"] = towAt.ToString("O"),
        };

        foreach (var adminId in admins)
        {
            try
            {
                await notifService.CreateAndSendAsync(
                    companyId: companyId,
                    userId: adminId,
                    type: "accident_tow_detected",
                    title: title,
                    message: message,
                    priority: "high",
                    referenceType: "accident_event",
                    referenceId: accidentEventId,
                    actionUrl: actionUrl,
                    metadata: metadata,
                    ct: ct);
            }
            catch (Exception)
            {
                // Per-admin failures are swallowed — the next cycle does NOT
                // retry because tow_detected_at is already stamped; we
                // accept that a rare push miss is recoverable via the
                // unread count on the bell icon.
            }
        }
    }

    /// <summary>
    /// Single-column carrier for <c>SqlQueryRaw&lt;T&gt;</c> — EF binds the
    /// result set by the column alias <c>Value</c>.
    /// </summary>
    private sealed class TowMomentRow
    {
        public DateTime Value { get; set; }
    }
}
