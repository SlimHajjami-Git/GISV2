using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Pinning tests for the two LINQ filters used by
/// <c>GisAPI.Services.AccidentTowMonitoringService</c>:
///
/// <list type="bullet">
///   <item><description><b>Candidate event selection</b> — only
///     <c>status = "confirmed"</c> events within the 14-day window, without
///     a <c>tow_detected_at</c> stamp and with a non-null <c>GpsDeviceId</c>
///     are picked up on each 5-minute cycle.</description></item>
///   <item><description><b>Admin fan-out</b> — the tow notification reaches
///     every active company admin of the right company and NO ONE else
///     (drivers, inactive users, admins of other companies are all
///     excluded).</description></item>
/// </list>
///
/// <para>These tests are intentionally <i>pinning</i>: they re-run the same
/// LINQ shape the service uses against the in-memory SQLite context. If the
/// filter changes in the service, the test must be updated alongside —
/// which is the desired behaviour. The PostgreSQL-specific LAG window
/// function that drives <c>FindTowMomentAsync</c> is validated manually on
/// staging per step 8 of the plan (it can't run on SQLite without
/// rewriting the raw SQL).</para>
/// </summary>
public class AccidentTowMonitoringServiceTests
{
    private const int TowWatchDays = 14;
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;
    private const int DeviceId = 77;

    private static void SeedUser(TestGisDbContext context, int id, int companyId, bool isAdmin, string status = "active")
    {
        var role = new Role
        {
            Id = 1000 + id,
            Name = isAdmin ? $"Admin-{id}" : $"User-{id}",
            IsCompanyAdmin = isAdmin,
            SocieteId = companyId
        };
        context.Roles.Add(role);

        context.Users.Add(new User
        {
            Id = id,
            FirstName = $"U{id}",
            LastName = "T",
            Email = $"u{id}@t.com",
            PasswordHash = "x",
            RoleId = role.Id,
            CompanyId = companyId,
            Status = status
        });
    }

    // ── Candidate event selection ───────────────────────────────────────────

    [Fact]
    public async Task CandidateSelection_OnlyPicksConfirmedEventsInsideWatchWindowWithoutTowStamp()
    {
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-TowWatchDays);

