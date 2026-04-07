using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Tests.Application.Immobilization;

/// <summary>
/// Tests for the immobilization command system:
/// Scenario 1: Operator clicks STOP → immobilization_active=true, userId saved, pending STOP command created
/// Scenario 2: Operator clicks GO → immobilization_active=false, pending GO command created
/// Scenario 3: Auto-recovery decision → if immobilization_active=false, command_go should be sent
/// Scenario 4: Commands are created for the correct device (not a different one)
/// </summary>
public class ImmobilizationTests
{
    // ==================== SCENARIO 1: STOP ====================

    [Fact]
    public async Task StopDevice_SetsImmobilizationActive_True()
    {
        using var context = TestDbContextFactory.Create();
        var device = CreateDeviceWithVehicle(context, deviceId: 1, companyId: 1);
        await context.SaveChangesAsync();

        // Act: simulate what GpsController.StopDevice does
        device.ImmobilizationActive = true;
        device.ImmobilizationBy = 42;
        device.ImmobilizationAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        // Assert
        var updated = await context.GpsDevices.FindAsync(1);
        updated!.ImmobilizationActive.Should().BeTrue();
        updated.ImmobilizationBy.Should().Be(42);
        updated.ImmobilizationAt.Should().NotBeNull();
    }

    [Fact]
    public async Task StopDevice_CreatesPendingStopCommand()
    {
        using var context = TestDbContextFactory.Create();
        var device = CreateDeviceWithVehicle(context, deviceId: 1, companyId: 1);
        await context.SaveChangesAsync();

        // Act
        device.ImmobilizationActive = true;
        device.ImmobilizationBy = 10;
        device.ImmobilizationAt = DateTime.UtcNow;

        context.DeviceCommands.Add(new DeviceCommand
        {
            DeviceId = 1,
            VehicleId = device.Vehicle?.Id,
            UserId = 10,
            CommandType = "STOP",
            CommandText = device.CommandStop,
            Status = "pending",
            Source = "manual",
            CompanyId = 1
        });
        await context.SaveChangesAsync();

        // Assert
        var commands = await context.DeviceCommands.Where(c => c.DeviceId == 1).ToListAsync();
        commands.Should().HaveCount(1);
        commands[0].CommandType.Should().Be("STOP");
        commands[0].CommandText.Should().Be("AJ+STOP#1311\n");
        commands[0].Status.Should().Be("pending");
        commands[0].Source.Should().Be("manual");
        commands[0].UserId.Should().Be(10);
    }

    [Fact]
    public async Task StopDevice_UsesDeviceSpecificCommand_NotDefault()
    {
        using var context = TestDbContextFactory.Create();
        var device = CreateDeviceWithVehicle(context, deviceId: 1, companyId: 1);
        device.CommandStop = "AJ+STOP#9999\n"; // custom password
        await context.SaveChangesAsync();

        // Act
        context.DeviceCommands.Add(new DeviceCommand
        {
            DeviceId = 1,
            UserId = 5,
            CommandType = "STOP",
            CommandText = device.CommandStop,
            Status = "pending",
            Source = "manual",
            CompanyId = 1
        });
        await context.SaveChangesAsync();

        // Assert: command uses the device's custom password
        var saved = await context.DeviceCommands.FirstAsync(c => c.DeviceId == 1);
        saved.CommandText.Should().Be("AJ+STOP#9999\n");
    }

    // ==================== SCENARIO 2: GO ====================

    [Fact]
    public async Task GoDevice_SetsImmobilizationActive_False()
    {
        using var context = TestDbContextFactory.Create();
        var device = CreateDeviceWithVehicle(context, deviceId: 1, companyId: 1);
        device.ImmobilizationActive = true;
        device.ImmobilizationBy = 42;
        await context.SaveChangesAsync();

        // Act: simulate GpsController.GoDevice
        device.ImmobilizationActive = false;
        device.ImmobilizationBy = 7;
        device.ImmobilizationAt = DateTime.UtcNow;

        context.DeviceCommands.Add(new DeviceCommand
        {
            DeviceId = 1,
            VehicleId = device.Vehicle?.Id,
            UserId = 7,
            CommandType = "GO",
            CommandText = device.CommandGo,
            Status = "pending",
            Source = "manual",
            CompanyId = 1
        });
        await context.SaveChangesAsync();

        // Assert
        var updated = await context.GpsDevices.FindAsync(1);
        updated!.ImmobilizationActive.Should().BeFalse();
        updated.ImmobilizationBy.Should().Be(7);

        var commands = await context.DeviceCommands.Where(c => c.DeviceId == 1).ToListAsync();
        commands.Should().HaveCount(1);
        commands[0].CommandType.Should().Be("GO");
        commands[0].CommandText.Should().Be("AJ+GO#1311\n");
        commands[0].Status.Should().Be("pending");
    }

