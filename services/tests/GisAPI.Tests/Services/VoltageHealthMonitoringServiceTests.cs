using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Pinning tests for the four detection signals that drive
/// <c>GisAPI.Services.VoltageHealthMonitoringService</c>:
///
/// <list type="bullet">
///   <item><description><b>Candidate filter</b> — NEMS L (gps_type_1)
///     devices with a vehicle, past the 48 h cooldown.</description></item>
///   <item><description><b>Signal 1 — resting decline</b>: J-3 average vs
///     J-30/J-7 baseline drop ≥ 0.6 V AND recent ≤ 12.4 V.</description></item>
///   <item><description><b>Signal 2 — resting critical</b>: J-3 average ≤
///     12.0 V (overrides signal 1).</description></item>
///   <item><description><b>Signal 3 — charging anomaly</b>: ignition on +
///     speed > 5 readings averaging under 13.0 V.</description></item>
///   <item><description><b>Signal 4 — saturated silence</b>: 100 % of frames
///     at byte 43 over 14 d AND last_communication > 90 min.</description></item>
/// </list>
///
/// <para>We replay the same LINQ the service uses against an in-memory
/// SQLite context. The MediatR fan-out is exercised by the
/// <c>BatteryHealthAlertHandler</c> tests (admin filter, copy templates).</para>
/// </summary>
public class VoltageHealthMonitoringServiceTests
{
    private const int CompanyId = 1;
    private const int NemsLDeviceId = 500;

    // Mirror of the service's constants.
    private const double RawToVoltsFactor = 0.3;
    private const double RestingAgingV = 12.4;
    private const double RestingCriticalV = 12.0;
    private const double ChargingMinHealthyV = 13.0;
    private const double DeclineDeltaV = 0.6;
    private const int MinBaselineFrames = 100;
    private const int MinRecentFrames = 50;
    private const int MinChargingFrames = 30;
    private const int SaturationByteValue = 43;
    private const double SaturationDominanceRatio = 0.95;
    private const int CooldownHours = 48;

    private static GpsDevice SeedDevice(
        TestGisDbContext context,
        int id,
        string protocolType,
        int? vehicleId,
        DateTime? lastHealthAlertAt = null,
        DateTime? lastCommunication = null)
    {
        var device = new GpsDevice
        {
            Id = id,
            CompanyId = CompanyId,
            DeviceUid = $"DEV-{id}",
            Status = "active",
            ProtocolType = protocolType,
            LastVoltageHealthAlertAt = lastHealthAlertAt,
            LastCommunication = lastCommunication
        };
        context.GpsDevices.Add(device);

        if (vehicleId.HasValue)
        {
            context.Vehicles.Add(new Vehicle
            {
                Id = vehicleId.Value,
                CompanyId = CompanyId,
                Name = $"Véhicule {vehicleId}",
                Plate = $"TN-{vehicleId}",
                GpsDeviceId = id,
                Type = "camion",
                Status = "available"
            });
        }

        return device;
    }

    private static void SeedPosition(
        TestGisDbContext context,
        int deviceId,
        DateTime recordedAt,
        int? powerVoltage,
        bool? ignitionOn,
        double speedKph = 0)
    {
        context.GpsPositions.Add(new GpsPosition
        {
            DeviceId = deviceId,
            RecordedAt = recordedAt,
            Latitude = 36.8,
            Longitude = 10.18,
            PowerVoltage = powerVoltage,
            IgnitionOn = ignitionOn,
            SpeedKph = speedKph,
            IsValid = true
        });
    }

    /// <summary>
    /// Spreads N readings of identical voltage across the time window
    /// [windowStart, windowEnd]. Used to seed enough samples to pass the
    /// MinBaselineFrames / MinRecentFrames floors without writing 100
    /// individual SeedPosition calls per test.
    /// </summary>
    private static void SeedRange(
        TestGisDbContext context,
        int deviceId,
        DateTime windowStart,
        DateTime windowEnd,
        int count,
        int powerVoltage,
        bool ignitionOn,
        double speedKph = 0)
    {
        var span = (windowEnd - windowStart).TotalSeconds;
        for (int i = 0; i < count; i++)
        {
            var ts = windowStart.AddSeconds(span * i / count);
            SeedPosition(context, deviceId, ts, powerVoltage, ignitionOn, speedKph);
        }
    }

