using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Covers the decision workflow for <see cref="DismissAccidentCommand"/>:
///   • happy-path flips <c>status = "dismissed"</c> and stamps the decider,
///   • the second call on an already-decided event is a no-op (idempotent),
///   • a cross-tenant attempt yields <see cref="NotFoundException"/>,
///   • a <c>accident_possible_damage</c> notification is fanned to every
///     OTHER active admin of the company (and ONLY to admins — drivers are
///     explicitly excluded per UX decision: no panic pings).
/// </summary>
public class DismissAccidentCommandTests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;
    private const int DeciderUserId = 100;

    private static int SeedAdmin(TestGisDbContext context, int id, int companyId, bool isAdmin, string status = "active")
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
            FirstName = $"User{id}",
            LastName = "Test",
            Email = $"u{id}@test.com",
            PasswordHash = "x",
            RoleId = role.Id,
            CompanyId = companyId,
            Status = status
        });
        return id;
    }

    private static AccidentEvent SeedPendingAccident(TestGisDbContext context, int id, int companyId)
    {
        var ev = new AccidentEvent
        {
            Id = id,
            CompanyId = companyId,
            VehicleId = 1,
            GpsDeviceId = 1,
            DeviceUid = "IMEI-TEST",
            IncidentAt = DateTime.UtcNow.AddMinutes(-5),
            Latitude = 36.8,
            Longitude = 10.18,
            VehicleLabel = "TU-0001",
            Confidence = 85,
            Status = "pending"
        };
        context.AccidentEvents.Add(ev);
        return ev;
    }

    // ── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_PendingEvent_FlipsToDismissedAndStampsDecider()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        SeedAdmin(context, DeciderUserId, CompanyId, isAdmin: true);
        SeedPendingAccident(context, id: 50, CompanyId);
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new DismissAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<DismissAccidentCommandHandler>.Instance);

        var before = DateTime.UtcNow;

        // Act
        var result = await handler.Handle(new DismissAccidentCommand(50), CancellationToken.None);

        // Assert
        result.Should().Be(Unit.Value);
        var after = DateTime.UtcNow;

        var reloaded = await context.AccidentEvents.FindAsync(50);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be("dismissed");
        reloaded.DecidedByUserId.Should().Be(DeciderUserId);
        reloaded.DecidedAt.Should().NotBeNull();
        reloaded.DecidedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        reloaded.TowDetectedAt.Should().BeNull();
    }

    // ── Idempotency ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_AlreadyDismissedEvent_IsNoOp()
    {
        using var context = TestDbContextFactory.Create();
        SeedAdmin(context, DeciderUserId, CompanyId, isAdmin: true);
        var alreadyAt = DateTime.UtcNow.AddHours(-1);
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 60,
            CompanyId = CompanyId,
            DeviceUid = "IMEI-TEST",
            IncidentAt = DateTime.UtcNow.AddHours(-2),
            Latitude = 36.8,
            Longitude = 10.18,
            Confidence = 90,
            Status = "dismissed",
            DecidedByUserId = 7,
            DecidedAt = alreadyAt
        });
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new DismissAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<DismissAccidentCommandHandler>.Instance);

        await handler.Handle(new DismissAccidentCommand(60), CancellationToken.None);

        var reloaded = await context.AccidentEvents.FindAsync(60);
        reloaded!.Status.Should().Be("dismissed");
        reloaded.DecidedByUserId.Should().Be(7);
        reloaded.DecidedAt.Should().BeCloseTo(alreadyAt, TimeSpan.FromSeconds(1));

        notif.Verify(
            x => x.CreateAndSendAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyConfirmedEvent_IsNoOp_DoesNotFlipBack()
    {
        using var context = TestDbContextFactory.Create();
        SeedAdmin(context, DeciderUserId, CompanyId, isAdmin: true);
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 61,
            CompanyId = CompanyId,
            DeviceUid = "IMEI-TEST",
            IncidentAt = DateTime.UtcNow.AddHours(-1),
            Latitude = 36.8,
            Longitude = 10.18,
            Confidence = 90,
            Status = "confirmed",
            DecidedByUserId = 8,
            DecidedAt = DateTime.UtcNow.AddMinutes(-30)
        });
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new DismissAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<DismissAccidentCommandHandler>.Instance);

        await handler.Handle(new DismissAccidentCommand(61), CancellationToken.None);

        var reloaded = await context.AccidentEvents.FindAsync(61);
        reloaded!.Status.Should().Be("confirmed"); // NOT flipped
    }

    // ── Tenant isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_EventFromAnotherCompany_ThrowsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        SeedAdmin(context, DeciderUserId, CompanyId, isAdmin: true);
        SeedPendingAccident(context, id: 70, OtherCompanyId);
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new DismissAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<DismissAccidentCommandHandler>.Instance);

        var act = async () => await handler.Handle(new DismissAccidentCommand(70), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        var unchanged = await context.AccidentEvents.FindAsync(70);
        unchanged!.Status.Should().Be("pending");
    }

    [Fact]
    public async Task Handle_UnknownEventId_ThrowsNotFound()
    {
        using var context = TestDbContextFactory.Create();
        SeedAdmin(context, DeciderUserId, CompanyId, isAdmin: true);
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new DismissAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<DismissAccidentCommandHandler>.Instance);

        var act = async () => await handler.Handle(new DismissAccidentCommand(999), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── Guard clauses ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NoTenantContext_ThrowsDomainException()
    {
        using var context = TestDbContextFactory.Create();
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns((int?)null);
        tenant.Setup(x => x.UserId).Returns(DeciderUserId);

        var handler = new DismissAccidentCommandHandler(
            context, tenant.Object, new Mock<INotificationService>().Object,
            NullLogger<DismissAccidentCommandHandler>.Instance);

        var act = async () => await handler.Handle(new DismissAccidentCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Handle_NoUserContext_ThrowsDomainException()
    {
        using var context = TestDbContextFactory.Create();
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns(CompanyId);
        tenant.Setup(x => x.UserId).Returns((int?)null);

        var handler = new DismissAccidentCommandHandler(
            context, tenant.Object, new Mock<INotificationService>().Object,
            NullLogger<DismissAccidentCommandHandler>.Instance);

        var act = async () => await handler.Handle(new DismissAccidentCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    // ── Notification fan-out (admin-only, no drivers, no email) ─────────────

    [Fact]
    public async Task Handle_NotifiesOtherAdminsWithPossibleDamage_SkipsDecider_SkipsDrivers()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();

        // Decider (admin) — MUST NOT be notified (they just clicked)
        SeedAdmin(context, DeciderUserId, CompanyId, isAdmin: true);
        // Other active admin (same company) — MUST be notified
        const int OtherAdminId = 101;
        SeedAdmin(context, OtherAdminId, CompanyId, isAdmin: true);
        // Driver (non-admin role) — MUST NOT be notified (no panic ping)
        const int DriverId = 102;
        SeedAdmin(context, DriverId, CompanyId, isAdmin: false);
        // Inactive admin — MUST NOT be notified
        const int InactiveAdminId = 103;
        SeedAdmin(context, InactiveAdminId, CompanyId, isAdmin: true, status: "inactive");
        // Admin of another company — MUST NOT be notified
        const int ForeignAdminId = 200;
        SeedAdmin(context, ForeignAdminId, OtherCompanyId, isAdmin: true);

        SeedPendingAccident(context, id: 80, CompanyId);
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new DismissAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<DismissAccidentCommandHandler>.Instance);

        // Act
        await handler.Handle(new DismissAccidentCommand(80), CancellationToken.None);

        // Assert: exactly ONE notification, to the other active admin,
        // of type "accident_possible_damage", priority "high"
        notif.Verify(
            x => x.CreateAndSendAsync(
                CompanyId, OtherAdminId, "accident_possible_damage",
                "Choc violent détecté — dégâts possibles",
                It.IsAny<string>(), "high",
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // And: the decider was NOT notified
        notif.Verify(
            x => x.CreateAndSendAsync(
                It.IsAny<int>(), DeciderUserId, It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // And: drivers were NOT notified
        notif.Verify(
            x => x.CreateAndSendAsync(
                It.IsAny<int>(), DriverId, It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // And: inactive admins were NOT notified
        notif.Verify(
            x => x.CreateAndSendAsync(
                It.IsAny<int>(), InactiveAdminId, It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // And: admins of other companies were NOT notified
        notif.Verify(
            x => x.CreateAndSendAsync(
                It.IsAny<int>(), ForeignAdminId, It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NoOtherAdmins_StillFlipsStatusWithoutError()
    {
        using var context = TestDbContextFactory.Create();
        SeedAdmin(context, DeciderUserId, CompanyId, isAdmin: true);
        SeedPendingAccident(context, id: 90, CompanyId);
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new DismissAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<DismissAccidentCommandHandler>.Instance);

        await handler.Handle(new DismissAccidentCommand(90), CancellationToken.None);

        var reloaded = await context.AccidentEvents.FindAsync(90);
        reloaded!.Status.Should().Be("dismissed");

        notif.Verify(
            x => x.CreateAndSendAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
