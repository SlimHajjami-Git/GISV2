using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Pinning tests for the two-signal design that drives
/// <c>GisAPI.Services.VoltageHealthMonitoringService</c>:
///
/// <list type="bullet">
///   <item><description><b>Candidate filter</b> — NEMS L (gps_type_1)
///     devices with a vehicle, past the 48 h cooldown.</description></item>
///   <item><description><b>resting_voltage_low</b> — at rest (ignition
///     off), at least 10 frames AND ≥20 % of recent frames report
///     voltage ≤ 12.0 V (byte ≤ 40). Direct battery signal that the
///     firmware CAN expose despite its 12.9 V saturation ceiling.</description></item>
///   <item><description><b>saturated_silence</b> — ≥95 % of frames at
///     byte 43 over 14d AND last_communication > 24h. Brutal-death
///     pattern that gets through the firmware blind-spot.</description></item>
/// </list>
///
/// <para>The two signals we DON'T have any more (intentional):
/// <c>charging_voltage_low</c> (it was the alternator, not the battery,
/// and the firmware saturation overlap made it 49 false positives), and
/// <c>resting_voltage_decline</c> (the baseline is always 12.9 V on a
/// saturated firmware so the delta check never fires meaningfully).
/// </para>
/// </summary>
public class VoltageHealthMonitoringServiceTests
{
    private const int CompanyId = 1;
    private const int NemsLDeviceId = 500;

