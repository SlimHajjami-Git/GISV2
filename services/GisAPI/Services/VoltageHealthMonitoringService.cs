using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Battery-health watcher for NEMS L (<c>protocol_type = 'gps_type_1'</c>)
/// devices. Runs every hour and looks at the <i>battery only</i> — we do
/// NOT chase the alternator. Why: an alternator-low reading only matters
/// because it implies the battery doesn't recharge, but our firmware
/// saturates at byte 0x2B (≈12.9 V) so we can't tell a healthy 13.8 V
/// charging system from a weak 12.9 V one. Trying to infer the alternator
/// state from a saturated sensor produced 49 false positives on a fleet
/// of mostly-new vehicles — completely unusable.
///
/// <para>The two signals we keep are <b>direct battery</b> indicators
/// that the firmware CAN expose despite the saturation:</para>
///
/// <list type="number">
///   <item><description><b>resting_voltage_low</b> — at rest (ignition
///     off), enough recent frames report voltage at or below 12.0 V.
///     The firmware happily reports byte ≤ 40 when the real voltage is
///     that low, so this signal is reliable. A 12V lead-acid battery at
///     12.0 V at rest is at ~50 % SoC and starting compromised; below
///     that it's effectively unable to start the engine cold.</description></item>
///   <item><description><b>saturated_silence</b> — firmware-blind safety
///     net for the brutal-death case: byte stays pinned at 0x2B for 14
///     consecutive days (no variation seen) AND the device has now been
///     silent for 24 h+. A parking, tunnel, or coverage hole doesn't last
///     a full day, so this is a strong signal the boîtier is actually
///     dead — the pattern we'd have wanted for 236 TU 6532.</description></item>
/// </list>
///
/// <para><b>What we deliberately do NOT do</b>:</para>
/// <list type="bullet">
///   <item><description>No "alternator suspect" / "charging voltage low"
///     signal. The firmware saturation overlaps with the textbook
///     alternator target (13.5 V+) so we cannot distinguish them.</description></item>
///   <item><description>No "resting voltage decline vs baseline" signal.
///     A 14-day baseline computed from a saturated firmware is itself
///     12.9 V, so the delta-from-baseline check never fires meaningfully
///     — it just adds noise for no diagnostic value.</description></item>
/// </list>
///
/// <para><b>Cooldown</b>: 48 h via <c>GpsDevice.LastVoltageHealthAlertAt</c>.
/// <b>Fan-out</b>: <see cref="BatteryHealthAlertEvent"/> via MediatR.</para>
/// </summary>
public class VoltageHealthMonitoringService : BackgroundService
{
    // Raw byte → volts conversion (matches the Rust ingest's 0.3 factor
    // in redis_cache.rs).
    private const double RawToVoltsFactor = 0.3;

    // Battery-only thresholds. 12.0 V is the lead-acid industry
    // "compromised / cannot reliably crank" point — Midtronics, Optima,
    // Toyota service manuals all cite it. Anything lower is severe.
    private const int RestingLowByteThreshold = 40;        // 40 * 0.3 = 12.0 V
    private const int MinLowFramesForAlert = 10;           // need at least 10 low readings
    private const double LowFramesMinShare = 0.20;         // and they must be ≥20% of recent

    // Statistical floor — don't fire on a device that has barely streamed.
    private const int MinRestingFrames = 50;

    // Saturated-silence parameters. The 24 h silence threshold is
    // deliberately large to filter out parking / tunnel / poor-coverage
    // scenarios; a real dying battery never reconnects, while a parking
    // almost never lasts a full day.
    private const int SaturationByteValue = 43;            // 0x2B (≈12.9 V)
    private const int SaturationLookbackDays = 14;
    private const int SaturationSilenceMinutes = 24 * 60;
    private const double SaturationDominanceRatio = 0.95;

    // Recent-window for the resting-low signal.
    private const int RecentDays = 7;

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

