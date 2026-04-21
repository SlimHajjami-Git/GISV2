using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// End-to-end flow tests for the admin decision workflow. Where the
/// <c>*CommandTests</c> files cover individual handlers in isolation, these
/// tests walk through <b>realistic multi-step scenarios</b> that mirror what
/// a production session looks like:
///
/// <list type="bullet">
///   <item><description>A <c>pending</c> event reaches the DB, an admin
///     confirms it via the modal → the row flips + the other admins get an
///     audit notification + a tow-monitoring candidate is born.</description></item>
///   <item><description>The <i>Reporter</i> button is a pure frontend
///     no-op — no command is dispatched, the event stays
///     <c>pending</c>.</description></item>
///   <item><description>Two admins click at the same instant (one confirm,
///     one dismiss). The winner is the first to write; the loser's call is
///     a no-op (no status flip, no second notification).</description></item>
///   <item><description>After a <c>dismissed</c> flip, the event is out of
///     the tow-monitoring candidate set forever.</description></item>
///   <item><description>Confirming stamps <c>TowDetectedAt = null</c>, and
///     manual simulation of the tow-monitoring stamp removes the row from
///     the candidate query — no duplicate &quot;remorquage&quot; pings.</description></item>
/// </list>
///
/// <para>These tests intentionally exercise <b>real handler instances</b>
/// against the in-memory SQLite context + a mocked <see cref="INotificationService"/>,
/// so the interaction between the three moving parts (DB, handler,
/// notification bus) stays honest.</para>
/// </summary>
public class AccidentDecisionFlowTests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;
    private const int AdminAliceId = 100;
    private const int AdminBobId = 101;
    private const int AdminChloeId = 102;
    private const int DriverId = 200;
    private const int TowWatchDays = 14;
    private const int DeviceId = 77;

    // ── Test fixture helpers ────────────────────────────────────────────────

    private static void SeedAdmin(TestGisDbContext context, int id, int companyId, bool isAdmin, string status = "active")
    {
        var role = new Role
        {
            Id = 1000 + id,
            Name = isAdmin ? $"Admin-{id}" : $"Driver-{id}",
            IsCompanyAdmin = isAdmin,
            SocieteId = companyId
        };
        context.Roles.Add(role);

        context.Users.Add(new User
        {
            Id = id,
            FirstName = isAdmin ? $"Admin{id}" : $"Driver{id}",
            LastName = "T",
            Email = $"u{id}@t.com",
            PasswordHash = "x",
            RoleId = role.Id,
            CompanyId = companyId,
            Status = status
        });
    }

    /// <summary>
    /// Seeds a baseline company with three admins (Alice, Bob, Chloé) and
    /// one driver, plus a stock pending accident event. Matches the kind
    /// of fleet the modal is designed for: a handful of managers plus
    /// drivers who must stay unaware of false alarms.
    /// </summary>
    private static async Task<(TestGisDbContext context, AccidentEvent ev)> SeedCompanyWithPendingAccidentAsync(int accidentId = 5000)
    {
        var context = TestDbContextFactory.Create();

        SeedAdmin(context, AdminAliceId, CompanyId, isAdmin: true);
        SeedAdmin(context, AdminBobId, CompanyId, isAdmin: true);
        SeedAdmin(context, AdminChloeId, CompanyId, isAdmin: true);
        SeedAdmin(context, DriverId, CompanyId, isAdmin: false);

        var ev = new AccidentEvent
        {
            Id = accidentId,
            CompanyId = CompanyId,
            VehicleId = 1,
            GpsDeviceId = DeviceId,
            DeviceUid = "IMEI-FLOW",
            IncidentAt = DateTime.UtcNow.AddMinutes(-5),
            Latitude = 36.8,
            Longitude = 10.18,
            VehicleLabel = "TU-FLOW-01",
            Confidence = 92,
            Status = "pending"
        };
        context.AccidentEvents.Add(ev);

        await context.SaveChangesAsync();
        return (context, ev);
    }

    private static ConfirmAccidentCommandHandler BuildConfirmHandler(
        TestGisDbContext context, int deciderUserId, Mock<INotificationService>? notif = null)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: deciderUserId);
        return new ConfirmAccidentCommandHandler(
            context, tenant.Object, (notif ?? new Mock<INotificationService>()).Object,
            NullLogger<ConfirmAccidentCommandHandler>.Instance);
    }

    private static DismissAccidentCommandHandler BuildDismissHandler(
        TestGisDbContext context, int deciderUserId, Mock<INotificationService>? notif = null)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: deciderUserId);
        return new DismissAccidentCommandHandler(
            context, tenant.Object, (notif ?? new Mock<INotificationService>()).Object,
            NullLogger<DismissAccidentCommandHandler>.Instance);
    }

    // ── Flow 1: pending → confirm → tow monitoring pickup ───────────────────

    [Fact]
    public async Task FullFlow_PendingConfirmedByAdmin_EventIsCandidateForTowMonitoring()
    {
        // Setup: a pending accident with 3 admins + 1 driver. Alice clicks
        // "Confirmer l'accident" in the modal.
        var (context, ev) = await SeedCompanyWithPendingAccidentAsync();
        var notif = new Mock<INotificationService>();
        var confirm = BuildConfirmHandler(context, AdminAliceId, notif);

        // Act: Alice confirms
        await confirm.Handle(new ConfirmAccidentCommand(ev.Id), CancellationToken.None);

        // DB state: status=confirmed, Alice stamped, TowDetectedAt still null
        var reloaded = await context.AccidentEvents.FindAsync(ev.Id);
        reloaded!.Status.Should().Be("confirmed");
        reloaded.DecidedByUserId.Should().Be(AdminAliceId);
        reloaded.DecidedAt.Should().NotBeNull();
        reloaded.TowDetectedAt.Should().BeNull();

        // Notifications: Bob + Chloé (other admins) — NOT Alice, NOT driver
        notif.Verify(x => x.CreateAndSendAsync(
            CompanyId, AdminBobId, "accident_decision",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Once);
        notif.Verify(x => x.CreateAndSendAsync(
            CompanyId, AdminChloeId, "accident_decision",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Once);
        notif.Verify(x => x.CreateAndSendAsync(
            It.IsAny<int>(), AdminAliceId, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Never);
        notif.Verify(x => x.CreateAndSendAsync(
            It.IsAny<int>(), DriverId, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Never);

        // Tow-monitoring candidate query sees this row — the service on its
        // next 5-min cycle will scan it against gps_positions for motion.
        var cutoff = DateTime.UtcNow.AddDays(-TowWatchDays);
        var candidates = await context.AccidentEvents
            .Where(e => e.Status == "confirmed"
                     && e.TowDetectedAt == null
                     && e.IncidentAt >= cutoff
                     && e.GpsDeviceId != null)
            .Select(e => e.Id)
            .ToListAsync();
        candidates.Should().Contain(ev.Id);
    }

    // ── Flow 2: pending → dismiss → "possible damage" broadcast ────────────

    [Fact]
    public async Task FullFlow_PendingDismissedByAdmin_OtherAdminsGetPossibleDamageNotif_NeverTowMonitoringCandidate()
    {
        var (context, ev) = await SeedCompanyWithPendingAccidentAsync();
        var notif = new Mock<INotificationService>();
        var dismiss = BuildDismissHandler(context, AdminBobId, notif);

        // Act: Bob clicks "Fausse alerte"
        await dismiss.Handle(new DismissAccidentCommand(ev.Id), CancellationToken.None);

        var reloaded = await context.AccidentEvents.FindAsync(ev.Id);
        reloaded!.Status.Should().Be("dismissed");
        reloaded.DecidedByUserId.Should().Be(AdminBobId);
        reloaded.TowDetectedAt.Should().BeNull();

        // Alice + Chloé get "Choc violent détecté — dégâts possibles" (high priority)
        notif.Verify(x => x.CreateAndSendAsync(
            CompanyId, AdminAliceId, "accident_possible_damage",
            "Choc violent détecté — dégâts possibles",
            It.IsAny<string>(), "high",
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Once);
        notif.Verify(x => x.CreateAndSendAsync(
            CompanyId, AdminChloeId, "accident_possible_damage",
            "Choc violent détecté — dégâts possibles",
            It.IsAny<string>(), "high",
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Driver must NEVER receive this — no panic pings on false alarms
        notif.Verify(x => x.CreateAndSendAsync(
            It.IsAny<int>(), DriverId, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Never);

        // A dismissed event is NOT a tow-monitoring candidate — ever
        var cutoff = DateTime.UtcNow.AddDays(-TowWatchDays);
        var candidates = await context.AccidentEvents
            .Where(e => e.Status == "confirmed"
                     && e.TowDetectedAt == null
                     && e.IncidentAt >= cutoff
                     && e.GpsDeviceId != null)
            .Select(e => e.Id)
            .ToListAsync();
        candidates.Should().NotContain(ev.Id);
    }

    // ── Flow 3: postpone (frontend-only) — no handler dispatched ────────────

    [Fact]
    public async Task FullFlow_PostponeButton_NoCommandDispatched_EventStaysPending()
    {
        // The "Reporter" button is a pure frontend no-op: it hides the modal
        // locally. Nothing reaches the backend, so the event must still be
        // pending and re-surface via the bell notification later.
        var (context, ev) = await SeedCompanyWithPendingAccidentAsync();
        var notif = new Mock<INotificationService>();

        // Act: no handler is invoked (simulating the frontend's behaviour).
        // We don't call confirm/dismiss — just re-read the row.

        // Assert: status stays pending, no one got a new audit notif
        var reloaded = await context.AccidentEvents.FindAsync(ev.Id);
        reloaded!.Status.Should().Be("pending");
        reloaded.DecidedByUserId.Should().BeNull();
        reloaded.DecidedAt.Should().BeNull();

        notif.Verify(x => x.CreateAndSendAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Never);

        // The event is still claimable via the fallback buttons on
        // /rapport-accident/:id — simulate Alice arriving later and confirming.
        var confirm = BuildConfirmHandler(context, AdminAliceId, notif);
        await confirm.Handle(new ConfirmAccidentCommand(ev.Id), CancellationToken.None);

        var afterClaim = await context.AccidentEvents.FindAsync(ev.Id);
        afterClaim!.Status.Should().Be("confirmed");
        afterClaim.DecidedByUserId.Should().Be(AdminAliceId);
    }

    // ── Flow 4: race — two admins click at the same instant ─────────────────

    [Fact]
    public async Task FullFlow_RaceBetweenTwoAdmins_FirstWriteWins_SecondIsNoOp()
    {
        // Scenario: Alice clicks "Confirmer" and a split second later Bob
        // clicks "Fausse alerte". The handlers are idempotent on the
        // <c>Status != "pending"</c> guard, so Bob's call must be a no-op —
        // the event stays "confirmed" as Alice set it, and no "possible
        // damage" broadcast is fanned.
        var (context, ev) = await SeedCompanyWithPendingAccidentAsync();
        var notif = new Mock<INotificationService>();

        var confirm = BuildConfirmHandler(context, AdminAliceId, notif);
        var dismiss = BuildDismissHandler(context, AdminBobId, notif);

        // Alice writes first (would win even under true concurrency because
        // the UPDATE happens before Bob's SELECT in a sequential test)
        await confirm.Handle(new ConfirmAccidentCommand(ev.Id), CancellationToken.None);
        await dismiss.Handle(new DismissAccidentCommand(ev.Id), CancellationToken.None);

        // Final state: Alice's "confirmed" stands
        var reloaded = await context.AccidentEvents.FindAsync(ev.Id);
        reloaded!.Status.Should().Be("confirmed");
        reloaded.DecidedByUserId.Should().Be(AdminAliceId);

        // Only the CONFIRM audit broadcast reached the other admins — the
        // DISMISS handler's "possible damage" broadcast must NOT have fired
        notif.Verify(x => x.CreateAndSendAsync(
            It.IsAny<int>(), It.IsAny<int>(), "accident_possible_damage",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Never);

        // Confirm broadcast reached Bob + Chloé (not Alice, not driver)
        notif.Verify(x => x.CreateAndSendAsync(
            It.IsAny<int>(), It.IsAny<int>(), "accident_decision",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task FullFlow_RaceBetweenTwoAdminsBothConfirm_SecondIsNoOp_OneAuditBroadcast()
    {
        // Two admins race to click "Confirmer". Only the first write lands;
        // the second handler sees status != "pending" and returns early with
        // NO duplicate audit notification.
        var (context, ev) = await SeedCompanyWithPendingAccidentAsync();
        var notif = new Mock<INotificationService>();

        var aliceHandler = BuildConfirmHandler(context, AdminAliceId, notif);
        var bobHandler = BuildConfirmHandler(context, AdminBobId, notif);

        await aliceHandler.Handle(new ConfirmAccidentCommand(ev.Id), CancellationToken.None);
        await bobHandler.Handle(new ConfirmAccidentCommand(ev.Id), CancellationToken.None);

        // DB reflects ONLY the first decider
        var reloaded = await context.AccidentEvents.FindAsync(ev.Id);
        reloaded!.Status.Should().Be("confirmed");
        reloaded.DecidedByUserId.Should().Be(AdminAliceId);      // Alice, not Bob

        // Audit broadcast fired ONCE (Alice's call) to exactly 2 recipients
        // (Bob + Chloé). Bob's call is a no-op, no duplicate notifications.
        notif.Verify(x => x.CreateAndSendAsync(
            It.IsAny<int>(), It.IsAny<int>(), "accident_decision",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── Flow 5: post-confirm tow stamp removes event from candidate set ─────

    [Fact]
    public async Task FullFlow_ConfirmedEventAfterTowStamp_DisappearsFromMonitoringCandidates()
    {
        var (context, ev) = await SeedCompanyWithPendingAccidentAsync();
        var notif = new Mock<INotificationService>();
        var confirm = BuildConfirmHandler(context, AdminAliceId, notif);

        // Alice confirms
        await confirm.Handle(new ConfirmAccidentCommand(ev.Id), CancellationToken.None);

        // Cycle N: tow-monitoring candidate query picks it up
        var cutoff = DateTime.UtcNow.AddDays(-TowWatchDays);
        var beforeStamp = await context.AccidentEvents
            .Where(e => e.Status == "confirmed"
                     && e.TowDetectedAt == null
                     && e.IncidentAt >= cutoff
                     && e.GpsDeviceId != null)
            .Select(e => e.Id)
            .ToListAsync();
        beforeStamp.Should().Contain(ev.Id);

        // Simulate: tow-monitoring scanned, found 3 frames > 5 km/h, stamped
        var reloaded = await context.AccidentEvents.FindAsync(ev.Id);
        reloaded!.TowDetectedAt = DateTime.UtcNow;
        reloaded.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        // Cycle N+1: the same query MUST NOT pick the row up again (avoids
        // duplicate "Remorquage détecté" pings)
        var afterStamp = await context.AccidentEvents
            .Where(e => e.Status == "confirmed"
                     && e.TowDetectedAt == null
                     && e.IncidentAt >= cutoff
                     && e.GpsDeviceId != null)
            .Select(e => e.Id)
            .ToListAsync();
        afterStamp.Should().NotContain(ev.Id);
    }

    // ── Flow 6: cross-tenant — company B can't touch company A's event ─────

    [Fact]
    public async Task FullFlow_CrossTenantConfirmAttempt_IsBlocked_EventUntouched()
    {
        // Setup: event belongs to CompanyId (1). A rogue admin of company 2
        // tries to confirm via a crafted API call.
        var (context, ev) = await SeedCompanyWithPendingAccidentAsync();
        // Add an admin from company 2
        const int ForeignAdminId = 999;
        SeedAdmin(context, ForeignAdminId, OtherCompanyId, isAdmin: true);
        await context.SaveChangesAsync();

        var notif = new Mock<INotificationService>();
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns(OtherCompanyId);
        tenant.Setup(x => x.UserId).Returns(ForeignAdminId);
        tenant.Setup(x => x.IsAuthenticated).Returns(true);

        var confirm = new ConfirmAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<ConfirmAccidentCommandHandler>.Instance);

        // Act + Assert: handler throws NotFound (surfaced as HTTP 404)
        var act = async () => await confirm.Handle(new ConfirmAccidentCommand(ev.Id), CancellationToken.None);
        await act.Should().ThrowAsync<GisAPI.Domain.Exceptions.NotFoundException>();

        // Event left pristine — no status flip, no decider stamp, no notif
        var reloaded = await context.AccidentEvents.FindAsync(ev.Id);
        reloaded!.Status.Should().Be("pending");
        reloaded.DecidedByUserId.Should().BeNull();
        notif.Verify(x => x.CreateAndSendAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Flow 7: multiple parallel accidents — FIFO queue semantics ──────────

    [Fact]
    public async Task FullFlow_TwoParallelAccidents_EachGoesThroughIndependentDecision()
    {
        // The frontend modal queues accidents FIFO; the backend has no queue
        // — each accident_event is a fully independent row. This test pins
        // that independence: deciding accident A leaves accident B untouched.
        var context = TestDbContextFactory.Create();
        SeedAdmin(context, AdminAliceId, CompanyId, isAdmin: true);
        SeedAdmin(context, AdminBobId, CompanyId, isAdmin: true);

        var evA = new AccidentEvent
        {
            Id = 900,
            CompanyId = CompanyId,
            VehicleId = 1,
            GpsDeviceId = DeviceId,
            DeviceUid = "IMEI-A",
            IncidentAt = DateTime.UtcNow.AddMinutes(-10),
            Latitude = 36.8, Longitude = 10.18,
            VehicleLabel = "TU-A",
            Confidence = 92,
            Status = "pending"
        };
        var evB = new AccidentEvent
        {
            Id = 901,
            CompanyId = CompanyId,
            VehicleId = 2,
            GpsDeviceId = DeviceId + 1,
            DeviceUid = "IMEI-B",
            IncidentAt = DateTime.UtcNow.AddMinutes(-2),
            Latitude = 36.9, Longitude = 10.20,
            VehicleLabel = "TU-B",
            Confidence = 95,
            Status = "pending"
        };
        context.AccidentEvents.AddRange(evA, evB);
        await context.SaveChangesAsync();

        var notif = new Mock<INotificationService>();
        var confirm = BuildConfirmHandler(context, AdminAliceId, notif);

        // Alice confirms A; B should remain pending
        await confirm.Handle(new ConfirmAccidentCommand(evA.Id), CancellationToken.None);

        var aReload = await context.AccidentEvents.FindAsync(evA.Id);
        var bReload = await context.AccidentEvents.FindAsync(evB.Id);
        aReload!.Status.Should().Be("confirmed");
        bReload!.Status.Should().Be("pending");
        bReload.DecidedByUserId.Should().BeNull();

        // Now Bob dismisses B; A is untouched
        var dismiss = BuildDismissHandler(context, AdminBobId, notif);
        await dismiss.Handle(new DismissAccidentCommand(evB.Id), CancellationToken.None);

        aReload = await context.AccidentEvents.FindAsync(evA.Id);
        bReload = await context.AccidentEvents.FindAsync(evB.Id);
        aReload!.Status.Should().Be("confirmed");
        aReload.DecidedByUserId.Should().Be(AdminAliceId);
        bReload!.Status.Should().Be("dismissed");
        bReload.DecidedByUserId.Should().Be(AdminBobId);
    }

    // ── Flow 8: end-to-end — detect, confirm, tow, monitoring ──────────────

    [Fact]
    public async Task FullFlow_DetectConfirmTowStamp_AllFourColumnsMoveThroughStates()
    {
        // Tracks a single event's four decision/monitoring columns from
        // birth to tow-detected. This is the "golden path" the product
        // cares about — everything else is error handling.
        var (context, ev) = await SeedCompanyWithPendingAccidentAsync(accidentId: 7777);
        var notif = new Mock<INotificationService>();

        // State 0: freshly detected
        ev.Status.Should().Be("pending");
        ev.DecidedByUserId.Should().BeNull();
        ev.DecidedAt.Should().BeNull();
        ev.TowDetectedAt.Should().BeNull();

        // State 1: admin confirms
        var confirm = BuildConfirmHandler(context, AdminAliceId, notif);
        var beforeDecision = DateTime.UtcNow;
        await confirm.Handle(new ConfirmAccidentCommand(ev.Id), CancellationToken.None);
        var afterDecision = DateTime.UtcNow;

        var confirmed = await context.AccidentEvents.AsNoTracking().FirstAsync(e => e.Id == ev.Id);
        confirmed.Status.Should().Be("confirmed");
        confirmed.DecidedByUserId.Should().Be(AdminAliceId);
        confirmed.DecidedAt.Should().NotBeNull();
        confirmed.DecidedAt!.Value.Should().BeOnOrAfter(beforeDecision).And.BeOnOrBefore(afterDecision);
        confirmed.TowDetectedAt.Should().BeNull();

        // State 2: tow-monitoring scans, finds motion, stamps
        var tracked = await context.AccidentEvents.FirstAsync(e => e.Id == ev.Id);
        var towAt = DateTime.UtcNow.AddMinutes(-1);
        tracked.TowDetectedAt = towAt;
        tracked.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var towed = await context.AccidentEvents.AsNoTracking().FirstAsync(e => e.Id == ev.Id);
        towed.Status.Should().Be("confirmed");                 // stays confirmed
        towed.DecidedByUserId.Should().Be(AdminAliceId);        // Alice's stamp preserved
        towed.DecidedAt.Should().NotBeNull();                   // date preserved
        towed.TowDetectedAt.Should().BeCloseTo(towAt, TimeSpan.FromSeconds(1));

        // And: the row is no longer a monitoring candidate
        var cutoff = DateTime.UtcNow.AddDays(-TowWatchDays);
        var stillCandidate = await context.AccidentEvents
            .Where(e => e.Status == "confirmed"
                     && e.TowDetectedAt == null
                     && e.IncidentAt >= cutoff
                     && e.GpsDeviceId != null
                     && e.Id == ev.Id)
            .AnyAsync();
        stillCandidate.Should().BeFalse();
    }
}
