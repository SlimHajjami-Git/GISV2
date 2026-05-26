using System.Text.Json;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Calypso 8 — post-creation enrichment for manually declared accidents.
///
/// <para>When an admin files an accident through
/// <c>POST /api/accident-reports/manual</c>, the row is persisted by
/// <c>CreateManualAccidentCommand</c> with empty story / indicators /
/// reasons (the deterministic detection pipeline didn't see it). The
/// controller then fires this enricher as background work so the user gets
/// an immediate redirect to the report page without waiting on the
/// telemetry scan or the LLM call.</para>
///
/// <para>The enricher mirrors the real-time path:</para>
/// <list type="number">
///   <item>Loads the accident row and its device.</item>
///   <item>Pulls every valid GPS frame in
///     <c>[IncidentAt - 10 min ; IncidentAt + 15 min]</c>.</item>
///   <item>Builds an <see cref="AccidentCandidate"/> from those frames —
///     finds the MEMS peak in a ±2 min window around the user-entered
///     time (handles approximate timestamps), then computes the same
///     cruise / post-event speed + dwell statistics as the V7 detector.</item>
///   <item>Persists the deterministic narrative via
///     <see cref="AccidentNarrativeBuilder"/> so the report renders rich
///     content even if the LLM step fails.</item>
///   <item>Best-effort: hands the candidate to
///     <see cref="IAccidentNarrativeService"/> for an LLM-rewritten
///     synthesis + reasons. Any failure leaves the deterministic narrative
///     in place — the report stays valid.</item>
/// </list>
///
/// <para>Hard guarantees:</para>
/// <list type="bullet">
///   <item>Never throws — every failure is logged and swallowed.</item>
///   <item>Idempotent — if called twice the second pass overwrites with
///     the same content.</item>
///   <item>Tenant-safe — runs with <c>IgnoreQueryFilters()</c> because
///     the BackgroundService scope has no tenant context.</item>
/// </list>
/// </summary>
public interface IManualAccidentEnricher
{
    /// <summary>
    /// Fire-and-forget enrichment of a freshly created manual accident.
    /// Returns the spawned task so callers can <c>await</c> it in tests,
    /// but the production caller simply discards it.
    /// </summary>
    Task EnrichInBackgroundAsync(int accidentEventId);
}