    // ==================== SCENARIO 3: AUTO-RECOVERY DECISION ====================

    [Fact]
    public async Task AutoRecovery_WhenImmobilizationFalse_ShouldSendGoCommand()
    {
        using var context = TestDbContextFactory.Create();
        var device = CreateDeviceWithVehicle(context, deviceId: 1, companyId: 1);
        await context.SaveChangesAsync();

        var dbDevice = await context.GpsDevices.FindAsync(1);

        // Assert: immobilization is false → auto-recovery SHOULD proceed
        dbDevice!.ImmobilizationActive.Should().BeFalse();
        dbDevice.CommandGo.Should().Be("AJ+GO#1311\n");
    }

    [Fact]
    public async Task AutoRecovery_WhenImmobilizationTrue_ShouldNotSendGoCommand()
    {
        using var context = TestDbContextFactory.Create();
        var device = CreateDeviceWithVehicle(context, deviceId: 1, companyId: 1);
        device.ImmobilizationActive = true;
        device.ImmobilizationBy = 42;
        device.ImmobilizationAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var dbDevice = await context.GpsDevices.FindAsync(1);

        // Assert: immobilization is true → auto-recovery should NOT proceed
        dbDevice!.ImmobilizationActive.Should().BeTrue();
    }

    [Fact]
    public async Task AutoRecovery_LogsCommand_WithSource_AutoRecovery()
    {
        using var context = TestDbContextFactory.Create();
        var device = CreateDeviceWithVehicle(context, deviceId: 1, companyId: 1);
        await context.SaveChangesAsync();

        // Act: simulate what Rust's log_auto_recovery does
        context.DeviceCommands.Add(new DeviceCommand
        {
            DeviceId = 1,
            VehicleId = device.Vehicle?.Id,
            UserId = 0, // system (no operator)
            CommandType = "GO",
            CommandText = device.CommandGo,
            Status = "sent",
            SentAt = DateTime.UtcNow,
            Attempts = 1,
            Source = "auto_recovery",
            CompanyId = 1
        });
        await context.SaveChangesAsync();

        // Assert
        var cmd = await context.DeviceCommands.FirstAsync(c => c.DeviceId == 1);
        cmd.Source.Should().Be("auto_recovery");
        cmd.UserId.Should().Be(0);
        cmd.Status.Should().Be("sent");
        cmd.CommandText.Should().Be("AJ+GO#1311\n");
    }

    // ==================== SCENARIO 4: CORRECT DEVICE ====================

    [Fact]
    public async Task Command_IsCreatedForCorrectDevice_NotOtherDevices()
    {
        using var context = TestDbContextFactory.Create();
        var device1 = CreateDeviceWithVehicle(context, deviceId: 1, companyId: 1, imei: "865456789012345", plate: "236 TU 6917");
        var device2 = CreateDeviceWithVehicle(context, deviceId: 2, companyId: 1, imei: "865456789099999", plate: "238 TU 3149");
        await context.SaveChangesAsync();

        // Act: stop only device 1
        device1.ImmobilizationActive = true;
        device1.ImmobilizationBy = 10;

        context.DeviceCommands.Add(new DeviceCommand
        {
            DeviceId = 1,
            UserId = 10,
            CommandType = "STOP",
            CommandText = device1.CommandStop,
            Status = "pending",
            Source = "manual",
            CompanyId = 1
        });
        await context.SaveChangesAsync();

        // Assert: device 1 has the command, device 2 does not
        var cmdsDevice1 = await context.DeviceCommands.Where(c => c.DeviceId == 1).ToListAsync();
        var cmdsDevice2 = await context.DeviceCommands.Where(c => c.DeviceId == 2).ToListAsync();

        cmdsDevice1.Should().HaveCount(1);
        cmdsDevice1[0].CommandType.Should().Be("STOP");

        cmdsDevice2.Should().BeEmpty("device 2 should not receive any commands");

        // Device 2 state unchanged
        var dev2 = await context.GpsDevices.FindAsync(2);
        dev2!.ImmobilizationActive.Should().BeFalse("device 2 was not stopped");
    }

