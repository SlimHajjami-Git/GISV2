using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Proactive battery-health watcher — runs every 1 hour and evaluates
/// four signals against the historical <c>power_voltage</c> readings of
/// each NEMS L (<c>protocol_type = 'gps_type_1'</c>) device. Goal: catch
/// a dying vehicle battery <b>days before</b> the device goes silent,
/// rather than waiting for a brown-out.
///
/// <para>The thresholds below come from the lead-acid 12 V industry
/// reference (Midtronics, Optima, AAMCO, Toyota service manuals):</para>
///
/// <list type="bullet">
///   <item><description><b>12.6–12.8 V</b> = healthy resting</description></item>
///   <item><description><b>12.4 V</b> = early aging signal (sulfation
///     starting)</description></item>
///   <item><description><b>12.0 V</b> = significantly discharged, cranking
///     compromised</description></item>
///   <item><description><b>13.5–14.4 V</b> = expected charging voltage with
///     alternator running</description></item>
/// </list>
///
/// <para>The four signals:</para>
/// <list type="number">
///   <item><description><b>Resting decline</b> — recent J-3 average dropped
///     ≥ 0.6 V vs the J-30/J-7 baseline AND is now ≤ 12.4 V. Catches
///     gradual degradation.</description></item>
///   <item><description><b>Resting critical</b> — recent J-3 average ≤
///     12.0 V. Catches batteries already in deep discharge.</description></item>
///   <item><description><b>Charging anomaly</b> — when ignition_on=true
///     and speed > 5 km/h, J-3 average voltage stays under 13.0 V.
///     Indicates failing alternator/regulator (battery not being
///     recharged on each trip).</description></item>
///   <item><description><b>Saturated silence</b> — the firmware-saturation
///     escape hatch: <c>power_voltage</c> byte stayed pinned at 0x2B
///     (≈12.9 V) on 100 % of frames for 14 days AND device has been
///     silent for 90+ minutes. The post-mortem signal we'd send for the
///     236 TU 6532 case where <c>BatteryMonitoringService</c> couldn't
///     see the brown-out coming.</description></item>
/// </list>
///
/// <para><b>Cooldown</b>: 48 hours via <c>GpsDevice.LastVoltageHealthAlertAt</c>.
/// The signal that fires first wins and silences the others until the
/// cooldown expires — we don't want a degrading battery to fire 4 alerts
/// in 5 minutes when several thresholds simultaneously trip.</para>
///
/// <para><b>Fan-out</b>: <see cref="BatteryHealthAlertEvent"/> via
/// MediatR; <see cref="BatteryHealthAlertHandler"/> resolves the company
/// admins and routes through the standard <c>NotificationService</c>
/// (in-app + SignalR + FCM).</para>
/// </summary>
public class VoltageHealthMonitoringService : BackgroundService
{
    // Industry reference points, expressed in volts.
    // The empirical RAW→V factor (0.3) is duplicated from the Rust
    // ingest's redis_cache.rs for parity with the displayed battery%.
    private const double RawToVoltsFactor = 0.3;
    private const double RestingHealthyV  = 12.6;
    private const double RestingAgingV    = 12.4;  // signal #1 ceiling
    private const double RestingCriticalV = 12.0;  // signal #2 trigger
    private const double ChargingMinHealthyV = 13.0; // signal #3 floor
    private const double DeclineDeltaV    = 0.6;   // signal #1 minimum drop

    // Statistical floors — under these sample counts the signal is too
    // noisy to publish.
    private const int MinBaselineFrames = 100;
    private const int MinRecentFrames   = 50;
    private const int MinChargingFrames = 30;

    // Trimming ratio for the noise-robust mean: drop the top 10 % and
    // bottom 10 % of values before averaging. Makes the signal
    // resilient to occasional sensor glitches (a single 0x05 spike in
    // a stream of 0x2B) without losing the underlying trend. Empirical
    // — at this trim level a 50-frame window can absorb 5 outliers on
    // each tail before any of them touches the result.
    private const double TrimRatio = 0.10;

