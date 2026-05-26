using System.Text.Json;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Detects accidents in near-real-time from the MEMS accelerometer stream
/// written to <c>gps_positions</c>. Implements the V7 filter validated
/// empirically on 30 days of prod data:
///
/// <list type="bullet">
///   <item><description>
///     <c>xy_100 &gt;= 2</c> — both |X| and |Y| saturate at ≥100/127. The Z
///     axis is ignored on purpose (suspensions trigger it chronically).
///   </description></item>
///   <item><description>
///     <c>cluster_60s &gt;= 1</c> — at least one other high-G frame within
///     ±60s on the same device. Isolated spikes are noise.
///   </description></item>
///   <item><description>
///     <c>kph_before &gt;= 30</c> AND <c>n_moving_before &gt;= 5</c> —
///     sustained driving speed in the 5 min before the event, not a
///     parking manoeuvre or an install test.
///   </description></item>
///   <item><description>
///     <c>kph_after &lt;= 5</c> AND <c>n_after &gt;= 3</c> — the vehicle
///     actually stopped for at least a few frames in the 2-10 min after.
///     A real crash produces a full stop; a pothole does not.
///   </description></item>
///   <item><description>
///     <c>n_prior_7d &lt; 2</c> — the device hasn't produced similar
///     high-G frames in the last 7 days. Chronic-sensor vehicles (a
///     handful of them) saturate xy_100 dozens of times a week without
///     ever crashing — this guard rejects them.
///   </description></item>
/// </list>
///
/// <para>
/// The scan runs every <see cref="ScanIntervalMinutes"/> minutes. Each
/// iteration evaluates frames in the window
/// <c>[now - MaxAgeMinutes, now - PostEventDelayMinutes]</c> — the upper
/// bound is shifted into the past so every candidate has a full 2–10 min
/// slice of post-event GPS data (needed to assert the vehicle actually
/// stopped). Expected end-to-end detection latency:
/// <see cref="PostEventDelayMinutes"/> min + one scan-interval, so about
/// 10–12 min from impact to in-app notification + email.
/// </para>
///
/// <para>
/// Dedup is handled in two layers: a DB-level unique partial index on
/// <c>(vehicle_id, date_trunc('minute', incident_at))</c> plus an
/// application-level pre-check. Overlapping scans therefore never
/// produce duplicate rows even if the same candidate frame survives
/// multiple scan cycles.
/// </para>
///
/// <para>
/// Notifications fan out to every active user of the owning company
/// via <see cref="INotificationService.CreateAndSendAsync"/> (SignalR
/// for web, FCM for mobile) and to every configured
/// <c>alert_emails</c> recipient of <c>alert_type = "accident"</c> via
/// <see cref="IAlertEmailDispatcher"/>. Missing recipients fall back to
/// the company admin so an accident is never silently dropped.
/// </para>
/// </summary>
public class AccidentDetectionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGeocodingService _geocoding;
    private readonly IAccidentNarrativeService _narrativeService;
    private readonly ILogger<AccidentDetectionService> _logger;

    /// <summary>
    /// How often the detector wakes up. 2 min is a compromise between
    /// end-to-end latency (users want to know ASAP) and DB load — each
    /// iteration runs a handful of window-function queries on
    /// <c>gps_positions</c>.
    /// </summary>
    private const int ScanIntervalMinutes = 2;

    /// <summary>
    /// Oldest frame age considered in a scan. Anything older than this
    /// has already been evaluated by a previous cycle (bar dedup safety).
    /// Must be greater than <see cref="ScanIntervalMinutes"/> +
    /// <see cref="PostEventDelayMinutes"/> so late-arriving ingest frames
    /// still get picked up.
    /// </summary>
    private const int MaxAgeMinutes = 25;

    /// <summary>
    /// Minimum time to wait after a candidate frame before evaluating it.
    /// Gives the post-event window (<c>kph_after</c>, <c>n_after</c>)
    /// enough data to decide whether the vehicle actually stopped.
    /// </summary>
    private const int PostEventDelayMinutes = 10;

    /// <summary>
    /// Wait between API boot and the first scan — gives the DB and the
    /// ingest pipelines time to settle.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Notification + email routing key. Must match the value used in
    /// the frontend <c>alert-emails</c> dropdown and in
    /// <see cref="IAlertEmailDispatcher.DispatchAsync"/> lookups.
    /// </summary>
    private const string AlertType = "accident";

    /// <summary>
    /// In-app notification <c>type</c> (used by the frontend bell to pick
    /// an icon and to deduplicate per-user).
    /// </summary>
    private const string NotificationType = "accident_detected";

    public AccidentDetectionService(
        IServiceProvider serviceProvider,
        IGeocodingService geocoding,
        IAccidentNarrativeService narrativeService,
        ILogger<AccidentDetectionService> logger)
    {
        _serviceProvider = serviceProvider;
        _geocoding = geocoding;
        _narrativeService = narrativeService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(StartupDelay, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        _logger.LogInformation(
            "AccidentDetectionService started (scan every {Scan} min, lookback {Max} min, post-event delay {Delay} min)",
            ScanIntervalMinutes, MaxAgeMinutes, PostEventDelayMinutes);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AccidentDetectionService scan failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(ScanIntervalMinutes), ct);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var notifService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var alertDispatcher = scope.ServiceProvider.GetRequiredService<IAlertEmailDispatcher>();

        var now = DateTime.UtcNow;
        var windowEnd = now.AddMinutes(-PostEventDelayMinutes);
        var windowStart = now.AddMinutes(-MaxAgeMinutes);

        var candidates = await DetectCandidatesAsync(context, windowStart, windowEnd, ct);

        if (candidates.Count == 0)
        {
            _logger.LogDebug(
                "AccidentDetectionService: no candidates in window [{From} .. {To}]",
                windowStart, windowEnd);
            return;
        }

        _logger.LogInformation(
            "AccidentDetectionService: {Count} candidate(s) in window [{From} .. {To}]",
            candidates.Count, windowStart, windowEnd);

        foreach (var candidate in candidates)
        {
            try
            {
                await HandleCandidateAsync(context, notifService, alertDispatcher, candidate, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AccidentDetectionService: failed to handle candidate device={DeviceId} at={At}",
                    candidate.DeviceId, candidate.RecordedAt);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // V7 detection — raw SQL because window functions + LATERAL joins
    // don't translate cleanly to LINQ. Mirror of accident_score_test.sql.
    // ─────────────────────────────────────────────────────────────────────

    private async Task<List<AccidentCandidate>> DetectCandidatesAsync(
        GisDbContext context,
        DateTime windowStart,
        DateTime windowEnd,
        CancellationToken ct)
    {
        const string sql = @"
WITH candidates AS (
    SELECT
        p.device_id,
        p.recorded_at,
        p.latitude,
        p.longitude,
        p.speed_kph,
        ABS(GREATEST(LEAST(p.mems_x::int, 127), -128)) AS ax,
        ABS(GREATEST(LEAST(p.mems_y::int, 127), -128)) AS ay,
        ABS(GREATEST(LEAST(p.mems_z::int, 127), -128)) AS az,
        SQRT(
            POWER(GREATEST(LEAST(p.mems_x::int, 127), -128), 2)::double precision
          + POWER(GREATEST(LEAST(p.mems_y::int, 127), -128), 2)::double precision
          + POWER(GREATEST(LEAST(p.mems_z::int, 127), -128), 2)::double precision
        ) AS mag
    FROM gps_positions p
    WHERE p.recorded_at >= {0}
      AND p.recorded_at <  {1}
      AND p.is_valid = TRUE
      AND p.mems_x IS NOT NULL AND p.mems_y IS NOT NULL AND p.mems_z IS NOT NULL
      AND ABS(GREATEST(LEAST(p.mems_x::int, 127), -128)) >= 100
      AND ABS(GREATEST(LEAST(p.mems_y::int, 127), -128)) >= 100
),
clustered AS (
    SELECT c.*
    FROM candidates c
    WHERE EXISTS (
        SELECT 1
        FROM gps_positions n
        WHERE n.device_id = c.device_id
          AND n.recorded_at BETWEEN c.recorded_at - INTERVAL '60 seconds'
                                AND c.recorded_at + INTERVAL '60 seconds'
          AND n.recorded_at <> c.recorded_at
          AND n.is_valid = TRUE
          AND n.mems_x IS NOT NULL AND n.mems_y IS NOT NULL
    )
),
enriched AS (
    SELECT
        c.device_id,
        c.recorded_at,
        c.latitude,
        c.longitude,
        c.speed_kph,
        c.ax, c.ay, c.az, c.mag,
        (SELECT COUNT(*)
         FROM gps_positions h
         WHERE h.device_id   = c.device_id
           AND h.recorded_at >= c.recorded_at - INTERVAL '7 days'
           AND h.recorded_at <  c.recorded_at - INTERVAL '1 second'
           AND h.is_valid = TRUE
           AND h.mems_x IS NOT NULL AND h.mems_y IS NOT NULL
           AND ABS(GREATEST(LEAST(h.mems_x::int, 127), -128)) >= 100
           AND ABS(GREATEST(LEAST(h.mems_y::int, 127), -128)) >= 100
        ) AS n_prior_7d,
        (SELECT MAX(b.speed_kph)
         FROM gps_positions b
         WHERE b.device_id   = c.device_id
           AND b.recorded_at BETWEEN c.recorded_at - INTERVAL '5 minutes'
                                 AND c.recorded_at - INTERVAL '30 seconds'
        ) AS kph_bef,
        (SELECT COUNT(*)
         FROM gps_positions b
         WHERE b.device_id   = c.device_id
           AND b.recorded_at BETWEEN c.recorded_at - INTERVAL '5 minutes'
                                 AND c.recorded_at - INTERVAL '30 seconds'
           AND b.speed_kph >= 10
        ) AS n_mov_bef,
        (SELECT MAX(a.speed_kph)
         FROM gps_positions a
         WHERE a.device_id   = c.device_id
           AND a.recorded_at BETWEEN c.recorded_at + INTERVAL '2 minutes'
                                 AND c.recorded_at + INTERVAL '10 minutes'
        ) AS kph_aft,
        (SELECT COUNT(*)
         FROM gps_positions a
         WHERE a.device_id   = c.device_id
           AND a.recorded_at BETWEEN c.recorded_at + INTERVAL '2 minutes'
                                 AND c.recorded_at + INTERVAL '10 minutes'
        ) AS n_aft
    FROM clustered c
)
SELECT
    device_id     AS ""DeviceId"",
    recorded_at   AS ""RecordedAt"",
    latitude      AS ""Latitude"",
    longitude     AS ""Longitude"",
    COALESCE(speed_kph, 0)::double precision AS ""SpeedKph"",
    ax::int       AS ""Ax"",
    ay::int       AS ""Ay"",
    az::int       AS ""Az"",
    mag::double precision        AS ""Mag"",
    n_prior_7d::int              AS ""NPrior7d"",
    COALESCE(kph_bef, 0)::double precision AS ""KphBef"",
    n_mov_bef::int                         AS ""NMovBef"",
    COALESCE(kph_aft, 0)::double precision AS ""KphAft"",
    n_aft::int                             AS ""NAft""
FROM enriched
WHERE n_prior_7d   <  2
  AND COALESCE(kph_bef, 0) >= 30
  AND n_mov_bef    >= 5
  AND COALESCE(kph_aft, 999) <= 5
  AND n_aft        >= 3
ORDER BY device_id, recorded_at;
";

        return await context.Database
            .SqlQueryRaw<AccidentCandidate>(sql, windowStart, windowEnd)
            .ToListAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Per-candidate handler — resolves the vehicle, deduplicates, inserts
    // the AccidentEvent row, fans out in-app + email notifications.
    // ─────────────────────────────────────────────────────────────────────

    private async Task HandleCandidateAsync(
        GisDbContext context,
        INotificationService notifService,
        IAlertEmailDispatcher alertDispatcher,
        AccidentCandidate candidate,
        CancellationToken ct)
    {
        // Resolve the vehicle + device. A device without a vehicle is
        // unassigned and we have no one to notify — skip.
        var device = await context.GpsDevices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == candidate.DeviceId, ct);

        if (device == null)
        {
            _logger.LogWarning(
                "AccidentDetectionService: candidate for unknown device_id={DeviceId} — skipped",
                candidate.DeviceId);
            return;
        }

        var vehicle = await context.Vehicles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.GpsDeviceId == device.Id, ct);

        if (vehicle == null)
        {
            _logger.LogInformation(
                "AccidentDetectionService: candidate for device {DeviceId} ({DeviceUid}) has no vehicle attached — skipped",
                device.Id, device.DeviceUid);
            return;
        }

        // Operator-set immobilisation: when a vehicle is in the garage or
        // sitting on a tow truck, the MEMS values are garbage and we'd
        // otherwise emit fake "accident detected" alerts. Skip every
        // candidate for such vehicles.
        if (vehicle.IsImmobilized)
        {
            _logger.LogInformation(
                "AccidentDetectionService: vehicle {VehicleId} ({Plate}) is immobilised — accident candidate skipped",
                vehicle.Id, vehicle.Plate ?? vehicle.Name);
            return;
        }

        // Application-level dedup — belt-and-braces on top of the DB unique
        // index. We allow a ±5 min window so two close candidate frames
        // from the same impact (e.g. 16:02 + 16:03 like the 118013 rollover)
        // collapse into a single row anchored on the earliest one.
        var incidentMinute = TruncateToMinute(candidate.RecordedAt);

        var existing = await context.AccidentEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.VehicleId == vehicle.Id
                  && e.IncidentAt >= candidate.RecordedAt.AddMinutes(-5)
                  && e.IncidentAt <= candidate.RecordedAt.AddMinutes(5),
                ct);

        if (existing != null)
        {
            _logger.LogDebug(
                "AccidentDetectionService: candidate device={DeviceId} at={At} already persisted as accident {Id} — skipped",
                device.Id, candidate.RecordedAt, existing.Id);
            return;
        }

        var vehicleLabel = !string.IsNullOrWhiteSpace(vehicle.Plate)
            ? vehicle.Plate!
            : (!string.IsNullOrWhiteSpace(vehicle.Name) ? vehicle.Name : $"#{vehicle.Id}");

        var accidentEventId = await CreateAccidentEventAsync(
            context, vehicle, device, candidate, vehicleLabel, ct);

        // Notify FIRST (the deterministic narrative is already persisted), so
        // the alert is never delayed by the LLM call.
        await NotifyCompanyAsync(
            context, notifService, alertDispatcher,
            vehicle.CompanyId, vehicle.Id, vehicleLabel, accidentEventId, candidate, ct);

        // Then best-effort: upgrade the synthesis/reasons prose from the real
        // telemetry frames via Groq. Any failure leaves the deterministic
        // narrative untouched.
        await TryEnrichNarrativeAsync(context, accidentEventId, candidate, vehicleLabel, ct);
    }

    /// <summary>
    /// Best-effort LLM enrichment of an already-persisted accident. Reads the
    /// real telemetry frames, asks Groq for a faithful synthesis + reasons,
    /// and overwrites only those two prose fields when the output passes the
    /// guardrail. Story + indicators + every number stay deterministic.
    /// Swallows all errors — the report is already valid without this.
    /// </summary>
    private async Task TryEnrichNarrativeAsync(
        GisDbContext context,
        int accidentEventId,
        AccidentCandidate candidate,
        string vehicleLabel,
        CancellationToken ct)
    {
        try
        {
            var ev = await context.AccidentEvents
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == accidentEventId, ct);
            if (ev == null) return;

            var location = new GeocodedLocation(
                ev.LocationCommune, ev.LocationGovernorate, ev.LocationRoadType);

            var narrative = await _narrativeService.TryGenerateAsync(
                context, candidate, vehicleLabel, location, ct);
            if (narrative == null) return;

            var jsonOpts = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            ev.SynthesisText = narrative.SynthesisText;
            ev.ReasonsJson = JsonSerializer.Serialize(
                narrative.Reasons.Select(r => new { title = r.Title, text = r.Text }), jsonOpts);
            ev.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync(ct);

            _logger.LogInformation(
                "AccidentDetectionService: narrative enriched via LLM for accident {AccidentId}",
                accidentEventId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AccidentDetectionService: LLM narrative enrichment failed for accident {AccidentId} (non-blocking)",
                accidentEventId);
        }
    }

    private async Task<int> CreateAccidentEventAsync(
        GisDbContext context,
        Vehicle vehicle,
        GpsDevice device,
        AccidentCandidate candidate,
        string vehicleLabel,
        CancellationToken ct)
    {
        var incidentAt = DateTime.SpecifyKind(candidate.RecordedAt, DateTimeKind.Utc);

        // Confidence heuristic: start from 85 (V7 is tight, so every hit
        // is already high-confidence), add up to +10 for strong post-event
        // stop signal, +5 for very high MEMS magnitude.
        var confidence = 85
            + (candidate.NAft >= 6 ? 5 : 0)
            + (candidate.KphAft <= 1 ? 5 : 0)
            + (candidate.Mag >= 180 ? 5 : 0);
        confidence = Math.Clamp(confidence, 0, 100);

        var jsonOpts = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Reverse-geocode the impact point so the report shows a REAL location
        // (commune / governorate / road) instead of falling back to a generic
        // placeholder. Best-effort: a null result (Nominatim down / no usable
        // address) leaves the fields null and the report renders "—".
        GeocodedLocation? geo = null;
        try
        {
            geo = await _geocoding.ReverseGeocodeStructuredAsync(
                candidate.Latitude, candidate.Longitude);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AccidentDetectionService: reverse geocoding failed for ({Lat}, {Lon}) — location left empty",
                candidate.Latitude, candidate.Longitude);
        }

        var ev = new AccidentEvent
        {
            CompanyId = vehicle.CompanyId,
            VehicleId = vehicle.Id,
            GpsDeviceId = device.Id,
            DeviceUid = device.DeviceUid,
            IncidentAt = incidentAt,
            Latitude = candidate.Latitude,
            Longitude = candidate.Longitude,
            ReferenceCode = BuildReferenceCode(incidentAt, vehicleLabel),
            VehicleLabel = vehicleLabel,
            // Populated from the impact coordinates via reverse geocoding.
            // Null when the lookup yields nothing — the frontend then shows
            // "—" and still renders the exact GPS coordinates + map marker.
            LocationCommune = geo?.Commune,
            LocationGovernorate = geo?.Governorate,
            LocationRoadType = geo?.Road,
            SynthesisText = AccidentNarrativeBuilder.BuildSynthesisText(candidate),
            Confidence = confidence,
            StoryJson = JsonSerializer.Serialize(AccidentNarrativeBuilder.BuildStory(candidate), jsonOpts),
            ReasonsJson = JsonSerializer.Serialize(AccidentNarrativeBuilder.BuildReasons(candidate), jsonOpts),
            IndicatorsJson = JsonSerializer.Serialize(AccidentNarrativeBuilder.BuildIndicators(candidate), jsonOpts),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        context.AccidentEvents.Add(ev);

        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Another scan cycle raced us — look up the existing row and
            // return its id so notifications still fire on it.
            context.Entry(ev).State = EntityState.Detached;

            var winner = await context.AccidentEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstAsync(
                    e => e.VehicleId == vehicle.Id
                      && e.IncidentAt >= candidate.RecordedAt.AddMinutes(-5)
                      && e.IncidentAt <= candidate.RecordedAt.AddMinutes(5),
                    ct);

            _logger.LogDebug(
                "AccidentDetectionService: concurrent insert resolved by unique index — reusing accident {Id}",
                winner.Id);

            return winner.Id;
        }

        _logger.LogWarning(
            "AccidentDetectionService: accident persisted — vehicle {VehicleId} ({Plate}), company {CompanyId}, at {At}, mag {Mag:F0}, confidence {Confidence}",
            vehicle.Id, vehicleLabel, vehicle.CompanyId, incidentAt, candidate.Mag, confidence);

        return ev.Id;
    }

    private async Task NotifyCompanyAsync(
        GisDbContext context,
        INotificationService notifService,
        IAlertEmailDispatcher alertDispatcher,
        int companyId,
        int vehicleId,
        string vehicleLabel,
        int accidentEventId,
        AccidentCandidate candidate,
        CancellationToken ct)
    {
        // Admin-only fan-out: the decision modal is strictly blocking and
        // only company admins have the authority to confirm / dismiss an
        // accident. Drivers are intentionally excluded to avoid flooding
        // the cab with pop-ups over false positives.
        var users = await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.CompanyId == companyId
                     && u.Status == "active"
                     && u.Role != null
                     && u.Role.IsCompanyAdmin)
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);

        if (users.Count == 0)
        {
            _logger.LogWarning(
                "AccidentDetectionService: company {CompanyId} has no active admins — no notification sent for accident {AccidentId}",
                companyId, accidentEventId);
            return;
        }

        var localTime = candidate.RecordedAt.ToString("dd/MM/yyyy 'à' HH'h'mm");
        const string title = "Accident détecté sur votre véhicule";
        var message =
            $"Un événement compatible avec un accident grave a été détecté le {localTime} UTC " +
            $"sur le véhicule {vehicleLabel}. Cliquez pour consulter le rapport détaillé.";
        var actionUrl = $"/rapport-accident/{accidentEventId}";

        var metadata = new Dictionary<string, object>
        {
            ["accidentEventId"] = accidentEventId,
            ["vehicleId"] = vehicleId,
            ["vehicleLabel"] = vehicleLabel,
            ["incidentAt"] = candidate.RecordedAt.ToString("O"),
            ["latitude"] = candidate.Latitude,
            ["longitude"] = candidate.Longitude,
            ["magnitude"] = Math.Round(candidate.Mag),
            ["speedBeforeKph"] = Math.Round(candidate.KphBef),
            // Drives the frontend modal — the AccidentDecisionModalComponent
            // subscribes to notification$ and filters on
            // type === 'accident_detected' && metadata.requiresDecision === 'true'.
            // Stored as string so the dictionary survives SignalR's string
            // coercion round-trip intact.
            ["requiresDecision"] = "true",
        };

        int created = 0;
        int skipped = 0;
        foreach (var user in users)
        {
            var alreadyExists = await context.Notifications
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(n =>
                    n.UserId == user.Id
                    && n.Type == NotificationType
                    && n.ReferenceType == "accident_event"
                    && n.ReferenceId == accidentEventId,
                    ct);

            if (alreadyExists)
            {
                skipped++;
                continue;
            }

            try
            {
                await notifService.CreateAndSendAsync(
                    companyId: companyId,
                    userId: user.Id,
                    type: NotificationType,
                    title: title,
                    message: message,
                    priority: "critical",
                    referenceType: "accident_event",
                    referenceId: accidentEventId,
                    actionUrl: actionUrl,
                    metadata: metadata,
                    ct: ct);

                created++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "AccidentDetectionService: failed to create notification for user {UserId} ({Email})",
                    user.Id, user.Email);
            }
        }

        try
        {
            await alertDispatcher.DispatchAsync(
                companyId: companyId,
                alertType: AlertType,
                title: title,
                message: message,
                actionUrl: actionUrl,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AccidentDetectionService: failed to dispatch accident email for company {CompanyId}",
                companyId);
        }

        _logger.LogInformation(
            "AccidentDetectionService: accident {AccidentId} ({Plate}) dispatched — {Created} in-app created, {Skipped} skipped, company {CompanyId}",
            accidentEventId, vehicleLabel, created, skipped, companyId);
    }

    // Narrative builders moved to AccidentNarrativeBuilder so the manual-
    // accident enrichment pipeline (ManualAccidentEnrichmentService) can
    // produce an identical deterministic report from real frames.

    private static string BuildReferenceCode(DateTime incidentAt, string vehicleLabel)
    {
        var datePart = incidentAt.ToString("yyyy-MM-dd");
        var labelPart = vehicleLabel.Replace(' ', '-');
        return $"{datePart}-{labelPart}";
    }

    private static DateTime TruncateToMinute(DateTime dt)
        => new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Kind);

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        // Npgsql surfaces a 23505 SQLSTATE on unique index violations.
        var cause = ex.InnerException;
        while (cause != null)
        {
            if (cause is Npgsql.PostgresException pg && pg.SqlState == "23505")
                return true;
            cause = cause.InnerException;
        }
        return false;
    }
}

/// <summary>
/// Flat DTO used by <c>Database.SqlQueryRaw&lt;T&gt;</c> to return V7
/// candidate rows from PostgreSQL. Property names are mapped via the
/// <c>AS "PropertyName"</c> aliases in the SQL statement — EF Core binds
/// strictly by name (case-sensitive in Postgres once the identifier is
/// quoted).
/// </summary>
public class AccidentCandidate
{
    public int DeviceId { get; set; }
    public DateTime RecordedAt { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double SpeedKph { get; set; }
    public int Ax { get; set; }
    public int Ay { get; set; }
    public int Az { get; set; }
    public double Mag { get; set; }
    public int NPrior7d { get; set; }
    public double KphBef { get; set; }
    public int NMovBef { get; set; }
    public double KphAft { get; set; }
    public int NAft { get; set; }
}
