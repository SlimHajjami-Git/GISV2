using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Pinning tests for the two LINQ shapes that drive
/// <c>GisAPI.Services.BatteryMonitoringService</c>:
///
/// <list type="bullet">
///   <item><description><b>Candidate filter</b> — only NEMS L
///     (<c>protocol_type == "gps_type_1"</c>) devices assigned to a vehicle
///     and either never-alerted or past the 24h cooldown are scanned.</description></item>
///   <item><description><b>Low-voltage rule</b> — on the 5 most recent
///     positions, ≥4 must report <c>0 &lt; power_voltage &lt; 35</c> AND
///     ≥3 must report <c>ignition_on = true</c>. Otherwise the device is
///     considered healthy.</description></item>
/// </list>
///
/// <para>We re-run the same LINQ the service uses against an in-memory
/// SQLite context. The MediatR publish path + the admin fan-out are
/// covered by the <c>PowerCutNotificationHandler</c> tests and the
/// <c>AccidentTowMonitoringService</c> admin-filter test (same pattern);
/// duplicating them here would pin the same query twice.</para>
/// </summary>
public class BatteryMonitoringServiceTests
{
    private const int CompanyId = 1;
    private const int NemsLDeviceId = 500;
    private const int NoronDeviceId = 501;
    private const int UnassignedDeviceId = 502;
    private const int CooldownHours = 24;
    private const int LowVoltageThreshold = 35;
    private const int RecentFramesWindow = 5;
    private const int LowVoltageHits = 4;
    private const int IgnitionOnHits = 3;