    // Mirror of the service's constants.
    private const double RawToVoltsFactor = 0.3;
    private const int RestingLowByteThreshold = 40;        // 12.0 V
    private const int MinLowFramesForAlert = 10;
    private const double LowFramesMinShare = 0.20;
    private const int MinRestingFrames = 50;
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
        bool? ignitionOn)
    {
        context.GpsPositions.Add(new GpsPosition
        {
            DeviceId = deviceId,
            RecordedAt = recordedAt,
            Latitude = 36.8,
            Longitude = 10.18,
            PowerVoltage = powerVoltage,
            IgnitionOn = ignitionOn,
            IsValid = true
        });
    }

    private static void SeedRange(
        TestGisDbContext context,
        int deviceId,
        DateTime windowStart,
        DateTime windowEnd,
        int count,
        int powerVoltage,
        bool ignitionOn)
    {
        var span = (windowEnd - windowStart).TotalSeconds;
        for (int i = 0; i < count; i++)
        {
            var ts = windowStart.AddSeconds(span * i / count);
            SeedPosition(context, deviceId, ts, powerVoltage, ignitionOn);
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

    // ── Signal A: resting_voltage_low ────────────────────────────────────────

    [Fact]
    public async Task RestingLow_Fires_WhenAtLeast20PercentOfFramesAreBelow12V()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // 60 healthy frames at byte 43 (saturation) + 20 critical frames
        // at byte 38 (11.4 V). 20/80 = 25 % low → above the 20 % bar.
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now.AddDays(-1),
            count: 60, powerVoltage: 43, ignitionOn: false);
        SeedRange(context, NemsLDeviceId, now.AddDays(-1), now,
            count: 20, powerVoltage: 38, ignitionOn: false);

        await context.SaveChangesAsync();

        var (low, total) = await CountAsync(context, NemsLDeviceId, now);
        total.Should().BeGreaterThanOrEqualTo(MinRestingFrames);
        low.Should().BeGreaterThanOrEqualTo(MinLowFramesForAlert);
        ((double)low / total).Should().BeGreaterThanOrEqualTo(LowFramesMinShare);
    }

    [Fact]
    public async Task RestingLow_DoesNotFire_OnHealthyFleetSaturatedAt12_9V()
    {
        // The default case for a new fleet: every frame at byte 43.
        // Critically the signal must NOT trigger here — it was the 49
        // false positives on the live data that motivated this refactor.
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now,
            count: 100, powerVoltage: 43, ignitionOn: false);

        await context.SaveChangesAsync();

        var (low, total) = await CountAsync(context, NemsLDeviceId, now);
        total.Should().BeGreaterThanOrEqualTo(MinRestingFrames);
        low.Should().Be(0);
    }

    [Fact]
    public async Task RestingLow_DoesNotFire_WhenFewerThan10LowFrames()
    {
        // 5 low readings out of 100 = 5 % low — under the absolute floor.
        // Even though the share threshold could conceivably be met on a
        // tiny sample, the floor of 10 frames prevents single-event
        // noise from triggering a critical battery alert.
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now.AddDays(-1),
            count: 95, powerVoltage: 43, ignitionOn: false);
        SeedRange(context, NemsLDeviceId, now.AddDays(-1), now,
            count: 5, powerVoltage: 38, ignitionOn: false);

        await context.SaveChangesAsync();

        var (low, total) = await CountAsync(context, NemsLDeviceId, now);
        low.Should().BeLessThan(MinLowFramesForAlert);
    }

    [Fact]
    public async Task RestingLow_DoesNotFire_WhenShareBelow20Percent()
    {
        // 12 low readings out of 200 = 6 % low — above the absolute floor
        // (10) but below the share threshold (20 %). The share guard
        // protects against high-volume devices accumulating enough lows
        // to look problematic by sheer frame count.
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now.AddDays(-1),
            count: 188, powerVoltage: 43, ignitionOn: false);
        SeedRange(context, NemsLDeviceId, now.AddDays(-1), now,
            count: 12, powerVoltage: 38, ignitionOn: false);

        await context.SaveChangesAsync();

        var (low, total) = await CountAsync(context, NemsLDeviceId, now);
        low.Should().BeGreaterThanOrEqualTo(MinLowFramesForAlert);
        ((double)low / total).Should().BeLessThan(LowFramesMinShare);
    }

    [Fact]
    public async Task RestingLow_OnlyCountsIgnitionOffFrames()
    {
        // Cranking dips count in driving frames, which can briefly look
        // critical. We deliberately filter to ignition_on = false so the
        // signal reflects battery state at rest, not transient draws.
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // Plenty of low readings, but ALL with ignition on → ignored.
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now,
            count: 100, powerVoltage: 35, ignitionOn: true);

        await context.SaveChangesAsync();

        var (low, total) = await CountAsync(context, NemsLDeviceId, now);
        total.Should().Be(0);
        low.Should().Be(0);
    }

    [Theory]
    [InlineData(40, true)]    // 40 * 0.3 = 12.0 V exactly → counts as low
    [InlineData(39, true)]    // 11.7 V → counts as low
    [InlineData(38, true)]    // 11.4 V → counts as low
    [InlineData(41, false)]   // 12.3 V → does NOT count as low
    [InlineData(42, false)]   // 12.6 V → does NOT count as low
    [InlineData(43, false)]   // 12.9 V (saturation) → not low
    public async Task RestingLow_ThresholdIsByte40Inclusive(int byteValue, bool shouldCount)
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now,
            count: 50, powerVoltage: byteValue, ignitionOn: false);

        await context.SaveChangesAsync();

        var (low, total) = await CountAsync(context, NemsLDeviceId, now);
        if (shouldCount) low.Should().Be(total);
        else             low.Should().Be(0);
    }

    // ── Signal B: saturated_silence (unchanged from previous refactor) ───────

    [Fact]
    public async Task SaturatedSilence_Fires_WhenAllFramesSaturatedAndSilent24h()
    {
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700,
            lastCommunication: now.AddHours(-25));

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

        total.Should().BeGreaterThanOrEqualTo(MinRestingFrames);
        saturated.Should().Be(total);
    }

    [Theory]
    [InlineData(2)]   // 2h underground parking
    [InlineData(8)]   // overnight in covered garage
    [InlineData(20)]  // long working day in a low-coverage industrial zone
    [InlineData(23)]  // edge case just below the 24h threshold
    public async Task SaturatedSilence_DoesNotFire_ForParkingUnder24Hours(int silenceHours)
    {
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

    [Fact]
    public async Task SaturatedSilence_StillFires_WhenLessThanFivePercentAreOutliers()
    {
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700,
            lastCommunication: now.AddHours(-30));

        SeedRange(context, NemsLDeviceId, now.AddDays(-14), now,
            count: 195, powerVoltage: SaturationByteValue, ignitionOn: false);
        for (int i = 0; i < 5; i++)
            SeedPosition(context, NemsLDeviceId, now.AddDays(-2).AddMinutes(i),
                powerVoltage: 41, ignitionOn: false);

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
    public async Task SaturatedSilence_DoesNotFire_WhenMoreThanFivePercentAreOutliers()
    {
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700,
            lastCommunication: now.AddHours(-30));

        SeedRange(context, NemsLDeviceId, now.AddDays(-14), now,
            count: 180, powerVoltage: SaturationByteValue, ignitionOn: false);
        for (int i = 0; i < 20; i++)
            SeedPosition(context, NemsLDeviceId, now.AddDays(-2).AddMinutes(i),
                powerVoltage: 41, ignitionOn: false);

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

    // ── Helper: replays the resting low/total LINQ ──────────────────────────

    private static async Task<(int low, int total)> CountAsync(
        TestGisDbContext context, int deviceId, DateTime now)
    {
        var recentStart = now.AddDays(-7);
        var groups = await context.GpsPositions
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= recentStart
                     && p.IgnitionOn == false
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .GroupBy(p => p.PowerVoltage!.Value <= RestingLowByteThreshold)
            .Select(g => new { IsLow = g.Key, Count = g.Count() })
            .ToListAsync();

        var total = groups.Sum(g => g.Count);
        var low = groups.FirstOrDefault(g => g.IsLow)?.Count ?? 0;
        return (low, total);
    }
}