public class ManualAccidentEnrichmentService : IManualAccidentEnricher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ManualAccidentEnrichmentService> _logger;

    public ManualAccidentEnrichmentService(
        IServiceScopeFactory scopeFactory,
        ILogger<ManualAccidentEnrichmentService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task EnrichInBackgroundAsync(int accidentEventId)
    {
        return Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
                var narrative = scope.ServiceProvider.GetRequiredService<IAccidentNarrativeService>();

                await EnrichAsync(context, narrative, accidentEventId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ManualAccidentEnrichmentService: background enrichment failed for accident {Id} (non-blocking)",
                    accidentEventId);
            }
        });
    }

    private async Task EnrichAsync(
        GisDbContext context,
        IAccidentNarrativeService narrativeService,
        int accidentEventId,
        CancellationToken ct)
    {
        var ev = await context.AccidentEvents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == accidentEventId, ct);

        if (ev == null)
        {
            _logger.LogWarning(
                "ManualAccidentEnrichmentService: accident {Id} not found — skipping",
                accidentEventId);
            return;
        }

        if (ev.GpsDeviceId == null)
        {
            _logger.LogInformation(
                "ManualAccidentEnrichmentService: accident {Id} has no GPS device — nothing to enrich",
                accidentEventId);
            return;
        }

        var deviceId = ev.GpsDeviceId.Value;
        var incidentAt = DateTime.SpecifyKind(ev.IncidentAt, DateTimeKind.Utc);

        // Pull a slightly larger window than detection needs: -10 / +15 min,
        // so we can compute peak / cruise / post-event statistics even when
        // the user-entered time is approximate.
        var from = incidentAt.AddMinutes(-10);
        var to = incidentAt.AddMinutes(+15);

        var frames = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= from
                     && p.RecordedAt <= to
                     && p.IsValid)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new RawFrame
            {
                RecordedAt = p.RecordedAt,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                SpeedKph = p.SpeedKph ?? 0,
                MemsX = p.MemsX,
                MemsY = p.MemsY,
                MemsZ = p.MemsZ,
            })
            .ToListAsync(ct);

        if (frames.Count < 5)
        {
            _logger.LogInformation(
                "ManualAccidentEnrichmentService: only {Count} frames around accident {Id} (device {DeviceId} at {At}) — skipping",
                frames.Count, accidentEventId, deviceId, incidentAt);
            return;
        }

        var candidate = await BuildCandidateAsync(context, frames, deviceId, incidentAt, ev.Id, ct);

        // No honesty guard needed: AccidentNarrativeBuilder now adapts to
        // a "no MEMS evidence" candidate (returns Direction.Unknown +
        // Intensity.Negligible) and emits an honest "Événement déclaré,
        // pas de signal de choc exploitable" narrative instead of
        // fabricating a multi-direction impact.

        // Persist the deterministic narrative first so the report has
        // structured story / indicators / reasons even if the LLM call
        // never completes. Mirrors the real-time path.
        var jsonOpts = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        ev.SynthesisText = AccidentNarrativeBuilder.BuildSynthesisText(candidate);
        ev.StoryJson = JsonSerializer.Serialize(AccidentNarrativeBuilder.BuildStory(candidate), jsonOpts);
        ev.ReasonsJson = JsonSerializer.Serialize(AccidentNarrativeBuilder.BuildReasons(candidate), jsonOpts);
        ev.IndicatorsJson = JsonSerializer.Serialize(AccidentNarrativeBuilder.BuildIndicators(candidate), jsonOpts);
        // Lift the confidence from the human-set 100 anchor to a sensor-
        // derived score so the badge reflects what the data actually shows.
        ev.Confidence = ComputeConfidence(candidate);
        ev.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ManualAccidentEnrichmentService: deterministic narrative built for accident {Id} (mag {Mag:F0}, kphBef {KphBef:F0}, kphAft {KphAft:F0})",
            accidentEventId, candidate.Mag, candidate.KphBef, candidate.KphAft);

        // Best-effort LLM polish. Same guardrails as the detection path —
        // any failure leaves the deterministic prose intact.
        try
        {
            var location = new GeocodedLocation(
                ev.LocationCommune, ev.LocationGovernorate, ev.LocationRoadType);
            var vehicleLabel = ev.VehicleLabel ?? $"Véhicule #{ev.VehicleId}";

            var llm = await narrativeService.TryGenerateAsync(
                context, candidate, vehicleLabel, location, ct);

            if (llm != null)
            {
                ev.SynthesisText = llm.SynthesisText;
                ev.ReasonsJson = JsonSerializer.Serialize(
                    llm.Reasons.Select(r => new { title = r.Title, text = r.Text }),
                    jsonOpts);
                ev.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "ManualAccidentEnrichmentService: LLM narrative accepted for accident {Id}",
                    accidentEventId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ManualAccidentEnrichmentService: LLM enrichment failed for accident {Id} — keeping deterministic narrative",
                accidentEventId);
        }
    }

    /// <summary>
    /// Reconstructs an <see cref="AccidentCandidate"/> from a list of raw
    /// frames around the user-entered incident time. Mirrors the V7
    /// detector's SQL but executes in-memory so it works on manual reports
    /// that may not actually pass the V7 thresholds (e.g. the device was
    /// offline at impact).
    ///
    /// <para>Recentering: the MEMS peak within ±2 min of the anchor is
    /// promoted to <c>RecordedAt</c> — matches the frontend A enrichment
    /// behaviour so the report on screen and the persisted narrative agree
    /// on the impact frame even when the user typed an approximate time.</para>
    ///
    /// <para>Also computes the diversifying signals the narrative builder
    /// consumes: sustained abnormal tilt after stop (rollover indicator),
    /// second-shock magnitude (rebound / tonneau indicator), and tow-event
    /// presence (vehicle picked up by a flatbed afterwards).</para>
    /// </summary>
    private static async Task<AccidentCandidate> BuildCandidateAsync(
        GisDbContext context,
        List<RawFrame> frames,
        int deviceId,
        DateTime userAnchor,
        int accidentEventId,
        CancellationToken ct)
    {
        // 1. MEMS peak within ±2 min of the user-entered time. We fall back
        //    to the closest-in-time frame if no MEMS data is present.
        var wideStart = userAnchor.AddMinutes(-2);
        var wideEnd = userAnchor.AddMinutes(+2);

        RawFrame? peak = null;
        double peakMag = 0;
        int peakAx = 0, peakAy = 0, peakAz = 0;

        foreach (var f in frames)
        {
            if (f.RecordedAt < wideStart || f.RecordedAt > wideEnd) continue;
            if (f.MemsX == null || f.MemsY == null || f.MemsZ == null) continue;

            var x = Clamp(f.MemsX.Value);
            var y = Clamp(f.MemsY.Value);
            var z = Clamp(f.MemsZ.Value);
            var m = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);

            if (m > peakMag)
            {
                peakMag = m;
                peak = f;
                peakAx = Math.Abs(x);
                peakAy = Math.Abs(y);
                peakAz = Math.Abs(z);
            }
        }

        // 2. Anchor — peak if we have one, otherwise the closest frame to
        //    the user-entered time (so we still get meaningful before/after
        //    windows even when no MEMS evidence is present).
        var anchor = peak?.RecordedAt
            ?? frames.OrderBy(f => Math.Abs((f.RecordedAt - userAnchor).TotalSeconds)).First().RecordedAt;
        var anchorFrame = peak
            ?? frames.OrderBy(f => Math.Abs((f.RecordedAt - userAnchor).TotalSeconds)).First();

        // 3. Cruise speed: MAX in [-5 min ; -30 s] (matches V7 SQL).
        var befStart = anchor.AddMinutes(-5);
        var befEnd = anchor.AddSeconds(-30);
        var befFrames = frames
            .Where(f => f.RecordedAt >= befStart && f.RecordedAt <= befEnd)
            .ToList();
        var kphBef = befFrames.Count > 0 ? befFrames.Max(f => f.SpeedKph) : 0;
        var nMovBef = befFrames.Count(f => f.SpeedKph >= 10);

        // 4. Post-event: MAX in [+2 min ; +10 min] (matches V7 SQL).
        var aftStart = anchor.AddMinutes(+2);
        var aftEnd = anchor.AddMinutes(+10);
        var aftFrames = frames
            .Where(f => f.RecordedAt >= aftStart && f.RecordedAt <= aftEnd)
            .ToList();
        var kphAft = aftFrames.Count > 0 ? aftFrames.Max(f => f.SpeedKph) : 0;
        var nAft = aftFrames.Count;

        // 5. Second shock: any other peak ≥ 100 magnitude separated by ≥ 5 s
        //    from the primary peak, within ±90 s.
        double secondShockMag = 0;
        if (peak != null)
        {
            var secondStart = anchor.AddSeconds(-90);
            var secondEnd = anchor.AddSeconds(+90);
            foreach (var f in frames)
            {
                if (f.RecordedAt < secondStart || f.RecordedAt > secondEnd) continue;
                if (Math.Abs((f.RecordedAt - anchor).TotalSeconds) < 5) continue;
                if (f.MemsX == null || f.MemsY == null || f.MemsZ == null) continue;
                var x = Clamp(f.MemsX.Value);
                var y = Clamp(f.MemsY.Value);
                var z = Clamp(f.MemsZ.Value);
                var m = Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
                if (m >= 100 && m > secondShockMag) secondShockMag = m;
            }
        }

        // 6. Sustained abnormal tilt after stop: longest contiguous run of
        //    frames where |Z| ≥ 90, starting AFTER the vehicle reached ≤ 5
        //    km/h. Returned in MINUTES (1 = one frame, since frames are
        //    typically 30-60 s apart on a parked vehicle).
        int tiltDurationMin = 0;
        if (peak != null)
        {
            DateTime? stopT = null;
            foreach (var f in frames.Where(f => f.RecordedAt >= anchor).OrderBy(f => f.RecordedAt))
            {
                if (f.SpeedKph <= 5) { stopT = f.RecordedAt; break; }
            }
            if (stopT != null)
            {
                DateTime? runStart = null;
                DateTime? runEnd = null;
                int bestMin = 0;
                foreach (var f in frames.Where(f => f.RecordedAt >= stopT).OrderBy(f => f.RecordedAt))
                {
                    if (f.MemsZ == null) continue;
                    var zAbs = Math.Abs(Clamp(f.MemsZ.Value));
                    if (zAbs >= 90)
                    {
                        runStart ??= f.RecordedAt;
                        runEnd = f.RecordedAt;
                    }
                    else if (runStart != null && runEnd != null)
                    {
                        var mins = (int)Math.Round((runEnd.Value - runStart.Value).TotalMinutes);
                        if (mins > bestMin) bestMin = mins;
                        runStart = null;
                        runEnd = null;
                    }
                }
                if (runStart != null && runEnd != null)
                {
                    var mins = (int)Math.Round((runEnd.Value - runStart.Value).TotalMinutes);
                    if (mins > bestMin) bestMin = mins;
                }
                tiltDurationMin = bestMin;
            }
        }

        // 7. Tow event linked to this accident — AccidentTowMonitoringService
        //    stamps TowDetectedAt on the accident itself once a flatbed
        //    pickup is detected (may still be null on freshly created
        //    manual accidents; will be true when the user re-opens an old
        //    accident or when the row is re-enriched later).
        var hasTow = await context.AccidentEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(e => e.Id == accidentEventId && e.TowDetectedAt != null, ct);

        return new AccidentCandidate
        {
            DeviceId = deviceId,
            RecordedAt = anchor,
            Latitude = anchorFrame.Latitude,
            Longitude = anchorFrame.Longitude,
            SpeedKph = anchorFrame.SpeedKph,
            Ax = peakAx,
            Ay = peakAy,
            Az = peakAz,
            Mag = peakMag,
            NPrior7d = 0, // Not computed for manual reports — irrelevant signal
            KphBef = kphBef,
            NMovBef = nMovBef,
            KphAft = kphAft,
            NAft = nAft,
            TiltDurationMin = tiltDurationMin,
            SecondShockMag = secondShockMag,
            HasTow = hasTow,
        };
    }

    /// <summary>
    /// Confidence score that mirrors the frontend A-enrichment logic so
    /// the badge on the report matches what the user sees in the browser:
    /// stronger MEMS evidence and a clean post-event stop both lift the
    /// score; a manual accident with no MEMS signal stays at the floor.
    /// </summary>
    private static int ComputeConfidence(AccidentCandidate c)
    {
        var score = 55;
        if (c.Mag >= 180) score += 20;
        else if (c.Mag >= 150) score += 15;
        else if (c.Mag >= 120) score += 10;
        else if (c.Mag >= 80) score += 5;

        if (c.KphAft <= 1 && c.NAft >= 3) score += 10;
        if (c.NMovBef >= 5 && c.KphBef >= 30) score += 5;

        return Math.Clamp(score, 0, 100);
    }

    private static int Clamp(short v) => Math.Max(-128, Math.Min(127, (int)v));

    /// <summary>Flat row pulled out of <c>gps_positions</c> for candidate computation.</summary>
    private sealed class RawFrame
    {
        public DateTime RecordedAt { get; init; }
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public double SpeedKph { get; init; }
        public short? MemsX { get; init; }
        public short? MemsY { get; init; }
        public short? MemsZ { get; init; }
    }
}
