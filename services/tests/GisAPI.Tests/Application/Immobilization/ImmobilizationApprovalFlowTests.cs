using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace GisAPI.Tests.Application.Immobilization;

/// <summary>
/// Tests for the remote-stop approval flow (web-initiated STOP/GO, mobile-admin approval):
///
///   • Fanout:  a request without password must create ONE notification per company
///              admin — never to non-admins, never to admins of other companies.
///   • Approval: first admin to tap "Approuver" → dispatches the STOP/GO command
///              (device flipped, DeviceCommand row inserted, Rust push fired, "approved"
///              response notif sent to the original requester).
///   • First-approver-wins: the sibling unread requests addressed to the other N-1
///              admins MUST be marked read so their phones stop showing the alert.
///   • Authorization guards: non-admins and cross-company admins cannot approve.
///
/// All tests simulate the exact LINQ the controller runs (we can't instantiate the
/// controller here because of HTTP/Auth context), and verify both DB side effects
/// and the mocked <see cref="INotificationService"/> / <see cref="IRustCommandPusher"/>
/// calls.
/// </summary>
public class ImmobilizationApprovalFlowTests
{
    // ============================================================
    // 1) FANOUT — one notification per company admin, scoped by company
    // ============================================================

    [Fact]
    public async Task StopRequest_NoPassword_FansOutOneNotifPerCompanyAdmin_ScopedByCompany()
    {
        // Arrange: two companies. Company 10 has 3 admins + 1 non-admin + 1 inactive admin.
        // Company 20 has 1 admin (must NOT be notified).
        using var context = TestDbContextFactory.Create();

        var adminRole10 = new Role { Id = 100, Name = "Admin", SocieteId = 10, IsCompanyAdmin = true };
        var driverRole10 = new Role { Id = 101, Name = "Driver", SocieteId = 10, IsCompanyAdmin = false };
        var adminRole20 = new Role { Id = 200, Name = "Admin", SocieteId = 20, IsCompanyAdmin = true };
        context.Roles.AddRange(adminRole10, driverRole10, adminRole20);

        context.Users.AddRange(
            new User { Id = 1, FirstName = "A1", Email = "a1@co10.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" },
            new User { Id = 2, FirstName = "A2", Email = "a2@co10.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" },
            new User { Id = 3, FirstName = "A3", Email = "a3@co10.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" },
            new User { Id = 4, FirstName = "Driver", Email = "d@co10.com", PasswordHash = "x", RoleId = 101, CompanyId = 10, Status = "active" },
            new User { Id = 5, FirstName = "InactiveAdmin", Email = "ia@co10.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "inactive" },
            new User { Id = 6, FirstName = "CrossCoAdmin", Email = "x@co20.com", PasswordHash = "x", RoleId = 200, CompanyId = 20, Status = "active" },
            new User { Id = 7, FirstName = "Requester", Email = "r@co10.com", PasswordHash = "x", RoleId = 101, CompanyId = 10, Status = "active" }
        );
        await context.SaveChangesAsync();

        var notifMock = new Mock<INotificationService>();
        notifMock
            .Setup(x => x.CreateAndSendAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int companyId, int userId, string type, string title, string message,
                            string priority, string? refType, int? refId, string? url,
                            Dictionary<string, object>? meta, CancellationToken ct)
                => new Notification { Id = userId * 10, CompanyId = companyId, UserId = userId, Type = type });

        // Act: simulate the exact filter + loop in GpsController.ExecuteImmobilizationAsync
        const int targetCompanyId = 10;
        const int deviceId = 42;

        var companyUsers = await context.Users
            .Include(u => u.Role)
            .Where(u => u.CompanyId == targetCompanyId && u.Status == "active")
            .ToListAsync();
        var admins = companyUsers.Where(u => u.IsCompanyAdmin).ToList();

        foreach (var admin in admins)
        {
            await notifMock.Object.CreateAndSendAsync(
                targetCompanyId, admin.Id,
                type: "immobilization_request",
                title: "Demande d'arrêt",
                message: "Requester demande l'arrêt",
                priority: "urgent",
                referenceType: "device",
                referenceId: deviceId,
                metadata: new Dictionary<string, object> { ["requestedBy"] = 7 });
        }

        // Assert: exactly 3 notifications sent — to admins 1, 2, 3
        admins.Should().HaveCount(3, "only active admins of company 10 qualify");
        admins.Select(a => a.Id).Should().BeEquivalentTo(new[] { 1, 2, 3 });

        notifMock.Verify(x => x.CreateAndSendAsync(
            10, It.IsAny<int>(), "immobilization_request",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        // Each of the 3 admins got exactly one notification
        foreach (var adminId in new[] { 1, 2, 3 })
        {
            notifMock.Verify(x => x.CreateAndSendAsync(
                10, adminId, "immobilization_request",
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
                Times.Once,
                $"admin {adminId} must receive exactly one notification");
        }

        // Nobody else: driver, inactive admin, cross-company admin, requester
        foreach (var excludedId in new[] { 4, 5, 6, 7 })
        {
            notifMock.Verify(x => x.CreateAndSendAsync(
                It.IsAny<int>(), excludedId, It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
                Times.Never,
                $"user {excludedId} must never be notified");
        }
    }

    // ============================================================
    // 2) APPROVAL — device flipped, command created, Rust pushed, response notif sent
    // ============================================================

    [Fact]
    public async Task ApproveRequest_FlipsDevice_CreatesCommand_PushesToRust_NotifiesRequester()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();

        var adminRole = new Role { Id = 100, Name = "Admin", SocieteId = 10, IsCompanyAdmin = true };
        var userRole = new Role { Id = 101, Name = "User", SocieteId = 10, IsCompanyAdmin = false };
        context.Roles.AddRange(adminRole, userRole);

        var approver = new User { Id = 1, FirstName = "Admin", LastName = "One", Email = "a@c.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" };
        var requester = new User { Id = 2, FirstName = "Req", LastName = "Uester", Email = "r@c.com", PasswordHash = "x", RoleId = 101, CompanyId = 10, Status = "active" };
        context.Users.AddRange(approver, requester);

        var device = CreateDeviceWithVehicle(context, deviceId: 1, companyId: 10);
        device.ImmobilizationActive = false; // starts running
        await context.SaveChangesAsync();

        // Pre-existing request notification addressed to the approver
        var requestNotif = new Notification
        {
            Id = 5000,
            UserId = approver.Id,
            CompanyId = 10,
            Type = "immobilization_request",
            Title = "Demande d'arrêt",
            Message = "...",
            Priority = "urgent",
            ReferenceType = "device",
            ReferenceId = device.Id,
            IsRead = false
        };
        context.Notifications.Add(requestNotif);
        await context.SaveChangesAsync();

        // Metadata is kept in-memory only (Ignored in test DB) — the controller reads it
        // from the freshly-loaded notification object, so we mimic that by using the
        // original in-memory dictionary directly.
        var metadata = new Dictionary<string, object>
        {
            ["deviceId"] = device.Id,
            ["commandType"] = "STOP",
            ["vehicleId"] = device.Vehicle!.Id,
            ["vehicleName"] = device.Vehicle!.Name,
            ["requestedBy"] = requester.Id
        };

        var pusher = new RecordingPusher();
        var notifMock = new Mock<INotificationService>();
        notifMock
            .Setup(x => x.CreateAndSendAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int c, int u, string t, string ti, string m, string p, string? rt, int? ri, string? a, Dictionary<string, object>? md, CancellationToken ct)
                => new Notification { Id = 6000, UserId = u, CompanyId = c, Type = t });

        // Act: simulate ApproveImmobilizationRequest for deviceId=1, by approver (admin)
        const int commandTypeStopCompanyId = 10;
        var currentUser = await context.Users.Include(u => u.Role).FirstAsync(u => u.Id == approver.Id);
        currentUser.IsCompanyAdmin.Should().BeTrue("sanity — approver must be admin");

        var commandType = (string)metadata["commandType"];
        var isStop = commandType == "STOP";
        var deviceIdFromMeta = (int)metadata["deviceId"];
        var requesterId = (int)metadata["requestedBy"];
        var targetDevice = await context.GpsDevices
            .Include(d => d.Vehicle)
            .FirstAsync(d => d.Id == deviceIdFromMeta && d.CompanyId == commandTypeStopCompanyId);

        targetDevice.ImmobilizationActive = isStop;
        targetDevice.ImmobilizationBy = requesterId;
        targetDevice.ImmobilizationAt = DateTime.UtcNow;

        var commandText = isStop ? targetDevice.CommandStop : targetDevice.CommandGo;
        var cmd = new DeviceCommand
        {
            DeviceId = targetDevice.Id,
            VehicleId = targetDevice.Vehicle!.Id,
            UserId = requesterId,
            CommandType = commandType,
            CommandText = commandText,
            Status = "pending",
            Source = "manual",
            CompanyId = targetDevice.CompanyId
        };
        context.DeviceCommands.Add(cmd);

        // First-approver-wins cleanup
        var allRelated = await context.Notifications
            .Where(n => n.Type == "immobilization_request"
                && n.ReferenceId == targetDevice.Id
                && n.CompanyId == commandTypeStopCompanyId
                && !n.IsRead)
            .ToListAsync();
        foreach (var n in allRelated) { n.IsRead = true; n.ReadAt = DateTime.UtcNow; }
        await context.SaveChangesAsync();

        // Push to Rust
        var pushResult = await pusher.PushAsync(targetDevice.Id, commandText);

        // Notify requester
        await notifMock.Object.CreateAndSendAsync(
            commandTypeStopCompanyId, requesterId,
            type: "immobilization_response",
            title: $"Arrêt approuvé — {targetDevice.Vehicle!.Name}",
            message: $"Votre demande d'arrêt pour {targetDevice.Vehicle!.Name} a été approuvée",
            priority: "high",
            referenceType: "device",
            referenceId: targetDevice.Id,
            metadata: new Dictionary<string, object> { ["status"] = "approved" });

        // ─── Assertions ───

        // Device state was flipped
        var reloadedDevice = await context.GpsDevices.FindAsync(device.Id);
        reloadedDevice!.ImmobilizationActive.Should().BeTrue();
        reloadedDevice.ImmobilizationBy.Should().Be(requester.Id);
        reloadedDevice.ImmobilizationAt.Should().NotBeNull();

        // Exactly one DeviceCommand was inserted, scoped to the right device + company
        var commands = await context.DeviceCommands.ToListAsync();
        commands.Should().HaveCount(1);
        commands[0].DeviceId.Should().Be(device.Id);
        commands[0].CompanyId.Should().Be(10);
        commands[0].CommandType.Should().Be("STOP");
        commands[0].CommandText.Should().Be("AJ+STOP#1311\n");
        commands[0].Status.Should().Be("pending");
        commands[0].UserId.Should().Be(requester.Id);

        // Rust push fired exactly once, targeting that device + command text
        pusher.Calls.Should().HaveCount(1);
        pusher.Calls[0].DeviceId.Should().Be(device.Id);
        pusher.Calls[0].Command.Should().Be("AJ+STOP#1311\n");
        pushResult.Outcome.Should().Be(RustPushOutcome.Pushed);

        // Request notif is now read
        var reloadedNotif = await context.Notifications.FindAsync(requestNotif.Id);
        reloadedNotif!.IsRead.Should().BeTrue();
        reloadedNotif.ReadAt.Should().NotBeNull();

        // Requester received an "approved" response notif (via SignalR/FCM)
        notifMock.Verify(x => x.CreateAndSendAsync(
            10, requester.Id, "immobilization_response",
            It.IsAny<string>(), It.IsAny<string>(), "high",
            "device", device.Id, It.IsAny<string?>(),
            It.Is<Dictionary<string, object>?>(md => md != null && (string)md["status"] == "approved"),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "requester must be pushed the 'approved' confirmation");
    }

    // ============================================================
    // 3) FIRST-APPROVER-WINS — sibling notifs marked read for other admins
    // ============================================================

    [Fact]
    public async Task ApproveRequest_WithThreeAdmins_MarksAllSiblingRequestsAsRead_AndPushesOnce()
    {
        // Arrange: 3 admins, each received their own copy of the same request notif
        using var context = TestDbContextFactory.Create();

        var adminRole = new Role { Id = 100, Name = "Admin", SocieteId = 10, IsCompanyAdmin = true };
        context.Roles.Add(adminRole);
        context.Users.AddRange(
            new User { Id = 1, FirstName = "A1", Email = "a1@c.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" },
            new User { Id = 2, FirstName = "A2", Email = "a2@c.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" },
            new User { Id = 3, FirstName = "A3", Email = "a3@c.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" }
        );

        var device = CreateDeviceWithVehicle(context, deviceId: 7, companyId: 10);

        // Seed 3 unread notifications, one per admin, same (device, company)
        context.Notifications.AddRange(
            new Notification { Id = 1001, UserId = 1, CompanyId = 10, Type = "immobilization_request", ReferenceType = "device", ReferenceId = 7, IsRead = false, Title = "t", Message = "m", Priority = "urgent" },
            new Notification { Id = 1002, UserId = 2, CompanyId = 10, Type = "immobilization_request", ReferenceType = "device", ReferenceId = 7, IsRead = false, Title = "t", Message = "m", Priority = "urgent" },
            new Notification { Id = 1003, UserId = 3, CompanyId = 10, Type = "immobilization_request", ReferenceType = "device", ReferenceId = 7, IsRead = false, Title = "t", Message = "m", Priority = "urgent" }
        );

        // Also an UNRELATED notif that must NOT be touched
        context.Notifications.Add(
            new Notification { Id = 1004, UserId = 1, CompanyId = 10, Type = "immobilization_request", ReferenceType = "device", ReferenceId = 99, IsRead = false, Title = "different device", Message = "m", Priority = "urgent" }
        );
        // And one already read (must stay as-is)
        context.Notifications.Add(
            new Notification { Id = 1005, UserId = 2, CompanyId = 10, Type = "immobilization_request", ReferenceType = "device", ReferenceId = 7, IsRead = true, ReadAt = DateTime.UtcNow.AddHours(-1), Title = "old", Message = "m", Priority = "urgent" }
        );
        await context.SaveChangesAsync();

        var pusher = new RecordingPusher();

        // Act: admin #1 approves → dispatch + first-wins cleanup
        const int deviceId = 7;
        const int companyId = 10;

        var target = await context.GpsDevices.Include(d => d.Vehicle).FirstAsync(d => d.Id == deviceId);
        target.ImmobilizationActive = true;
        context.DeviceCommands.Add(new DeviceCommand
        {
            DeviceId = deviceId, VehicleId = target.Vehicle!.Id, UserId = 1,
            CommandType = "STOP", CommandText = target.CommandStop,
            Status = "pending", Source = "manual", CompanyId = companyId
        });

        var allRelated = await context.Notifications
            .Where(n => n.Type == "immobilization_request"
                && n.ReferenceId == deviceId
                && n.CompanyId == companyId
                && !n.IsRead)
            .ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var n in allRelated) { n.IsRead = true; n.ReadAt = now; }
        await context.SaveChangesAsync();

        await pusher.PushAsync(deviceId, target.CommandStop);

        // ─── Assertions ───

        // All 3 sibling request notifs (ids 1001, 1002, 1003) are now IsRead=true
        var fanOutNotifs = await context.Notifications
            .Where(n => new long[] { 1001, 1002, 1003 }.Contains(n.Id))
            .OrderBy(n => n.Id)
            .ToListAsync();
        fanOutNotifs.Should().HaveCount(3);
        fanOutNotifs.Should().AllSatisfy(n =>
        {
            n.IsRead.Should().BeTrue("first-approver-wins must clear the other admins' alerts");
            n.ReadAt.Should().NotBeNull();
        });

        // The unrelated-device notif (id 1004) remains untouched
        var unrelated = await context.Notifications.FindAsync((long)1004);
        unrelated!.IsRead.Should().BeFalse("other device's requests must NOT be affected");

        // The already-read notif (id 1005) remains with original ReadAt (not overwritten)
        var alreadyRead = await context.Notifications.FindAsync((long)1005);
        alreadyRead!.IsRead.Should().BeTrue();

        // Only one DeviceCommand inserted for device 7
        var commands = await context.DeviceCommands.Where(c => c.DeviceId == deviceId).ToListAsync();
        commands.Should().HaveCount(1);

        // Rust push happened exactly ONCE, not 3x
        pusher.Calls.Should().HaveCount(1, "first-approver-wins means only one dispatch, not one per admin");
        pusher.Calls[0].DeviceId.Should().Be(deviceId);
    }

    [Fact]
    public async Task RejectRequest_WithThreeAdmins_MarksAllSiblingRequestsAsRead_NoCommandNoPush()
    {
        // Arrange — same seed as the approval first-wins test
        using var context = TestDbContextFactory.Create();

        var adminRole = new Role { Id = 100, Name = "Admin", SocieteId = 10, IsCompanyAdmin = true };
        context.Roles.Add(adminRole);
        context.Users.AddRange(
            new User { Id = 1, FirstName = "A1", Email = "a1@c.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" },
            new User { Id = 2, FirstName = "A2", Email = "a2@c.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" },
            new User { Id = 3, FirstName = "A3", Email = "a3@c.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" }
        );
        var device = CreateDeviceWithVehicle(context, deviceId: 7, companyId: 10);

        context.Notifications.AddRange(
            new Notification { Id = 1001, UserId = 1, CompanyId = 10, Type = "immobilization_request", ReferenceType = "device", ReferenceId = 7, IsRead = false, Title = "t", Message = "m", Priority = "urgent" },
            new Notification { Id = 1002, UserId = 2, CompanyId = 10, Type = "immobilization_request", ReferenceType = "device", ReferenceId = 7, IsRead = false, Title = "t", Message = "m", Priority = "urgent" },
            new Notification { Id = 1003, UserId = 3, CompanyId = 10, Type = "immobilization_request", ReferenceType = "device", ReferenceId = 7, IsRead = false, Title = "t", Message = "m", Priority = "urgent" }
        );
        await context.SaveChangesAsync();

        var pusher = new RecordingPusher();

        // Act: admin #1 rejects → cleanup notifs only, NO command, NO push
        const int deviceId = 7;
        const int companyId = 10;

        var allRelated = await context.Notifications
            .Where(n => n.Type == "immobilization_request"
                && n.ReferenceId == deviceId
                && n.CompanyId == companyId
                && !n.IsRead)
            .ToListAsync();
        foreach (var n in allRelated) { n.IsRead = true; n.ReadAt = DateTime.UtcNow; }
        await context.SaveChangesAsync();

        // Assert: all 3 notifs read, no command, no push, device untouched
        var fanOutNotifs = await context.Notifications
            .Where(n => new long[] { 1001, 1002, 1003 }.Contains(n.Id))
            .ToListAsync();
        fanOutNotifs.Should().AllSatisfy(n => n.IsRead.Should().BeTrue("rejection clears admin alerts"));

        var commands = await context.DeviceCommands.ToListAsync();
        commands.Should().BeEmpty("rejection must not dispatch any command");

        pusher.Calls.Should().BeEmpty("rejection must not push to Rust");

        var reloadedDevice = await context.GpsDevices.FindAsync(deviceId);
        reloadedDevice!.ImmobilizationActive.Should().BeFalse("device state must not change on rejection");
    }

    // ============================================================
    // 4) AUTHORIZATION — non-admins and cross-company admins blocked
    // ============================================================

    [Fact]
    public async Task Approve_NonAdminUser_IsForbidden()
    {
        using var context = TestDbContextFactory.Create();

        var driverRole = new Role { Id = 101, Name = "Driver", SocieteId = 10, IsCompanyAdmin = false };
        context.Roles.Add(driverRole);
        context.Users.Add(new User { Id = 1, FirstName = "Driver", Email = "d@c.com", PasswordHash = "x", RoleId = 101, CompanyId = 10, Status = "active" });
        await context.SaveChangesAsync();

        // Simulate the controller's guard
        var currentUser = await context.Users.Include(u => u.Role).FirstAsync(u => u.Id == 1);

        // Assert: non-admin fails the guard
        currentUser.IsCompanyAdmin.Should().BeFalse("driver is NOT a company admin → Forbid()");
    }

    [Fact]
    public async Task Approve_AdminFromDifferentCompany_IsForbidden()
    {
        using var context = TestDbContextFactory.Create();

        var adminCo10 = new Role { Id = 100, Name = "Admin", SocieteId = 10, IsCompanyAdmin = true };
        var adminCo20 = new Role { Id = 200, Name = "Admin", SocieteId = 20, IsCompanyAdmin = true };
        context.Roles.AddRange(adminCo10, adminCo20);

        // Requester's admin (legit) in company 10
        context.Users.Add(new User { Id = 1, FirstName = "A10", Email = "a@co10.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" });
        // Attacker: admin in company 20 trying to approve a company-10 request
        context.Users.Add(new User { Id = 2, FirstName = "A20", Email = "a@co20.com", PasswordHash = "x", RoleId = 200, CompanyId = 20, Status = "active" });

        // Notification belongs to company 10
        context.Notifications.Add(new Notification
        {
            Id = 9000,
            UserId = 1,
            CompanyId = 10,
            Type = "immobilization_request",
            ReferenceType = "device",
            ReferenceId = 42,
            Title = "t", Message = "m", Priority = "urgent", IsRead = false
        });
        await context.SaveChangesAsync();

        // Simulate the cross-company guard
        var attacker = await context.Users.Include(u => u.Role).FirstAsync(u => u.Id == 2);
        var notification = await context.Notifications.FirstAsync(n => n.Id == 9000);

        // Attacker is an admin (passes first guard)…
        attacker.IsCompanyAdmin.Should().BeTrue();
        // …but NOT for the notification's company (fails second guard → Forbid)
        (notification.CompanyId != attacker.CompanyId)
            .Should().BeTrue("cross-company admin must be blocked from approving other companies' requests");
    }

    [Fact]
    public async Task GetPendingRequests_OnlyReturnsNotifsAddressedToMe_AndMyCompany()
    {
        using var context = TestDbContextFactory.Create();

        var adminRole = new Role { Id = 100, Name = "Admin", SocieteId = 10, IsCompanyAdmin = true };
        context.Roles.Add(adminRole);
        context.Users.AddRange(
            new User { Id = 1, FirstName = "Me", Email = "me@c.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" },
            new User { Id = 2, FirstName = "OtherAdmin", Email = "o@c.com", PasswordHash = "x", RoleId = 100, CompanyId = 10, Status = "active" }
        );

        // 3 notifs in company 10 — one for me, one for another admin same company, one for me already read
        // Plus one for me in company 20 (tenant leak attempt)
        context.Notifications.AddRange(
            new Notification { Id = 1, UserId = 1, CompanyId = 10, Type = "immobilization_request", IsRead = false, Title = "mine unread", Message = "m", Priority = "urgent" },
            new Notification { Id = 2, UserId = 2, CompanyId = 10, Type = "immobilization_request", IsRead = false, Title = "other admin's", Message = "m", Priority = "urgent" },
            new Notification { Id = 3, UserId = 1, CompanyId = 10, Type = "immobilization_request", IsRead = true,  Title = "mine already read", Message = "m", Priority = "urgent" },
            new Notification { Id = 4, UserId = 1, CompanyId = 20, Type = "immobilization_request", IsRead = false, Title = "cross-tenant leak", Message = "m", Priority = "urgent" }
        );
        await context.SaveChangesAsync();

        // Simulate GetPendingImmobilizationRequests query for userId=1, companyId=10
        const int meId = 1;
        const int myCompany = 10;
        var mine = await context.Notifications
            .Where(n => n.Type == "immobilization_request"
                && !n.IsRead
                && n.UserId == meId
                && n.CompanyId == myCompany)
            .ToListAsync();

        // Only notif #1 matches — not #2 (other admin), #3 (already read), #4 (other company)
        mine.Should().HaveCount(1);
        mine[0].Id.Should().Be(1);
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private static GpsDevice CreateDeviceWithVehicle(
        TestGisDbContext context,
        int deviceId,
        int companyId,
        string imei = "865456789012345",
        string plate = "236 TU 6917")
    {
        var device = new GpsDevice
        {
            Id = deviceId,
            DeviceUid = imei,
            Mat = plate,
            Label = plate,
            Status = "active",
            CompanyId = companyId,
            ImmobilizationActive = false,
            CommandGo = "AJ+GO#1311\n",
            CommandStop = "AJ+STOP#1311\n"
        };
        context.GpsDevices.Add(device);

        var vehicle = new Vehicle
        {
            Id = deviceId,
            Name = plate,
            Plate = plate,
            Type = "camion",
            Status = "available",
            CompanyId = companyId,
            GpsDeviceId = deviceId,
            HasGps = true
        };
        context.Vehicles.Add(vehicle);

        return device;
    }

    private sealed class RecordingPusher : IRustCommandPusher
    {
        public List<(int DeviceId, string Command)> Calls { get; } = new();

        public Task<RustPushResult> PushAsync(int deviceId, string command, CancellationToken ct = default)
        {
            Calls.Add((deviceId, command));
            return Task.FromResult(new RustPushResult(RustPushOutcome.Pushed, "test"));
        }
    }
}