        // (1) Confirmed, within window, no tow stamp, has device — SHOULD be picked
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 1,
            CompanyId = CompanyId,
            GpsDeviceId = DeviceId,
            DeviceUid = "IMEI",
            IncidentAt = now.AddDays(-3),
            Latitude = 36.8, Longitude = 10.18,
            Confidence = 90,
            Status = "confirmed",
            TowDetectedAt = null
        });

        // (2) Still pending — must NOT be picked (tow watch only starts on confirm)
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 2,
            CompanyId = CompanyId,
            GpsDeviceId = DeviceId,
            DeviceUid = "IMEI",
            IncidentAt = now.AddDays(-2),
            Latitude = 36.8, Longitude = 10.18,
            Confidence = 90,
            Status = "pending"
        });

        // (3) Dismissed — must NOT be picked
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 3,
            CompanyId = CompanyId,
            GpsDeviceId = DeviceId,
            DeviceUid = "IMEI",
            IncidentAt = now.AddDays(-1),
            Latitude = 36.8, Longitude = 10.18,
            Confidence = 90,
            Status = "dismissed"
        });

        // (4) Confirmed, already stamped — must NOT be picked (no re-scan)
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 4,
            CompanyId = CompanyId,
            GpsDeviceId = DeviceId,
            DeviceUid = "IMEI",
            IncidentAt = now.AddDays(-5),
            Latitude = 36.8, Longitude = 10.18,
            Confidence = 90,
            Status = "confirmed",
            TowDetectedAt = now.AddDays(-4)
        });

        // (5) Confirmed, but too old (> 14 days) — must NOT be picked
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 5,
            CompanyId = CompanyId,
            GpsDeviceId = DeviceId,
            DeviceUid = "IMEI",
            IncidentAt = now.AddDays(-20),
            Latitude = 36.8, Longitude = 10.18,
            Confidence = 90,
            Status = "confirmed"
        });

        // (6) Confirmed, inside window, but no GPS device — must NOT be picked
        //    (FindTowMomentAsync has no device to scan)
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 6,
            CompanyId = CompanyId,
            GpsDeviceId = null,
            DeviceUid = "IMEI",
            IncidentAt = now.AddDays(-1),
            Latitude = 36.8, Longitude = 10.18,
            Confidence = 90,
            Status = "confirmed"
        });

        await context.SaveChangesAsync();

        // Replicates the filter used by RunCycleAsync verbatim.
        var picked = await context.AccidentEvents
            .Where(e => e.Status == "confirmed"
                     && e.TowDetectedAt == null
                     && e.IncidentAt >= cutoff
                     && e.GpsDeviceId != null)
            .Select(e => e.Id)
            .ToListAsync();

        picked.Should().BeEquivalentTo(new[] { 1 });
    }

    [Fact]
    public async Task CandidateSelection_DropsEventsOnTheDayTheyExitTheWindow()
    {
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-TowWatchDays);

        // Exactly on the cutoff — still in (>= cutoff)
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 10,
            CompanyId = CompanyId,
            GpsDeviceId = DeviceId,
            DeviceUid = "IMEI",
            IncidentAt = cutoff.AddSeconds(1),
            Latitude = 0, Longitude = 0,
            Confidence = 90,
            Status = "confirmed"
        });

        // One second before cutoff — out
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 11,
            CompanyId = CompanyId,
            GpsDeviceId = DeviceId,
            DeviceUid = "IMEI",
            IncidentAt = cutoff.AddSeconds(-1),
            Latitude = 0, Longitude = 0,
            Confidence = 90,
            Status = "confirmed"
        });

        await context.SaveChangesAsync();

        var picked = await context.AccidentEvents
            .Where(e => e.Status == "confirmed"
                     && e.TowDetectedAt == null
                     && e.IncidentAt >= cutoff
                     && e.GpsDeviceId != null)
            .Select(e => e.Id)
            .ToListAsync();

        picked.Should().BeEquivalentTo(new[] { 10 });
    }

    // ── Admin fan-out ───────────────────────────────────────────────────────

    [Fact]
    public async Task AdminFanOut_OnlyReachesActiveAdminsOfTheSameCompany()
    {
        using var context = TestDbContextFactory.Create();

        // Active admin of the target company — MUST be included
        SeedUser(context, id: 100, companyId: CompanyId, isAdmin: true);

        // Another active admin of the same company — MUST be included
        SeedUser(context, id: 101, companyId: CompanyId, isAdmin: true);

        // Non-admin (driver) of the same company — MUST NOT be included
        SeedUser(context, id: 102, companyId: CompanyId, isAdmin: false);

        // Inactive admin of the same company — MUST NOT be included
        SeedUser(context, id: 103, companyId: CompanyId, isAdmin: true, status: "inactive");

        // Admin of a DIFFERENT company — MUST NOT be included
        SeedUser(context, id: 200, companyId: OtherCompanyId, isAdmin: true);

        await context.SaveChangesAsync();

        // Replicates the filter used by NotifyTowDetectedAsync verbatim
        // (including the .Include(u => u.Role) for the IsCompanyAdmin check).
        var admins = await context.Users
            .Include(u => u.Role)
            .Where(u => u.CompanyId == CompanyId
                     && u.Status == "active"
                     && u.Role != null
                     && u.Role.IsCompanyAdmin)
            .Select(u => u.Id)
            .OrderBy(id => id)
            .ToListAsync();

        admins.Should().BeEquivalentTo(new[] { 100, 101 });
    }

    [Fact]
    public async Task AdminFanOut_ReturnsEmptyWhenCompanyHasNoAdmins()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, id: 102, companyId: CompanyId, isAdmin: false);           // driver
        SeedUser(context, id: 103, companyId: CompanyId, isAdmin: true, status: "inactive");
        await context.SaveChangesAsync();

        var admins = await context.Users
            .Include(u => u.Role)
            .Where(u => u.CompanyId == CompanyId
                     && u.Status == "active"
                     && u.Role != null
                     && u.Role.IsCompanyAdmin)
            .Select(u => u.Id)
            .ToListAsync();

        admins.Should().BeEmpty();
    }

    // ── Stamp semantics ─────────────────────────────────────────────────────

    [Fact]
    public async Task StampingTowDetectedAt_RemovesTheEventFromTheNextCycleCandidates()
    {
        // The service stamps tow_detected_at = NOW() once motion is detected.
        // After the stamp, the candidate-selection filter must drop the row
        // so subsequent cycles don't re-notify (prevents duplicate tow pings).
        using var context = TestDbContextFactory.Create();
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-TowWatchDays);

        var ev = new AccidentEvent
        {
            Id = 42,
            CompanyId = CompanyId,
            GpsDeviceId = DeviceId,
            DeviceUid = "IMEI",
            IncidentAt = now.AddDays(-2),
            Latitude = 0, Longitude = 0,
            Confidence = 90,
            Status = "confirmed",
            TowDetectedAt = null
        };
        context.AccidentEvents.Add(ev);
        await context.SaveChangesAsync();

        // Cycle 1: picked up
        var beforeStamp = await context.AccidentEvents
            .Where(e => e.Status == "confirmed"
                     && e.TowDetectedAt == null
                     && e.IncidentAt >= cutoff
                     && e.GpsDeviceId != null)
            .Select(e => e.Id)
            .ToListAsync();
        beforeStamp.Should().Contain(42);

        // Stamp tow_detected_at (mimicking the service)
        ev.TowDetectedAt = now;
        ev.UpdatedAt = now;
        await context.SaveChangesAsync();

        // Cycle 2: should be dropped from candidates
        var afterStamp = await context.AccidentEvents
            .Where(e => e.Status == "confirmed"
                     && e.TowDetectedAt == null
                     && e.IncidentAt >= cutoff
                     && e.GpsDeviceId != null)
            .Select(e => e.Id)
            .ToListAsync();
        afterStamp.Should().NotContain(42);
    }
}