    // Saturated-silence parameters.
    //
    // The silence threshold is intentionally LARGE (24h) — a vehicle in
    // an underground parking, a tunnel, or a weak-coverage zone can stay
    // silent for several hours without any battery issue, and we don't
    // want to push false alarms in those normal cases. A real dying
    // battery, by contrast, never comes back: 24h+ of silence on a device
    // that previously emitted 48 frames/day is statistically near-certain
    // to indicate a brown-out, not a parking situation.
    private const int SaturationByteValue = 43;   // 0x2B (≈12.9 V)
    private const int SaturationLookbackDays = 14;
    private const int SaturationSilenceMinutes = 24 * 60; // 24 hours
    // Tolerance for the saturation invariant: a few outlier frames
    // (cranking dips, GSM hiccups) shouldn't disqualify a 14-day stream
    // that is otherwise pinned at the firmware ceiling. Require ≥95 %
    // of frames at the saturation byte rather than 100 %.
    private const double SaturationDominanceRatio = 0.95;

    // Lookback windows.
    private const int BaselineDaysOld = 30;
    private const int BaselineDaysCutoff = 7;
    private const int RecentDays = 3;

    // Service cadence.
    private const int CycleMinutes = 60;
    private const int StartupDelayMinutes = 3;
    private const int CooldownHours = 48;
    private const string NemsLProtocol = "gps_type_1";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VoltageHealthMonitoringService> _logger;

    public VoltageHealthMonitoringService(
        IServiceProvider serviceProvider,
        ILogger<VoltageHealthMonitoringService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Stagger after BatteryMonitoringService so we don't both hit the
        // DB on the same exact second at cold boot.
        try { await Task.Delay(TimeSpan.FromMinutes(StartupDelayMinutes), ct); }
        catch (TaskCanceledException) { return; }

        _logger.LogInformation(
            "VoltageHealthMonitoringService started (cycle={CycleMin}min, cooldown={Cooldown}h)",
            CycleMinutes, CooldownHours);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "VoltageHealthMonitoringService cycle failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(CycleMinutes), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var now = DateTime.UtcNow;
        var cooldownCutoff = now.AddHours(-CooldownHours);

        // Same candidate filter as BatteryMonitoringService — only NEMS L
        // devices assigned to a vehicle, past the per-device cooldown.
        var candidates = await context.GpsDevices
            .IgnoreQueryFilters()
            .Include(d => d.Vehicle)
            .Where(d => d.ProtocolType == NemsLProtocol
                     && d.Vehicle != null
                     && (d.LastVoltageHealthAlertAt == null
                         || d.LastVoltageHealthAlertAt < cooldownCutoff))
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        _logger.LogDebug(
            "VoltageHealthMonitoringService: scanning {Count} NEMS L device(s)",
            candidates.Count);

        int flagged = 0;
        foreach (var device in candidates)
        {
            try
            {
                var hit = await EvaluateAsync(context, device, now, ct);
                if (hit == null) continue;

                device.LastVoltageHealthAlertAt = now;
                device.UpdatedAt = now;
                await context.SaveChangesAsync(ct);
                flagged++;

                _logger.LogInformation(
                    "VoltageHealth: {Signal} ({Severity}) on device {DeviceId} ({Plate}) — observed={Observed:F2}V baseline={Baseline:F2}V",
                    hit.SignalKind, hit.Severity, device.Id,
                    VehicleLabel(device.Vehicle) ?? "?",
                    hit.VoltageObservedV ?? 0, hit.VoltageBaselineV ?? 0);

                await mediator.Publish(new BatteryHealthAlertEvent(
                    CompanyId: device.CompanyId,
                    DeviceId: device.Id,
                    VehicleId: device.Vehicle?.Id,
                    VehicleName: VehicleLabel(device.Vehicle),
                    SignalKind: hit.SignalKind,
                    Severity: hit.Severity,
                    Description: hit.Description,
                    VoltageObservedV: hit.VoltageObservedV,
                    VoltageBaselineV: hit.VoltageBaselineV,
                    DetectedAt: now
                ), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "VoltageHealthMonitoringService: failed to evaluate device {DeviceId}",
                    device.Id);
            }
        }

        if (flagged > 0)
        {
            _logger.LogInformation(
                "VoltageHealthMonitoringService: flagged {Flagged}/{Total} device(s) this cycle",
                flagged, candidates.Count);
        }
    }

