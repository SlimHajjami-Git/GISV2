using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Pinning tests for the single-signal design of
/// <c>GisAPI.Services.VoltageHealthMonitoringService</c>:
///
/// <list type="bullet">
///   <item><description><b>Candidate filter</b> — NEMS L (gps_type_1)
///     devices with a vehicle, NOT immobilised, past the 48 h cooldown.</description></item>
///   <item><description><b>battery_dead</b> — at rest (ignition off),
///     at least 10 frames AND ≥20 % of recent frames report voltage
///     strictly under 11.9 V (byte ≤ 39). The only signal we push.</description></item>
/// </list>
///
/// <para>The signals we intentionally REMOVED (operator feedback after
/// two false-positive saturated_silence notifications on long-parked
/// rental vehicles):
/// <c>saturated_silence</c> (offline alerts were unactionable noise),
/// <c>charging_voltage_low</c> (alternator, not battery, and unmeasurable
/// past firmware saturation), and <c>resting_voltage_decline</c>
/// (baseline always saturated, delta check never meaningful).</para>
/// </summary>
public class VoltageHealthMonitoringServiceTests
{
    private const int CompanyId = 1;
    private const int NemsLDeviceId = 500;

    // Mirror of the service's constants.
    private const double RawToVoltsFactor = 0.3;
    private const int RestingDeadByteThreshold = 39;       // 11.7 V (strictly under 11.9 V)
    private const int MinDeadFramesForAlert = 10;
    private const double DeadFramesMinShare = 0.20;
    private const int MinRestingFrames = 50;
    private const int CooldownHours = 48;

    private static GpsDevice SeedDevice(
        TestGisDbContext context,
        int id,
        string protocolType,
        int? vehicleId,
        DateTime? lastHealthAlertAt = null,
        bool isImmobilized = false)
    {
        var device = new GpsDevice
        {
            Id = id,
            CompanyId = CompanyId,
            DeviceUid = $"DEV-{id}",
            Status = "active",
            ProtocolType = protocolType,
            LastVoltageHealthAlertAt = lastHealthAlertAt
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
                Status = "available",
                IsImmobilized = isImmobilized
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
    public async Task CandidateFilter_OnlyPicksNemsLWithVehicleNotImmobilisedPastCooldown()
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
        SeedDevice(context, id: 507, protocolType: "gps_type_1", vehicleId: 705,
            isImmobilized: true); // operator muted, skip

        await context.SaveChangesAsync();

        var picked = await context.GpsDevices
            .Include(d => d.Vehicle)
            .Where(d => d.ProtocolType == "gps_type_1"
                     && d.Vehicle != null
                     && !d.Vehicle.IsImmobilized
                     && (d.LastVoltageHealthAlertAt == null
                         || d.LastVoltageHealthAlertAt < cooldownCutoff))
            .Select(d => d.Id)
            .OrderBy(id => id)
            .ToListAsync();

        picked.Should().BeEquivalentTo(new[] { NemsLDeviceId, 503 });
    }

    // ── battery_dead signal ─────────────────────────────────────────────────

    [Fact]
    public async Task BatteryDead_Fires_WhenAtLeast20PercentOfFramesAreBelow11_9V()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // 60 healthy frames at byte 43 (saturation = 12.9 V) + 20 dead
        // frames at byte 38 (11.4 V, clearly under 11.9 V). 20/80 = 25 %.
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now.AddDays(-1),
            count: 60, powerVoltage: 43, ignitionOn: false);
        SeedRange(context, NemsLDeviceId, now.AddDays(-1), now,
            count: 20, powerVoltage: 38, ignitionOn: false);

        await context.SaveChangesAsync();