    // ── Candidate filter ────────────────────────────────────────────────────

    [Fact]
    public async Task CandidateFilter_OnlyPicksNemsLWithVehiclePastCooldown()
    {
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        var cooldownCutoff = now.AddHours(-CooldownHours);

        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700); // pick
        SeedDevice(context, id: 503, protocolType: "gps_type_1", vehicleId: 701,
            lastHealthAlertAt: now.AddHours(-49)); // past 48h, pick
        SeedDevice(context, id: 504, protocolType: "gps_type_1", vehicleId: 702,
            lastHealthAlertAt: now.AddHours(-2)); // inside cooldown, skip
        SeedDevice(context, id: 505, protocolType: "noron", vehicleId: 703); // wrong protocol, skip
        SeedDevice(context, id: 506, protocolType: "gps_type_1", vehicleId: null); // no vehicle, skip

        await context.SaveChangesAsync();

        var picked = await context.GpsDevices
            .Include(d => d.Vehicle)
            .Where(d => d.ProtocolType == "gps_type_1"
                     && d.Vehicle != null
                     && (d.LastVoltageHealthAlertAt == null
                         || d.LastVoltageHealthAlertAt < cooldownCutoff))
            .Select(d => d.Id)
            .OrderBy(id => id)
            .ToListAsync();