    private static GpsDevice SeedDevice(
        TestGisDbContext context,
        int id,
        string protocolType,
        int? vehicleId,
        DateTime? lastAlertAt = null)
    {
        var device = new GpsDevice
        {
            Id = id,
            CompanyId = CompanyId,
            DeviceUid = $"DEV-{id}",
            Status = "active",
            ProtocolType = protocolType,
            LastBatteryAlertAt = lastAlertAt
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

    // ── Candidate filter ────────────────────────────────────────────────────

    [Fact]
    public async Task CandidateFilter_OnlyPicksNemsLDevicesAssignedToAVehicleAndPastCooldown()
    {
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        var cooldownCutoff = now.AddHours(-CooldownHours);

        // (1) NEMS L, has vehicle, never alerted — SHOULD be picked
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        // (2) NEMS L, has vehicle, alert stamped 25h ago (past cooldown) — SHOULD be picked
        SeedDevice(context, id: 503, protocolType: "gps_type_1", vehicleId: 701,
            lastAlertAt: now.AddHours(-25));

        // (3) NEMS L, has vehicle, alert stamped 2h ago (inside cooldown) — MUST be skipped
        SeedDevice(context, id: 504, protocolType: "gps_type_1", vehicleId: 702,
            lastAlertAt: now.AddHours(-2));

        // (4) Noron device — MUST be skipped (parser bug on voltage byte)
        SeedDevice(context, id: NoronDeviceId, protocolType: "noron", vehicleId: 703);

        // (5) Unassigned protocol_type ("") — MUST be skipped
        SeedDevice(context, id: 505, protocolType: "", vehicleId: 704);

        // (6) NEMS L without vehicle — MUST be skipped (no one to alert for)
        SeedDevice(context, id: UnassignedDeviceId, protocolType: "gps_type_1", vehicleId: null);

        await context.SaveChangesAsync();

        // Replicates the candidate selection from RunCycleAsync verbatim.
        var picked = await context.GpsDevices
            .Include(d => d.Vehicle)
            .Where(d => d.ProtocolType == "gps_type_1"
                     && d.Vehicle != null
                     && (d.LastBatteryAlertAt == null
                         || d.LastBatteryAlertAt < cooldownCutoff))
            .Select(d => d.Id)
            .OrderBy(id => id)
            .ToListAsync();

        picked.Should().BeEquivalentTo(new[] { NemsLDeviceId, 503 });
    }

    // ── Low-voltage rule ────────────────────────────────────────────────────

    [Fact]
    public async Task LowVoltageRule_FlagsDeviceWithFourSubThresholdAndThreeIgnitionOn()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // 4 sub-threshold frames (30,28,31,25), 3 with ignition on — flagged.
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-5), powerVoltage: 30, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-4), powerVoltage: 28, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-3), powerVoltage: 31, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-2), powerVoltage: 25, ignitionOn: false);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-1), powerVoltage: 44, ignitionOn: true);

        await context.SaveChangesAsync();

        var (lowHits, ignitionHits) = await EvaluateAsync(context, NemsLDeviceId);

        lowHits.Should().BeGreaterOrEqualTo(LowVoltageHits);
        ignitionHits.Should().BeGreaterOrEqualTo(IgnitionOnHits);
        (lowHits >= LowVoltageHits && ignitionHits >= IgnitionOnHits).Should().BeTrue();
    }

    [Fact]
    public async Task LowVoltageRule_SkipsDeviceWithOnlyThreeSubThresholdFrames()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-5), powerVoltage: 30, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-4), powerVoltage: 28, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-3), powerVoltage: 31, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-2), powerVoltage: 45, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-1), powerVoltage: 44, ignitionOn: true);

        await context.SaveChangesAsync();

        var (lowHits, _) = await EvaluateAsync(context, NemsLDeviceId);
        lowHits.Should().BeLessThan(LowVoltageHits);
    }

    [Fact]
    public async Task LowVoltageRule_SkipsParkedVehicle_IgnitionOffOnMostFrames()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // Vehicle parked → voltage can legitimately dip low; we only flag
        // when the driver is actively drawing from the battery.
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-5), powerVoltage: 30, ignitionOn: false);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-4), powerVoltage: 28, ignitionOn: false);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-3), powerVoltage: 31, ignitionOn: false);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-2), powerVoltage: 25, ignitionOn: false);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-1), powerVoltage: 27, ignitionOn: true);

        await context.SaveChangesAsync();

        var (lowHits, ignitionHits) = await EvaluateAsync(context, NemsLDeviceId);
        lowHits.Should().BeGreaterOrEqualTo(LowVoltageHits);
        ignitionHits.Should().BeLessThan(IgnitionOnHits);
    }

    [Fact]
    public async Task LowVoltageRule_TreatsNullOrZeroVoltageAsHealthy()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // 0 and NULL are sensor artifacts (device booting, GPS glitch), not
        // real voltage readings — must NOT count toward low-voltage hits.
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-5), powerVoltage: 0,    ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-4), powerVoltage: null, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-3), powerVoltage: 28,   ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-2), powerVoltage: 29,   ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-1), powerVoltage: 30,   ignitionOn: true);

        await context.SaveChangesAsync();

        var (lowHits, _) = await EvaluateAsync(context, NemsLDeviceId);
        lowHits.Should().Be(3); // only the three real readings, under threshold
        lowHits.Should().BeLessThan(LowVoltageHits);
    }

    [Fact]
    public async Task LowVoltageRule_SkipsDeviceWithFewerThanFiveFrames()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // Only 3 frames total — not enough evidence to trust the signal.
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-3), powerVoltage: 20, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-2), powerVoltage: 22, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-1), powerVoltage: 24, ignitionOn: true);

        await context.SaveChangesAsync();

        var recent = await context.GpsPositions
            .Where(p => p.DeviceId == NemsLDeviceId)
            .OrderByDescending(p => p.RecordedAt)
            .Take(RecentFramesWindow)
            .ToListAsync();

        recent.Count.Should().BeLessThan(RecentFramesWindow);
    }

    [Fact]
    public async Task LowVoltageRule_HealthyBatteryStaysSilent()
    {
        using var context = TestDbContextFactory.Create();
        SeedDevice(context, id: NemsLDeviceId, protocolType: "gps_type_1", vehicleId: 700);

        var now = DateTime.UtcNow;
        // p50 in the real DB is 43 — all readings above threshold.
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-5), powerVoltage: 42, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-4), powerVoltage: 43, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-3), powerVoltage: 44, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-2), powerVoltage: 41, ignitionOn: true);
        SeedPosition(context, NemsLDeviceId, now.AddMinutes(-1), powerVoltage: 45, ignitionOn: false);

        await context.SaveChangesAsync();

        var (lowHits, _) = await EvaluateAsync(context, NemsLDeviceId);
        lowHits.Should().Be(0);
    }

    // ── Helper: replays the in-memory evaluation done by IsBatteryLowAsync ─

    private static async Task<(int lowHits, int ignitionHits)> EvaluateAsync(
        TestGisDbContext context,
        int deviceId)
    {
        var recent = await context.GpsPositions
            .Where(p => p.DeviceId == deviceId)
            .OrderByDescending(p => p.RecordedAt)
            .Take(RecentFramesWindow)
            .Select(p => new { p.PowerVoltage, p.IgnitionOn })
            .ToListAsync();

        int lowHits = recent.Count(p =>
            p.PowerVoltage.HasValue
            && p.PowerVoltage.Value > 0
            && p.PowerVoltage.Value < LowVoltageThreshold);

        int ignitionHits = recent.Count(p => p.IgnitionOn == true);

        return (lowHits, ignitionHits);
    }
}
