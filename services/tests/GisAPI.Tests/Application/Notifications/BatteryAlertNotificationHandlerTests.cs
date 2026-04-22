using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Notifications;

/// <summary>
/// Covers the fan-out path of <c>BatteryAlertNotificationHandler</c> —
/// the MediatR handler triggered by <c>BatteryMonitoringService</c> once
/// it decides a NEMS L boîtier is reporting a dying battery.
///
/// <para>The contract we lock down here:</para>
/// <list type="bullet">
///   <item><description>Recipients = <b>active company admins only</b>
///     (conducteurs are excluded — they can't act on the alert anyway;
///     inactive admins are excluded — they've been offboarded).</description></item>
///   <item><description>Cross-tenant isolation: admins of other societies
///     are never paged.</description></item>
///   <item><description>No admin ⇒ no notifications sent, no exception.</description></item>
///   <item><description>Notification payload: type = <c>"low_voltage"</c>,
///     priority = <c>"normal"</c>, actionUrl = <c>"/vehicules"</c>,
///     metadata = <c>{deviceId, voltageRaw, detectedAt}</c>.</description></item>
///   <item><description>Vehicle label defaults to <c>"Véhicule #{VehicleId}"</c>
///     when <c>VehicleName</c> is null or whitespace.</description></item>
///   <item><description>A per-admin failure does not kill the rest of the
///     fan-out — the remaining admins still get their notification.</description></item>
/// </list>
///
/// <para>We use a real <see cref="TestGisDbContext"/> for the Users +
/// Roles query so the LINQ shape is validated end-to-end, and a mocked
/// <see cref="INotificationService"/> to capture the calls.</para>
/// </summary>
public class BatteryAlertNotificationHandlerTests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;
    private const int DeviceId = 500;
    private const int VehicleId = 700;

    private static void SeedUser(
        TestGisDbContext context,
        int id,
        int companyId,
        bool isAdmin,
        string status = "active")
    {
        var role = new Role
        {
            Id = 2000 + id,
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

    private static BatteryAlertNotificationHandler CreateHandler(
        TestGisDbContext context,
        Mock<INotificationService> notifMock)
    {
        return new BatteryAlertNotificationHandler(
            notifMock.Object,
            context,
            NullLogger<BatteryAlertNotificationHandler>.Instance);
    }

    private static BatteryAlertNotificationEvent DefaultEvent(
        string? vehicleName = "Véhicule 700",
        int voltageRaw = 30)
    {
        return new BatteryAlertNotificationEvent(
            CompanyId: CompanyId,
            DeviceId: DeviceId,
            VehicleId: VehicleId,
            VehicleName: vehicleName,
            VoltageRaw: voltageRaw,
            DetectedAt: new DateTime(2026, 4, 22, 10, 0, 0, DateTimeKind.Utc));
    }

    // ── Recipient filter ────────────────────────────────────────────────────

    [Fact]
    public async Task FanOut_DeliversOnlyToActiveAdminsOfSameCompany()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, id: 100, companyId: CompanyId,      isAdmin: true);                          // ✓
        SeedUser(context, id: 101, companyId: CompanyId,      isAdmin: true);                          // ✓
        SeedUser(context, id: 102, companyId: CompanyId,      isAdmin: false);                         // driver — skip
        SeedUser(context, id: 103, companyId: CompanyId,      isAdmin: true, status: "inactive");      // inactive — skip
        SeedUser(context, id: 200, companyId: OtherCompanyId, isAdmin: true);                          // other co — skip
        await context.SaveChangesAsync();

        var notif = new Mock<INotificationService>(MockBehavior.Strict);
        var delivered = new List<int>();
        notif
            .Setup(n => n.CreateAndSendAsync(
                It.IsAny<int>(),
                Capture.In(delivered),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Notification());

        await CreateHandler(context, notif).Handle(DefaultEvent(), CancellationToken.None);

        delivered.Should().BeEquivalentTo(new[] { 100, 101 });
    }

    [Fact]
    public async Task FanOut_NoAdmins_PerformsNoCallsAndDoesNotThrow()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, id: 102, companyId: CompanyId, isAdmin: false);                   // only drivers
        SeedUser(context, id: 103, companyId: CompanyId, isAdmin: true, status: "inactive"); // inactive admin
        await context.SaveChangesAsync();

        var notif = new Mock<INotificationService>(MockBehavior.Strict);

        await CreateHandler(context, notif).Handle(DefaultEvent(), CancellationToken.None);

        notif.Verify(n => n.CreateAndSendAsync(
            It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FanOut_CompanyWithoutAnyUsers_DoesNothing()
    {
        using var context = TestDbContextFactory.Create();
        await context.SaveChangesAsync();

        var notif = new Mock<INotificationService>(MockBehavior.Strict);

        await CreateHandler(context, notif).Handle(DefaultEvent(), CancellationToken.None);

        notif.VerifyNoOtherCalls();
    }

    // ── Payload content ─────────────────────────────────────────────────────

    [Fact]
    public async Task FanOut_PublishesLowVoltageTypeWithNormalPriorityAndCorrectActionUrl()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, id: 100, companyId: CompanyId, isAdmin: true);
        await context.SaveChangesAsync();

        var notif = new Mock<INotificationService>();
        notif.Setup(n => n.CreateAndSendAsync(
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Notification());

        await CreateHandler(context, notif).Handle(DefaultEvent(), CancellationToken.None);

        notif.Verify(n => n.CreateAndSendAsync(
            CompanyId,
            100,
            "low_voltage",
            It.Is<string>(t => t.Contains("Batterie faible")),
            It.Is<string>(m => m.Contains("tension batterie") && m.Contains("anormalement basse")),
            "normal",
            "vehicle",
            VehicleId,
            "/vehicules",
            It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FanOut_IncludesDeviceIdVoltageAndDetectedAtInMetadata()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, id: 100, companyId: CompanyId, isAdmin: true);
        await context.SaveChangesAsync();

        Dictionary<string, object>? captured = null;
        var notif = new Mock<INotificationService>();
        notif.Setup(n => n.CreateAndSendAsync(
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, string, string, string, string, string?, int?, string?, Dictionary<string, object>?, CancellationToken>(
                (_, _, _, _, _, _, _, _, _, meta, _) => captured = meta)
            .ReturnsAsync(new Notification());

        var evt = DefaultEvent(voltageRaw: 28);
        await CreateHandler(context, notif).Handle(evt, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!["deviceId"].Should().Be(DeviceId);
        captured["voltageRaw"].Should().Be(28);
        captured["detectedAt"].Should().Be(evt.DetectedAt.ToString("O"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FanOut_FallsBackToVehicleIdLabelWhenNameIsMissing(string? emptyName)
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, id: 100, companyId: CompanyId, isAdmin: true);
        await context.SaveChangesAsync();

        string? capturedTitle = null;
        string? capturedMessage = null;
        var notif = new Mock<INotificationService>();
        notif.Setup(n => n.CreateAndSendAsync(
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, string, string, string, string, string?, int?, string?, Dictionary<string, object>?, CancellationToken>(
                (_, _, _, t, m, _, _, _, _, _, _) => { capturedTitle = t; capturedMessage = m; })
            .ReturnsAsync(new Notification());

        await CreateHandler(context, notif).Handle(DefaultEvent(vehicleName: emptyName), CancellationToken.None);

        capturedTitle.Should().Contain($"Véhicule #{VehicleId}");
        capturedMessage.Should().Contain($"Véhicule #{VehicleId}");
    }

    [Fact]
    public async Task FanOut_PrefersVehicleNameOverFallbackWhenProvided()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, id: 100, companyId: CompanyId, isAdmin: true);
        await context.SaveChangesAsync();

        string? capturedTitle = null;
        var notif = new Mock<INotificationService>();
        notif.Setup(n => n.CreateAndSendAsync(
                It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, string, string, string, string, string?, int?, string?, Dictionary<string, object>?, CancellationToken>(
                (_, _, _, t, _, _, _, _, _, _, _) => capturedTitle = t)
            .ReturnsAsync(new Notification());

        await CreateHandler(context, notif).Handle(
            DefaultEvent(vehicleName: "TN-1234-XX"),
            CancellationToken.None);

        capturedTitle.Should().Contain("TN-1234-XX");
        capturedTitle.Should().NotContain($"Véhicule #{VehicleId}");
    }

    // ── Resilience ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FanOut_ContinuesAfterPerAdminFailure()
    {
        using var context = TestDbContextFactory.Create();
        SeedUser(context, id: 100, companyId: CompanyId, isAdmin: true);
        SeedUser(context, id: 101, companyId: CompanyId, isAdmin: true);
        SeedUser(context, id: 102, companyId: CompanyId, isAdmin: true);
        await context.SaveChangesAsync();

        var delivered = new List<int>();
        var notif = new Mock<INotificationService>();
        notif.Setup(n => n.CreateAndSendAsync(
                It.IsAny<int>(),
                Capture.In(delivered),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .Returns<int, int, string, string, string, string, string?, int?, string?, Dictionary<string, object>?, CancellationToken>(
                (_, userId, _, _, _, _, _, _, _, _, _) =>
                    userId == 101
                        ? throw new InvalidOperationException("transient SignalR failure")
                        : Task.FromResult(new Notification()));

        // Act — must not throw even though admin 101 fails
        var act = () => CreateHandler(context, notif).Handle(DefaultEvent(), CancellationToken.None);
        await act.Should().NotThrowAsync();

        // The failing admin was still attempted, and the other two went through.
        delivered.Should().Contain(new[] { 100, 101, 102 });
    }

    [Fact]
    public async Task FanOut_DbFailureIsSwallowedToAvoidHostedServiceCrash()
    {
        // Simulate a DB failure by disposing the context before the handler runs.
        // The handler must log + swallow — a BackgroundService that crashes here
        // would take down the whole host.
        var context = TestDbContextFactory.Create();
        context.Dispose();

        var notif = new Mock<INotificationService>(MockBehavior.Strict);

        var handler = new BatteryAlertNotificationHandler(
            notif.Object,
            context,
            NullLogger<BatteryAlertNotificationHandler>.Instance);

        var act = () => handler.Handle(DefaultEvent(), CancellationToken.None);
        await act.Should().NotThrowAsync();

        notif.VerifyNoOtherCalls();
    }
}