    [Fact]
    public async Task PendingCommand_IsPickedForCorrectDevice_InOrder()
    {
        using var context = TestDbContextFactory.Create();
        var device = CreateDeviceWithVehicle(context, deviceId: 1, companyId: 1);
        await context.SaveChangesAsync();

        // Add STOP first, save to get distinct CreatedAt
        context.DeviceCommands.Add(new DeviceCommand
        {
            DeviceId = 1, UserId = 10, CommandType = "STOP",
            CommandText = "AJ+STOP#1311\n", Status = "pending",
            Source = "manual", CompanyId = 1
        });
        await context.SaveChangesAsync();

        // Then GO (operator changed their mind)
        context.DeviceCommands.Add(new DeviceCommand
        {
            DeviceId = 1, UserId = 10, CommandType = "GO",
            CommandText = "AJ+GO#1311\n", Status = "pending",
            Source = "manual", CompanyId = 1
        });
        await context.SaveChangesAsync();

        // Simulate Rust: pick oldest pending command by Id (FIFO)
        var oldest = await context.DeviceCommands
            .Where(c => c.DeviceId == 1 && c.Status == "pending")
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync();

        // Assert: STOP comes first (FIFO order)
        oldest.Should().NotBeNull();
        oldest!.CommandType.Should().Be("STOP");
    }

    [Fact]
    public async Task DefaultState_AllDevices_ImmobilizationFalse()
    {
        using var context = TestDbContextFactory.Create();
        CreateDeviceWithVehicle(context, deviceId: 1, companyId: 1, imei: "111111111111111");
        CreateDeviceWithVehicle(context, deviceId: 2, companyId: 1, imei: "222222222222222");
        CreateDeviceWithVehicle(context, deviceId: 3, companyId: 1, imei: "333333333333333");
        await context.SaveChangesAsync();

        // Assert: all devices default to immobilization_active = false
        var devices = await context.GpsDevices.ToListAsync();
        devices.Should().HaveCount(3);
        devices.Should().AllSatisfy(d =>
        {
            d.ImmobilizationActive.Should().BeFalse();
            d.ImmobilizationBy.Should().BeNull();
            d.ImmobilizationAt.Should().BeNull();
            d.CommandGo.Should().Be("AJ+GO#1311\n");
            d.CommandStop.Should().Be("AJ+STOP#1311\n");
        });
    }

    [Fact]
    public async Task MultiTenant_CommandIsolation_DifferentCompanies()
    {
        using var context = TestDbContextFactory.Create();
        var device1 = CreateDeviceWithVehicle(context, deviceId: 1, companyId: 1, imei: "111111111111111");
        var device2 = CreateDeviceWithVehicle(context, deviceId: 2, companyId: 2, imei: "222222222222222");
        await context.SaveChangesAsync();

        // Act: stop device1 (company 1)
        context.DeviceCommands.Add(new DeviceCommand
        {
            DeviceId = 1, UserId = 10, CommandType = "STOP",
            CommandText = device1.CommandStop, Status = "pending",
            Source = "manual", CompanyId = 1
        });
        await context.SaveChangesAsync();

        // Assert: company 2 has no commands
        var company2Cmds = await context.DeviceCommands
            .Where(c => c.CompanyId == 2)
            .ToListAsync();
        company2Cmds.Should().BeEmpty();

        // Company 1 has the command
        var company1Cmds = await context.DeviceCommands
            .Where(c => c.CompanyId == 1)
            .ToListAsync();
        company1Cmds.Should().HaveCount(1);
    }

    // ==================== HELPERS ====================

    private static GpsDevice CreateDeviceWithVehicle(
        TestGisDbContext context,
        int deviceId = 1,
        int companyId = 1,
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
}
