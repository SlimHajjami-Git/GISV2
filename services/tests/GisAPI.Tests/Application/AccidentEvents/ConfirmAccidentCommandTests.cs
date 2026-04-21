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
/// Covers the decision workflow for <see cref="ConfirmAccidentCommand"/>:
///   • happy-path flips <c>status = "confirmed"</c> and stamps the decider,
///   • the second call on an already-decided event is a no-op (idempotent),
///   • a cross-tenant attempt yields <see cref="NotFoundException"/>,
///   • audit notifications are fanned to every OTHER active company admin
///     (decider excluded, drivers excluded, wrong-company admins excluded).
/// </summary>
public class ConfirmAccidentCommandTests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;
    private const int DeciderUserId = 100;

    // Small helper that seeds a role + user and returns the user id.
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

    private static AccidentEvent SeedPendingAccident(TestGisDbContext context, int id, int companyId, string? vehicleLabel = "TU-0001")
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
            VehicleLabel = vehicleLabel,
            Confidence = 92,
            Status = "pending"
        };
        context.AccidentEvents.Add(ev);
        return ev;
    }

    // ── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_PendingEvent_FlipsToConfirmedAndStampsDecider()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        SeedAdmin(context, DeciderUserId, CompanyId, isAdmin: true);
        SeedPendingAccident(context, id: 50, CompanyId);
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new ConfirmAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<ConfirmAccidentCommandHandler>.Instance);

        var before = DateTime.UtcNow;

        // Act
        var result = await handler.Handle(new ConfirmAccidentCommand(50), CancellationToken.None);

        // Assert
        result.Should().Be(Unit.Value);
        var after = DateTime.UtcNow;

        var reloaded = await context.AccidentEvents.FindAsync(50);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be("confirmed");
        reloaded.DecidedByUserId.Should().Be(DeciderUserId);
        reloaded.DecidedAt.Should().NotBeNull();
        reloaded.DecidedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        reloaded.TowDetectedAt.Should().BeNull();
    }

    // ── Idempotency ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_AlreadyConfirmedEvent_IsNoOp()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        SeedAdmin(context, DeciderUserId, CompanyId, isAdmin: true);
        var alreadyDecidedAt = DateTime.UtcNow.AddHours(-2);
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = 60,
            CompanyId = CompanyId,
            DeviceUid = "IMEI-TEST",
            IncidentAt = DateTime.UtcNow.AddHours(-3),
            Latitude = 36.8,
            Longitude = 10.18,
            Confidence = 90,
            Status = "confirmed",
            DecidedByUserId = 999,          // previous decider
            DecidedAt = alreadyDecidedAt
        });
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new ConfirmAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<ConfirmAccidentCommandHandler>.Instance);

        // Act
        await handler.Handle(new ConfirmAccidentCommand(60), CancellationToken.None);

        // Assert: row untouched
        var reloaded = await context.AccidentEvents.FindAsync(60);
        reloaded!.Status.Should().Be("confirmed");
        reloaded.DecidedByUserId.Should().Be(999);
        reloaded.DecidedAt.Should().BeCloseTo(alreadyDecidedAt, TimeSpan.FromSeconds(1));

        // And: no audit notification emitted for an already-decided event
        notif.Verify(
            x => x.CreateAndSendAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyDismissedEvent_IsNoOp_DoesNotFlipBack()
    {
        // Arrange: a "dismissed" event must stay dismissed
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
            Status = "dismissed",
            DecidedByUserId = 7,
            DecidedAt = DateTime.UtcNow.AddMinutes(-30)
        });
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new ConfirmAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<ConfirmAccidentCommandHandler>.Instance);

        // Act
        await handler.Handle(new ConfirmAccidentCommand(61), CancellationToken.None);

        // Assert
        var reloaded = await context.AccidentEvents.FindAsync(61);
        reloaded!.Status.Should().Be("dismissed"); // NOT flipped
    }

    // ── Tenant isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_EventFromAnotherCompany_ThrowsNotFound()
    {
        // Arrange: event belongs to company 2, caller is admin of company 1
        using var context = TestDbContextFactory.Create();
        SeedAdmin(context, DeciderUserId, CompanyId, isAdmin: true);
        SeedPendingAccident(context, id: 70, OtherCompanyId);
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new ConfirmAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<ConfirmAccidentCommandHandler>.Instance);

        // Act
        var act = async () => await handler.Handle(new ConfirmAccidentCommand(70), CancellationToken.None);

        // Assert
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
        var handler = new ConfirmAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<ConfirmAccidentCommandHandler>.Instance);

        var act = async () => await handler.Handle(new ConfirmAccidentCommand(999), CancellationToken.None);

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

        var notif = new Mock<INotificationService>();
        var handler = new ConfirmAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<ConfirmAccidentCommandHandler>.Instance);

        var act = async () => await handler.Handle(new ConfirmAccidentCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    [Fact]
    public async Task Handle_NoUserContext_ThrowsDomainException()
    {
        using var context = TestDbContextFactory.Create();
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns(CompanyId);
        tenant.Setup(x => x.UserId).Returns((int?)null);

        var notif = new Mock<INotificationService>();
        var handler = new ConfirmAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<ConfirmAccidentCommandHandler>.Instance);

        var act = async () => await handler.Handle(new ConfirmAccidentCommand(1), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
    }

    // ── Audit notification fan-out ──────────────────────────────────────────

    [Fact]
    public async Task Handle_NotifiesEveryOtherActiveAdmin_NotTheDeciderNotDrivers()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();

        // Decider (admin, company 1) — MUST be skipped
        SeedAdmin(context, DeciderUserId, CompanyId, isAdmin: true);
        // Second admin (company 1) — MUST be notified
        const int OtherAdminId = 101;
        SeedAdmin(context, OtherAdminId, CompanyId, isAdmin: true);
        // Driver (company 1, non-admin role) — MUST NOT be notified
        const int DriverId = 102;
        SeedAdmin(context, DriverId, CompanyId, isAdmin: false);
        // Inactive admin (company 1) — MUST NOT be notified
        const int InactiveAdminId = 103;
        SeedAdmin(context, InactiveAdminId, CompanyId, isAdmin: true, status: "inactive");
        // Admin of ANOTHER company — MUST NOT be notified
        const int ForeignAdminId = 200;
        SeedAdmin(context, ForeignAdminId, OtherCompanyId, isAdmin: true);

        SeedPendingAccident(context, id: 80, CompanyId, vehicleLabel: "TU-0001");
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new ConfirmAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<ConfirmAccidentCommandHandler>.Instance);

        // Act
        await handler.Handle(new ConfirmAccidentCommand(80), CancellationToken.None);

        // Assert: exactly ONE audit notification, sent to the other admin
        notif.Verify(
            x => x.CreateAndSendAsync(
                CompanyId, OtherAdminId, "accident_decision",
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
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
        // Arrange: the decider is the ONLY admin of the company
        using var context = TestDbContextFactory.Create();
        SeedAdmin(context, DeciderUserId, CompanyId, isAdmin: true);
        SeedPendingAccident(context, id: 90, CompanyId);
        await context.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: DeciderUserId);
        var notif = new Mock<INotificationService>();
        var handler = new ConfirmAccidentCommandHandler(
            context, tenant.Object, notif.Object,
            NullLogger<ConfirmAccidentCommandHandler>.Instance);

        // Act
        await handler.Handle(new ConfirmAccidentCommand(90), CancellationToken.None);

        // Assert: status updated, no notification emitted
        var reloaded = await context.AccidentEvents.FindAsync(90);
        reloaded!.Status.Should().Be("confirmed");

        notif.Verify(
            x => x.CreateAndSendAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