        var candidates = await context.GpsDevices
            .IgnoreQueryFilters()
            .Include(d => d.Vehicle)
            .Where(d => d.ProtocolType == NemsLProtocol
                     && d.Vehicle != null
                     && !d.Vehicle.IsImmobilized
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
                    "VoltageHealth: {Signal} ({Severity}) on device {DeviceId} ({Plate}) — observed={Observed:F2}V",
                    hit.SignalKind, hit.Severity, device.Id,
                    VehicleLabel(device.Vehicle) ?? "?",
                    hit.VoltageObservedV ?? 0);

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
    /// Tries the two signals in priority order. Resting-low has priority
    /// because a battery already at ≤ 12.0 V at rest is a direct,
    /// actionable battery fault. Saturated-silence is the firmware-blind
    /// safety net and only matters when the device is also offline.
    /// </summary>
    private static async Task<HealthHit?> EvaluateAsync(
        GisDbContext context,
        GpsDevice device,
        DateTime now,
        CancellationToken ct)
    {
        var deviceId = device.Id;
        var recentStart = now.AddDays(-RecentDays);

        // ── Signal A: Resting voltage low ───────────────────────────────────
        // Count BOTH the total resting frames and the subset reporting
        // ≤ 12.0 V (byte ≤ 40). The firmware saturates at byte 43 but it
        // CAN drop below the ceiling when the battery genuinely is below
        // 12.9 V — so a non-trivial fraction of byte ≤ 40 frames at rest
        // is a direct, actionable battery signal.
        var counts = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= recentStart
                     && p.IgnitionOn == false
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .GroupBy(p => p.PowerVoltage!.Value <= RestingLowByteThreshold)
            .Select(g => new { IsLow = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var totalRest = counts.Sum(c => c.Count);
        var lowRest = counts.FirstOrDefault(c => c.IsLow)?.Count ?? 0;

        if (totalRest >= MinRestingFrames
            && lowRest >= MinLowFramesForAlert
            && (double)lowRest / totalRest >= LowFramesMinShare)
        {
            // We have a real "battery at rest dips below 12.0 V" pattern.
            // Pick the median low-frame voltage for the description so the
            // operator sees how bad it really is, not just the threshold.
            var lowVoltagesRaw = await context.GpsPositions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.DeviceId == deviceId
                         && p.RecordedAt >= recentStart
                         && p.IgnitionOn == false
                         && p.PowerVoltage.HasValue
                         && p.PowerVoltage.Value > 0
                         && p.PowerVoltage.Value <= RestingLowByteThreshold)
                .Select(p => p.PowerVoltage!.Value)
                .ToListAsync(ct);

            var medianLowV = Median(lowVoltagesRaw) * RawToVoltsFactor;
            var sharePct = 100.0 * lowRest / totalRest;

            return new HealthHit(
                SignalKind: "resting_voltage_low",
                Severity: "critical",
                Description:
                    $"Batterie au repos: {lowRest}/{totalRest} trames sous 12 V " +
                    $"({sharePct:F0}%), médiane {medianLowV:F2} V",
                VoltageObservedV: medianLowV,
                VoltageBaselineV: null);
        }

        // ── Signal B: Saturated silence ─────────────────────────────────────
        // For devices like 236 TU 6532 where the firmware is pinned at
        // 12.9 V and we never saw a real pre-decline signal: combine the
        // saturation pattern over 14 days with a 24 h+ silence to detect
        // the brutal-death case.
        var saturationStart = now.AddDays(-SaturationLookbackDays);
        var satCounts = await context.GpsPositions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= saturationStart
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .GroupBy(p => p.PowerVoltage == SaturationByteValue)
            .Select(g => new { IsSaturated = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var totalSat = satCounts.Sum(c => c.Count);
        var saturatedFrames = satCounts.FirstOrDefault(c => c.IsSaturated)?.Count ?? 0;

        if (totalSat >= MinRestingFrames
            && saturatedFrames >= (int)(totalSat * SaturationDominanceRatio))
        {
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
    /// Median of an unsorted integer list. Robust to spike outliers in a
    /// way that <c>Average()</c> isn't — a single byte=5 glitch can't
    /// shift a median computed over dozens of byte=38 readings.
    /// </summary>
    private static double Median(IReadOnlyList<int> values)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2.0
            : sorted[mid];
    }

    /// <summary>
    /// Internal carrier between the evaluator and the publishing loop.
    /// </summary>
    private sealed record HealthHit(
        string SignalKind,
        string Severity,
        string Description,
        double? VoltageObservedV,
        double? VoltageBaselineV);
}