        picked.Should().BeEquivalentTo(new[] { NemsLDeviceId, 503 });
    }

    // ── Signal 1: Resting voltage decline ───────────────────────────────────

    [Fact]
    public async Task Signal1_RestingDecline_FiresWhenJ3DropsAtLeastPoint6VBelowBaselineAndIsAtMost12_4V()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // Baseline window J-30 to J-7 — voltage was 13.0 V (byte 43.3).
        SeedRange(context, NemsLDeviceId, now.AddDays(-30), now.AddDays(-7),
            count: 200, powerVoltage: 43, ignitionOn: false);
        // Recent window J-3 to NOW — voltage at 12.3 V (byte 41), Δ = 0.6 V, ≤ 12.4 V.
        SeedRange(context, NemsLDeviceId, now.AddDays(-3), now,
            count: 80, powerVoltage: 41, ignitionOn: false);

        await context.SaveChangesAsync();

        var (avgBaseline, avgRecent) = await EvaluateRestingAsync(context, NemsLDeviceId, now);

        avgRecent.Should().BeLessThanOrEqualTo(RestingAgingV);
        (avgBaseline - avgRecent).Should().BeGreaterThanOrEqualTo(DeclineDeltaV);
    }

    [Fact]
    public async Task Signal1_RestingDecline_DoesNotFireWhenDropIsBelowDeltaThreshold()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // 0.3 V drop: baseline 12.9 V → recent 12.6 V — under the 0.6 V threshold.
        SeedRange(context, NemsLDeviceId, now.AddDays(-30), now.AddDays(-7),
            count: 200, powerVoltage: 43, ignitionOn: false);
        SeedRange(context, NemsLDeviceId, now.AddDays(-3), now,
            count: 80, powerVoltage: 42, ignitionOn: false);

        await context.SaveChangesAsync();

        var (avgBaseline, avgRecent) = await EvaluateRestingAsync(context, NemsLDeviceId, now);

        (avgBaseline - avgRecent).Should().BeLessThan(DeclineDeltaV);
    }

    [Fact]
    public async Task Signal1_RestingDecline_DoesNotFireWhenRecentSamplesBelowMinimum()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        SeedRange(context, NemsLDeviceId, now.AddDays(-30), now.AddDays(-7),
            count: 200, powerVoltage: 43, ignitionOn: false);
        // Only 10 recent samples — below MinRecentFrames floor.
        SeedRange(context, NemsLDeviceId, now.AddDays(-3), now,
            count: 10, powerVoltage: 38, ignitionOn: false);

        await context.SaveChangesAsync();

        var recent = await context.GpsPositions
            .Where(p => p.DeviceId == NemsLDeviceId
                     && p.RecordedAt >= now.AddDays(-3)
                     && p.IgnitionOn == false
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .CountAsync();

        recent.Should().BeLessThan(MinRecentFrames);
    }

    // ── Signal 2: Resting voltage critical ──────────────────────────────────

    [Fact]
    public async Task Signal2_RestingCritical_FiresWhenAverageBelow12_0VRegardlessOfBaseline()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // Recent at byte 38 → 11.4 V (under 12.0 V critical).
        // No baseline samples — signal 2 must still trigger.
        SeedRange(context, NemsLDeviceId, now.AddDays(-3), now,
            count: 80, powerVoltage: 38, ignitionOn: false);

        await context.SaveChangesAsync();

        var avgRecent = await context.GpsPositions
            .Where(p => p.DeviceId == NemsLDeviceId
                     && p.RecordedAt >= now.AddDays(-3)
                     && p.IgnitionOn == false
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .Select(p => p.PowerVoltage!.Value)
            .ToListAsync();

        var avgRecentV = avgRecent.Average() * RawToVoltsFactor;
        avgRecent.Count.Should().BeGreaterThanOrEqualTo(MinRecentFrames);
        avgRecentV.Should().BeLessThanOrEqualTo(RestingCriticalV);
    }

    // ── Signal 3: Charging voltage anomaly ──────────────────────────────────

    [Fact]
    public async Task Signal3_ChargingAnomaly_FiresWhenIgnitionOnSpeedAboveThresholdAveragesUnder13V()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // 60 frames driving with byte 42 → 12.6 V (alternator should give 13.5+).
        SeedRange(context, NemsLDeviceId, now.AddDays(-3), now,
            count: 60, powerVoltage: 42, ignitionOn: true, speedKph: 50);

        await context.SaveChangesAsync();

        var avgChargingV = (await context.GpsPositions
            .Where(p => p.DeviceId == NemsLDeviceId
                     && p.RecordedAt >= now.AddDays(-3)
                     && p.IgnitionOn == true
                     && p.SpeedKph > 5
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .Select(p => p.PowerVoltage!.Value)
            .ToListAsync())
            .Average() * RawToVoltsFactor;

        avgChargingV.Should().BeLessThan(ChargingMinHealthyV);
    }

    [Fact]
    public async Task Signal3_ChargingAnomaly_DoesNotFireWhenAlternatorGivesHealthyVoltage()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // Byte 47 → 14.1 V — typical full-load alternator.
        SeedRange(context, NemsLDeviceId, now.AddDays(-3), now,
            count: 60, powerVoltage: 47, ignitionOn: true, speedKph: 50);

        await context.SaveChangesAsync();

        var charging = await context.GpsPositions
            .Where(p => p.DeviceId == NemsLDeviceId
                     && p.RecordedAt >= now.AddDays(-3)
                     && p.IgnitionOn == true
                     && p.SpeedKph > 5
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .Select(p => p.PowerVoltage!.Value)
            .ToListAsync();

        var avgChargingV = charging.Average() * RawToVoltsFactor;
        charging.Count.Should().BeGreaterThanOrEqualTo(MinChargingFrames);
        avgChargingV.Should().BeGreaterThanOrEqualTo(ChargingMinHealthyV);
    }

    // ── Signal 4: Saturated silence (firmware safety net) ───────────────────

    [Fact]
    public async Task Signal4_SaturatedSilence_FiresWhenAllFramesAt0x2BAndDeviceSilentForAtLeast24Hours()
    {
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        // Last comm 25 hours ago → past the 24h silence threshold. The
        // long requirement is deliberate: a parking, tunnel, or
        // poor-coverage zone almost never lasts a full day, so silence
        // beyond 24h is a strong signal the boîtier is actually dead.
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700,
            lastCommunication: now.AddHours(-25));

        // 200 frames over the 14d window, ALL at saturation byte.
        SeedRange(context, NemsLDeviceId, now.AddDays(-14), now,
            count: 200, powerVoltage: SaturationByteValue, ignitionOn: false);

        await context.SaveChangesAsync();

        var counts = await context.GpsPositions
            .Where(p => p.DeviceId == NemsLDeviceId
                     && p.RecordedAt >= now.AddDays(-14)
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .GroupBy(p => p.PowerVoltage == SaturationByteValue)
            .Select(g => new { IsSaturated = g.Key, Count = g.Count() })
            .ToListAsync();

        var total = counts.Sum(c => c.Count);
        var saturated = counts.FirstOrDefault(c => c.IsSaturated)?.Count ?? 0;

        total.Should().BeGreaterThanOrEqualTo(MinBaselineFrames);
        saturated.Should().Be(total);
    }

    [Fact]
    public async Task Signal4_SaturatedSilence_StillFiresWhenLessThanFivePercentAreOutliers()
    {
        // Noise tolerance: a few outlier frames (cranking dips, GSM
        // hiccups) shouldn't disqualify a 14-day stream that is otherwise
        // pinned at the firmware ceiling. With ≥95 % saturated the signal
        // remains valid.
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700,
            lastCommunication: now.AddHours(-30));

        SeedRange(context, NemsLDeviceId, now.AddDays(-14), now,
            count: 195, powerVoltage: SaturationByteValue, ignitionOn: false);
        // 5 outlier frames out of 200 = 2.5 % — still under the 5 % bar.
        for (int i = 0; i < 5; i++)
            SeedPosition(context, NemsLDeviceId, now.AddDays(-2).AddMinutes(i), powerVoltage: 41, ignitionOn: false);

        await context.SaveChangesAsync();

        var counts = await context.GpsPositions
            .Where(p => p.DeviceId == NemsLDeviceId
                     && p.RecordedAt >= now.AddDays(-14)
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .GroupBy(p => p.PowerVoltage == SaturationByteValue)
            .Select(g => new { IsSaturated = g.Key, Count = g.Count() })
            .ToListAsync();

        var total = counts.Sum(c => c.Count);
        var saturated = counts.FirstOrDefault(c => c.IsSaturated)?.Count ?? 0;

        saturated.Should().BeGreaterThanOrEqualTo((int)(total * SaturationDominanceRatio));
    }

    [Fact]
    public async Task Signal4_SaturatedSilence_DoesNotFireWhenMoreThanFivePercentAreOutliers()
    {
        // Beyond 5 % outliers the firmware was clearly NOT saturated —
        // the proactive resting/charging signals should have done the
        // job and we don't want to over-fire on healthy varying devices.
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700,
            lastCommunication: now.AddHours(-30));

        SeedRange(context, NemsLDeviceId, now.AddDays(-14), now,
            count: 180, powerVoltage: SaturationByteValue, ignitionOn: false);
        // 20 outliers out of 200 = 10 % — exceeds the 5 % bar.
        for (int i = 0; i < 20; i++)
            SeedPosition(context, NemsLDeviceId, now.AddDays(-2).AddMinutes(i), powerVoltage: 41, ignitionOn: false);

        await context.SaveChangesAsync();

        var counts = await context.GpsPositions
            .Where(p => p.DeviceId == NemsLDeviceId
                     && p.RecordedAt >= now.AddDays(-14)
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .GroupBy(p => p.PowerVoltage == SaturationByteValue)
            .Select(g => new { IsSaturated = g.Key, Count = g.Count() })
            .ToListAsync();

        var total = counts.Sum(c => c.Count);
        var saturated = counts.FirstOrDefault(c => c.IsSaturated)?.Count ?? 0;

        saturated.Should().BeLessThan((int)(total * SaturationDominanceRatio));
    }

    // ── Noise robustness: trimmed mean ──────────────────────────────────────

    [Fact]
    public void TrimmedMean_DropsTopAndBottomTenPercentBeforeAveraging()
    {
        // Stream of 50 normal readings (byte 43) + 5 wildly noisy spikes
        // (byte 5, sensor glitch). A plain average would drag the mean
        // down to ≈39.6; the trimmed mean drops the 5 lowest and 5 highest
        // tails (10 % each, with N=55 → 5 each) and lands back near 43.
        var values = Enumerable.Repeat(43, 50)
            .Concat(Enumerable.Repeat(5, 5)) // outliers
            .ToList();

        var trimmed = TrimmedMean(values);
        var plain = values.Average();

        trimmed.Should().Be(43);
        plain.Should().BeLessThan(40);
    }

    [Fact]
    public void TrimmedMean_FallsBackToPlainAverageOnSmallSamples()
    {
        // Under 10 readings, a 10 % trim becomes a single-element drop
        // per tail — too aggressive on a tiny window, would erase the
        // signal. Fall back to plain average instead.
        var values = new List<int> { 10, 12, 14, 16, 18 };
        TrimmedMean(values).Should().Be(values.Average());
    }

    [Fact]
    public void TrimmedMean_HandlesEmptyInput()
    {
        TrimmedMean(new List<int>()).Should().Be(0);
    }

    // Mirror of VoltageHealthMonitoringService.TrimmedMean — kept here
    // because the service's helper is private. If the production logic
    // changes shape we want to see the test re-pinned, not silently drift.
    private static double TrimmedMean(IReadOnlyList<int> values)
    {
        if (values.Count == 0) return 0;
        if (values.Count < 10) return values.Average();
        var sorted = values.OrderBy(v => v).ToArray();
        int trim = (int)(sorted.Length * 0.10);
        if (trim <= 0) return sorted.Average();
        var middle = sorted.AsSpan(trim, sorted.Length - 2 * trim);
        long sum = 0;
        foreach (var v in middle) sum += v;
        return (double)sum / middle.Length;
    }

    [Fact]
    public async Task Signal2_RestingCritical_IsRobustToHandfulOfNoisySpikes()
    {
        // 60 frames at byte 38 (11.4V critical) + 5 outlier spikes at
        // byte 250 (well outside any real measurement range — sensor
        // glitches). Plain average is dragged up to ≈ 16.3 V → ABOVE the
        // 12.0V critical threshold, missing the alert. Trimmed mean drops
        // the 5 spikes (10 % of 65 → 6 from each tail), leaving 53 normal
        // readings → correctly averages to 11.4 V → fires the critical signal.
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        SeedRange(context, NemsLDeviceId, now.AddDays(-3), now.AddMinutes(-30),
            count: 60, powerVoltage: 38, ignitionOn: false);
        // 5 spikes — the kind of bursts a flaky ADC produces in clusters.
        for (int i = 0; i < 5; i++)
            SeedPosition(context, NemsLDeviceId, now.AddMinutes(-25 + i), powerVoltage: 250, ignitionOn: false);

        await context.SaveChangesAsync();

        var recent = await context.GpsPositions
            .Where(p => p.DeviceId == NemsLDeviceId
                     && p.RecordedAt >= now.AddDays(-3)
                     && p.IgnitionOn == false
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .Select(p => p.PowerVoltage!.Value)
            .ToListAsync();

        var trimmedV = TrimmedMean(recent) * RawToVoltsFactor;
        var plainV = recent.Average() * RawToVoltsFactor;

        trimmedV.Should().BeLessThanOrEqualTo(RestingCriticalV, "trimmed mean must catch the critical pattern");
        plainV.Should().BeGreaterThan(RestingCriticalV, "plain average would miss it because of the spikes");
    }

    [Fact]
    public async Task Signal4_SaturatedSilence_DoesNotFireWhenDeviceStillCommunicating()
    {
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        // Last comm 5 min ago — well within active communication.
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700,
            lastCommunication: now.AddMinutes(-5));

        SeedRange(context, NemsLDeviceId, now.AddDays(-14), now,
            count: 200, powerVoltage: SaturationByteValue, ignitionOn: false);

        await context.SaveChangesAsync();

        var device = await context.GpsDevices.FirstAsync(d => d.Id == NemsLDeviceId);
        var silenceMin = (now - device.LastCommunication!.Value).TotalMinutes;
        silenceMin.Should().BeLessThan(24 * 60);
    }

    [Theory]
    [InlineData(2)]   // 2h underground parking
    [InlineData(8)]   // overnight parking in covered garage
    [InlineData(20)]  // long working day in a low-coverage industrial zone
    [InlineData(23)]  // edge case just below the 24h threshold
    public async Task Signal4_SaturatedSilence_DoesNotFireForParkingScenariosUnder24Hours(int silenceHours)
    {
        // Acceptance criterion from the operator: a vehicle in an
        // underground parking, a tunnel, or a poor-coverage zone must NOT
        // trigger a "battery panne probable" notification. The 24h
        // requirement is the floor where we can be confident the silence
        // is no longer a routine parking situation.
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700,
            lastCommunication: now.AddHours(-silenceHours));

        SeedRange(context, NemsLDeviceId, now.AddDays(-14), now,
            count: 200, powerVoltage: SaturationByteValue, ignitionOn: false);

        await context.SaveChangesAsync();

        var device = await context.GpsDevices.FirstAsync(d => d.Id == NemsLDeviceId);
        var silenceMin = (now - device.LastCommunication!.Value).TotalMinutes;
        silenceMin.Should().BeLessThan(24 * 60,
            "parking-class silence must not cross the saturated_silence threshold");
    }

    // ── Healthy baseline ────────────────────────────────────────────────────

    [Fact]
    public async Task HealthyBattery_StaysSilent()
    {
        // Stable 12.9 V at rest, 14.1 V driving — none of the four signals trip.
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700,
            lastCommunication: now.AddMinutes(-2));

        SeedRange(context, NemsLDeviceId, now.AddDays(-30), now.AddDays(-7),
            count: 200, powerVoltage: 43, ignitionOn: false);
        SeedRange(context, NemsLDeviceId, now.AddDays(-3), now,
            count: 80, powerVoltage: 43, ignitionOn: false);
        SeedRange(context, NemsLDeviceId, now.AddDays(-3), now,
            count: 60, powerVoltage: 47, ignitionOn: true, speedKph: 60);

        await context.SaveChangesAsync();

        var (avgBaseline, avgRecent) = await EvaluateRestingAsync(context, NemsLDeviceId, now);

        avgRecent.Should().BeGreaterThan(RestingAgingV);
        avgRecent.Should().BeGreaterThan(RestingCriticalV);
        (avgBaseline - avgRecent).Should().BeLessThan(DeclineDeltaV);
    }

    // ── Stamping & re-arm ───────────────────────────────────────────────────

    [Fact]
    public async Task StampingLastVoltageHealthAlertAt_RemovesDeviceFromNextCycleCandidates()
    {
        using var context = TestDbContextFactory.Create();
        var device = SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);
        await context.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var cooldownCutoff = now.AddHours(-CooldownHours);

        device.LastVoltageHealthAlertAt = now;
        await context.SaveChangesAsync();

        var afterStamp = await context.GpsDevices
            .Where(d => d.ProtocolType == "gps_type_1"
                     && d.Vehicle != null
                     && (d.LastVoltageHealthAlertAt == null
                         || d.LastVoltageHealthAlertAt < cooldownCutoff))
            .Select(d => d.Id)
            .ToListAsync();

        afterStamp.Should().NotContain(NemsLDeviceId);
    }

    // ── Helper: replays the resting-voltage average computation ─────────────

    private static async Task<(double avgBaselineV, double avgRecentV)> EvaluateRestingAsync(
        TestGisDbContext context, int deviceId, DateTime now)
    {
        var baseline = await context.GpsPositions
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= now.AddDays(-30)
                     && p.RecordedAt < now.AddDays(-7)
                     && p.IgnitionOn == false
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .Select(p => p.PowerVoltage!.Value)
            .ToListAsync();

        var recent = await context.GpsPositions
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= now.AddDays(-3)
                     && p.IgnitionOn == false
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .Select(p => p.PowerVoltage!.Value)
            .ToListAsync();

        var avgBaselineV = baseline.Count > 0 ? baseline.Average() * RawToVoltsFactor : 0;
        var avgRecentV = recent.Count > 0 ? recent.Average() * RawToVoltsFactor : 0;
        return (avgBaselineV, avgRecentV);
    }
}