        var (dead, total) = await CountAsync(context, NemsLDeviceId, now);
        total.Should().BeGreaterThanOrEqualTo(MinRestingFrames);
        dead.Should().BeGreaterThanOrEqualTo(MinDeadFramesForAlert);
        ((double)dead / total).Should().BeGreaterThanOrEqualTo(DeadFramesMinShare);
    }

    [Fact]
    public async Task BatteryDead_DoesNotFire_OnHealthyFleetSaturatedAt12_9V()
    {
        // Default state of a healthy fleet: every frame at byte 43.
        // This must NOT trigger — the whole point of the refactor.
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now,
            count: 100, powerVoltage: 43, ignitionOn: false);

        await context.SaveChangesAsync();

        var (dead, total) = await CountAsync(context, NemsLDeviceId, now);
        total.Should().BeGreaterThanOrEqualTo(MinRestingFrames);
        dead.Should().Be(0);
    }

    [Fact]
    public async Task BatteryDead_DoesNotFire_When12_0VFramesAreFrequent()
    {
        // 12.0 V (byte 40) is weak but NOT yet "dead" per the operator
        // spec: we only alert below 11.9 V. Lots of byte=40 readings must
        // not trigger the alert.
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now,
            count: 100, powerVoltage: 40, ignitionOn: false);

        await context.SaveChangesAsync();

        var (dead, total) = await CountAsync(context, NemsLDeviceId, now);
        dead.Should().Be(0);
    }

    [Fact]
    public async Task BatteryDead_DoesNotFire_WhenFewerThan10DeadFrames()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now.AddDays(-1),
            count: 95, powerVoltage: 43, ignitionOn: false);
        SeedRange(context, NemsLDeviceId, now.AddDays(-1), now,
            count: 5, powerVoltage: 38, ignitionOn: false);

        await context.SaveChangesAsync();

        var (dead, total) = await CountAsync(context, NemsLDeviceId, now);
        dead.Should().BeLessThan(MinDeadFramesForAlert);
    }

    [Fact]
    public async Task BatteryDead_DoesNotFire_WhenShareBelow20Percent()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now.AddDays(-1),
            count: 188, powerVoltage: 43, ignitionOn: false);
        SeedRange(context, NemsLDeviceId, now.AddDays(-1), now,
            count: 12, powerVoltage: 38, ignitionOn: false);

        await context.SaveChangesAsync();

        var (dead, total) = await CountAsync(context, NemsLDeviceId, now);
        dead.Should().BeGreaterThanOrEqualTo(MinDeadFramesForAlert);
        ((double)dead / total).Should().BeLessThan(DeadFramesMinShare);
    }

    [Fact]
    public async Task BatteryDead_OnlyCountsIgnitionOffFrames()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // Plenty of low readings, but ALL with ignition on → ignored.
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now,
            count: 100, powerVoltage: 35, ignitionOn: true);

        await context.SaveChangesAsync();

        var (dead, total) = await CountAsync(context, NemsLDeviceId, now);
        total.Should().Be(0);
        dead.Should().Be(0);
    }

    [Theory]
    [InlineData(39, true)]    // 11.7 V → counts as dead
    [InlineData(38, true)]    // 11.4 V → counts as dead
    [InlineData(30, true)]    //  9.0 V → counts as dead
    [InlineData(40, false)]   // 12.0 V → does NOT count (weak but not dead)
    [InlineData(41, false)]   // 12.3 V → does NOT count
    [InlineData(43, false)]   // 12.9 V (saturation) → does NOT count
    public async Task BatteryDead_ThresholdIsByte39Inclusive(int byteValue, bool shouldCount)
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        SeedRange(context, NemsLDeviceId, now.AddDays(-7), now,
            count: 50, powerVoltage: byteValue, ignitionOn: false);

        await context.SaveChangesAsync();

        var (dead, total) = await CountAsync(context, NemsLDeviceId, now);
        if (shouldCount) dead.Should().Be(total);
        else             dead.Should().Be(0);
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
                     && !d.Vehicle.IsImmobilized
                     && (d.LastVoltageHealthAlertAt == null
                         || d.LastVoltageHealthAlertAt < cooldownCutoff))
            .Select(d => d.Id)
            .ToListAsync();

        afterStamp.Should().NotContain(NemsLDeviceId);
    }

    // ── Helper: replays the dead/total LINQ ─────────────────────────────────

    private static async Task<(int dead, int total)> CountAsync(
        TestGisDbContext context, int deviceId, DateTime now)
    {
        var recentStart = now.AddDays(-7);
        var groups = await context.GpsPositions
            .Where(p => p.DeviceId == deviceId
                     && p.RecordedAt >= recentStart
                     && p.IgnitionOn == false
                     && p.PowerVoltage.HasValue
                     && p.PowerVoltage.Value > 0)
            .GroupBy(p => p.PowerVoltage!.Value <= RestingDeadByteThreshold)
            .Select(g => new { IsDead = g.Key, Count = g.Count() })
            .ToListAsync();

        var total = groups.Sum(g => g.Count);
        var dead = groups.FirstOrDefault(g => g.IsDead)?.Count ?? 0;
        return (dead, total);
    }
}