    /// <summary>
    /// Tries the four signals in priority order and returns the first
    /// that trips, or null if the device is healthy. Priority chain
    /// matters because <c>resting_voltage_critical</c> dominates
    /// <c>resting_voltage_decline</c>: when both fire we want the
    /// stronger user-facing wording.
    /// </summary>
    private static async Task<HealthHit?> EvaluateAsync(
        GisDbContext context,
        GpsDevice device,
        DateTime now,
        CancellationToken ct)
    {
        var deviceId = device.Id;

        // ── Signal 1 + 2: Resting voltage analysis ──────────────────────────
        // Only "ignition off" frames count — we want batteries at rest.
        // This is the Open Circuit Voltage proxy; engine-running readings
        // would be biased by the alternator output.
        var baselineStart = now.AddDays(-BaselineDaysOld);
        var baselineEnd = now.AddDays(-BaselineDaysCutoff);
        var recentStart = now.AddDays(-RecentDays);

        var baseline = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= baselineStart
                     && p.RecordedAt < baselineEnd
                     && p.IgnitionOn == false
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .Select(p => p.PowerVoltage!.Value)
            .ToListAsync(ct);

        var recent = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= recentStart
                     && p.IgnitionOn == false
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .Select(p => p.PowerVoltage!.Value)
            .ToListAsync(ct);

        if (recent.Count >= MinRecentFrames)
        {
            var avgRecentV = TrimmedMean(recent) * RawToVoltsFactor;

            // Signal 2 (critical) always wins — its threshold is reached
            // independently of the baseline.
            if (avgRecentV <= RestingCriticalV)
            {
                return new HealthHit(
                    SignalKind: "resting_voltage_critical",
                    Severity: "critical",
                    Description: $"Tension repos moyenne (3j): {avgRecentV:F2} V — sous seuil 12.0 V",
                    VoltageObservedV: avgRecentV,
                    VoltageBaselineV: null);
            }

            // Signal 1 needs a meaningful baseline to compare against.
            if (baseline.Count >= MinBaselineFrames && avgRecentV <= RestingAgingV)
            {
                var avgBaselineV = TrimmedMean(baseline) * RawToVoltsFactor;
                if ((avgBaselineV - avgRecentV) >= DeclineDeltaV)
                {
                    return new HealthHit(
                        SignalKind: "resting_voltage_decline",
                        Severity: "warning",
                        Description: $"Déclin tension repos: {avgBaselineV:F2} V → {avgRecentV:F2} V",
                        VoltageObservedV: avgRecentV,
                        VoltageBaselineV: avgBaselineV);
                }
            }
        }

        // ── Signal 3: Charging voltage anomaly ──────────────────────────────
        // Only frames where the alternator should be charging — engine on
        // and the vehicle moving (so we know it's not idling at the lights
        // with electrics drawing the battery down).
        var charging = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= recentStart
                     && p.IgnitionOn == true
                     && p.SpeedKph > 5
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .Select(p => p.PowerVoltage!.Value)
            .ToListAsync(ct);

        if (charging.Count >= MinChargingFrames)
        {
            var avgChargingV = TrimmedMean(charging) * RawToVoltsFactor;
            if (avgChargingV < ChargingMinHealthyV)
            {
                return new HealthHit(
                    SignalKind: "charging_voltage_low",
                    Severity: "warning",
                    Description: $"Tension en charge moyenne: {avgChargingV:F2} V — sous seuil 13.0 V",
                    VoltageObservedV: avgChargingV,
                    VoltageBaselineV: null);
            }
        }

        // ── Signal 4: Saturated silence (firmware safety net) ───────────────
        // For devices like 236 TU 6532 where the firmware reports a fixed
        // 0x2B regardless of true voltage, slow decline is invisible. The
        // post-mortem signature is: 100 % of frames at exactly 43 over a
        // long window AND device now silent. This is opportunistic — it
        // fires after the brown-out, but it still beats discovering the
        // panne on next-day visual inspection.
        var saturationStart = now.AddDays(-SaturationLookbackDays);
        var saturationCounts = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= saturationStart
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .GroupBy(p => p.PowerVoltage == SaturationByteValue)
            .Select(g => new { IsSaturated = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var totalFrames = saturationCounts.Sum(c => c.Count);
        var saturatedFrames = saturationCounts.FirstOrDefault(c => c.IsSaturated)?.Count ?? 0;

        if (totalFrames >= MinBaselineFrames
            && saturatedFrames >= (int)(totalFrames * SaturationDominanceRatio))
        {
            // ≥95 % of frames in the last 14d sat at the saturation byte.
            // That alone isn't worth alerting (most healthy parked
            // vehicles look like that on this firmware). Pair it with
            // current silence to make it actionable. The 95 % tolerance
            // absorbs occasional cranking dips and GSM-induced glitches
            // without losing the underlying "firmware ceiling" signal.
            var lastComm = device.LastCommunication;
            if (lastComm.HasValue)
            {
                var silenceMin = (now - lastComm.Value).TotalMinutes;
                if (silenceMin >= SaturationSilenceMinutes)
                {
                    var silenceHours = silenceMin / 60.0;
                    return new HealthHit(
                        SignalKind: "saturated_silence",
                        Severity: "warning",
                        Description: $"Tension figée à 12.9 V sur 14j + silence prolongé {silenceHours:F0}h",
                        VoltageObservedV: SaturationByteValue * RawToVoltsFactor,
                        VoltageBaselineV: null);
                }
            }
        }

        return null;
    }

    private static string? VehicleLabel(Vehicle? vehicle)
    {
        if (vehicle == null) return null;
        if (!string.IsNullOrWhiteSpace(vehicle.Plate)) return vehicle.Plate;
        if (!string.IsNullOrWhiteSpace(vehicle.Name)) return vehicle.Name;
        return null;
    }

    /// <summary>
    /// Noise-robust central tendency: sort the values, drop the top
    /// <see cref="TrimRatio"/> and bottom <see cref="TrimRatio"/> as
    /// likely outliers, then average the remaining middle. A single
    /// frame at byte=5 (sensor glitch) inside a long stream of byte=43
    /// can't shift the result by more than a fraction of a unit.
    ///
    /// <para>Falls back to plain average when the sample is too small
    /// to trim safely (under 10 readings the trim would be 1 from each
    /// tail, which is too aggressive on a tiny window).</para>
    /// </summary>
    private static double TrimmedMean(IReadOnlyList<int> values)
    {
        if (values.Count == 0) return 0;
        if (values.Count < 10) return values.Average();

        var sorted = values.OrderBy(v => v).ToArray();
        int trim = (int)(sorted.Length * TrimRatio);
        if (trim <= 0) return sorted.Average();

        // Average the middle slice only — Span avoids one allocation
        // on the hot path (this runs every cycle for every device).
        var middle = sorted.AsSpan(trim, sorted.Length - 2 * trim);
        long sum = 0;
        foreach (var v in middle) sum += v;
        return (double)sum / middle.Length;
    }

    /// <summary>
    /// Internal carrier between the evaluator and the publishing loop.
    /// Kept private — outside callers should consume <see cref="BatteryHealthAlertEvent"/>.
    /// </summary>
    private sealed record HealthHit(
        string SignalKind,
        string Severity,
        string Description,
        double? VoltageObservedV,
        double? VoltageBaselineV);
}
