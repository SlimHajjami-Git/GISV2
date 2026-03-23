using FluentAssertions;
using GisAPI.Application.Features.Admin.Vehicles.Commands.CreateAdminVehicle;
using GisAPI.Application.Features.Admin.Vehicles.Commands.UpdateAdminVehicle;
using GisAPI.Application.Features.Admin.Vehicles.Services;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// Tests for SimNumber (phone number) persistence on vehicle create/update.
/// Regression tests for the bug where phone numbers were not saved to the GPS device.
/// </summary>
public class AdminVehicleSimNumberTests
{
    #region Create Vehicle - New GPS Device

    [Fact]
    public async Task CreateVehicle_WithNewGpsDevice_SavesSimNumber()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        var handler = new CreateAdminVehicleCommandHandler(context);

        var command = new CreateAdminVehicleCommand(
            Name: "Test Vehicle",
            Type: "camion",
            Brand: "Mercedes",
            Model: "Actros",
            Plate: "ABC-123",
            Year: 2024,
            Color: "White",
            Status: "available",
            HasGps: true,
            Mileage: 0,
            FuelType: "diesel",
            FuelTankCapacity: 60,
            CompanyId: 1,
            GpsDeviceId: null,
            GpsImei: "IMEI123456789",
            GpsMat: "MAT-001",
            GpsBrand: "NEMS",
            GpsModel: "S",
            GpsFirmwareVersion: "1.0",
            GpsFuelSensorMode: "raw_255",
            GpsSimNumber: "+216 50 123 456",
            GpsSimOperator: "ooredoo",
            GpsInstallationDate: new DateTime(2024, 1, 15)
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Vehicle.Should().NotBeNull();
        result.Vehicle!.GpsSimNumber.Should().Be("+216 50 123 456");
        result.Vehicle.GpsSimOperator.Should().Be("ooredoo");

        // Verify directly in DB
        var device = await context.GpsDevices.FirstAsync();
        device.SimNumber.Should().Be("+216 50 123 456");
        device.SimOperator.Should().Be("ooredoo");
    }

    [Fact]
    public async Task CreateVehicle_WithNewGpsDevice_SavesAllGpsFields()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        var handler = new CreateAdminVehicleCommandHandler(context);

        var command = new CreateAdminVehicleCommand(
            Name: "Full GPS Vehicle",
            Type: "utilitaire",
            Brand: null, Model: null, Plate: "XYZ-789",
            Year: 2024, Color: null, Status: "available",
            HasGps: true, Mileage: 100,
            FuelType: "diesel", FuelTankCapacity: 80,
            CompanyId: 1,
            GpsDeviceId: null,
            GpsImei: "FULLIMEI999",
            GpsMat: "MAT-FULL",
            GpsBrand: "NORON",
            GpsModel: "NR024",
            GpsFirmwareVersion: "2.1",
            GpsFuelSensorMode: "percent",
            GpsSimNumber: "98765432",
            GpsSimOperator: "tunisie_telecom",
            GpsInstallationDate: new DateTime(2024, 6, 1)
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var device = await context.GpsDevices.FirstAsync();
        device.DeviceUid.Should().Be("FULLIMEI999");
        device.Mat.Should().Be("MAT-FULL");
        device.Brand.Should().Be("NORON");
        device.Model.Should().Be("NR024");
        device.FirmwareVersion.Should().Be("2.1");
        device.FuelSensorMode.Should().Be("percent");
        device.SimNumber.Should().Be("98765432");
        device.SimOperator.Should().Be("tunisie_telecom");
        device.InstallationDate.Should().Be(new DateTime(2024, 6, 1));
    }

    [Fact]
    public async Task CreateVehicle_WithoutSimNumber_LeavesSimNumberNull()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        var handler = new CreateAdminVehicleCommandHandler(context);

        var command = new CreateAdminVehicleCommand(
            Name: "No Phone Vehicle",
            Type: "camion",
            Brand: null, Model: null, Plate: "NPN-001",
            Year: 2024, Color: null, Status: "available",
            HasGps: true, Mileage: 0,
            FuelType: "diesel", FuelTankCapacity: null,
            CompanyId: 1,
            GpsDeviceId: null,
            GpsImei: "NOPHONE001",
            GpsMat: null,
            GpsBrand: null, GpsModel: null,
            GpsFirmwareVersion: null, GpsFuelSensorMode: null,
            GpsSimNumber: null,
            GpsSimOperator: null,
            GpsInstallationDate: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var device = await context.GpsDevices.FirstAsync();
        device.SimNumber.Should().BeNull();
        device.SimOperator.Should().BeNull();
    }

    #endregion

    #region Create Vehicle - Existing GPS Device

    [Fact]
    public async Task CreateVehicle_WithExistingDevice_UpdatesSimNumber()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);

        var existingDevice = new GpsDevice
        {
            Id = 10,
            DeviceUid = "EXISTING001",
            CompanyId = 1,
            Status = "unassigned",
            SimNumber = null // No phone number initially
        };
        context.GpsDevices.Add(existingDevice);
        await context.SaveChangesAsync();

        var handler = new CreateAdminVehicleCommandHandler(context);

        var command = new CreateAdminVehicleCommand(
            Name: "Existing GPS Vehicle",
            Type: "camion",
            Brand: null, Model: null, Plate: "EGV-001",
            Year: 2024, Color: null, Status: "available",
            HasGps: true, Mileage: 0,
            FuelType: "diesel", FuelTankCapacity: null,
            CompanyId: 1,
            GpsDeviceId: 10,
            GpsImei: null,
            GpsMat: null,
            GpsBrand: null, GpsModel: null,
            GpsFirmwareVersion: null, GpsFuelSensorMode: null,
            GpsSimNumber: "+216 99 888 777",
            GpsSimOperator: "orange_tunisie",
            GpsInstallationDate: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var device = await context.GpsDevices.FindAsync(10);
        device!.SimNumber.Should().Be("+216 99 888 777");
        device.SimOperator.Should().Be("orange_tunisie");
        device.Status.Should().Be("assigned");
    }

    [Fact]
    public async Task CreateVehicle_WithExistingDeviceByImei_UpdatesSimNumber()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);

        var existingDevice = new GpsDevice
        {
            Id = 20,
            DeviceUid = "IMEI_LOOKUP_001",
            CompanyId = 1,
            Status = "unassigned",
            SimNumber = "old_number"
        };
        context.GpsDevices.Add(existingDevice);
        await context.SaveChangesAsync();

        var handler = new CreateAdminVehicleCommandHandler(context);

        var command = new CreateAdminVehicleCommand(
            Name: "IMEI Lookup Vehicle",
            Type: "camion",
            Brand: null, Model: null, Plate: "ILV-001",
            Year: 2024, Color: null, Status: "available",
            HasGps: true, Mileage: 0,
            FuelType: "diesel", FuelTankCapacity: null,
            CompanyId: 1,
            GpsDeviceId: null,
            GpsImei: "IMEI_LOOKUP_001",
            GpsMat: null,
            GpsBrand: null, GpsModel: null,
            GpsFirmwareVersion: null, GpsFuelSensorMode: null,
            GpsSimNumber: "new_phone_number",
            GpsSimOperator: "ooredoo",
            GpsInstallationDate: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var device = await context.GpsDevices.FindAsync(20);
        device!.SimNumber.Should().Be("new_phone_number");
        device.SimOperator.Should().Be("ooredoo");
    }

    #endregion

    #region Update Vehicle - GPS Device

    [Fact]
    public async Task UpdateVehicle_WithSimNumber_UpdatesDeviceSimNumber()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);

        var device = new GpsDevice
        {
            Id = 30,
            DeviceUid = "UPDATE_DEV_001",
            CompanyId = 1,
            Status = "assigned",
            SimNumber = null
        };
        context.GpsDevices.Add(device);

        var vehicle = new Vehicle
        {
            Id = 100,
            Name = "Update Test",
            Type = "camion",
            Status = "available",
            HasGps = true,
            GpsDeviceId = 30,
            CompanyId = 1,
            Mileage = 0,
            FuelType = "diesel"
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var handler = new UpdateAdminVehicleCommandHandler(context);

        var command = new UpdateAdminVehicleCommand(
            Id: 100,
            Name: null, Type: null, Brand: null, Model: null,
            Plate: null, Year: null, Color: null, Status: null,
            HasGps: true, Mileage: null,
            FuelType: null, FuelTankCapacity: null, CompanyId: null,
            GpsDeviceId: null,
            GpsImei: null,
            GpsMat: null,
            GpsBrand: null, GpsModel: null,
            GpsFirmwareVersion: null,
            GpsFuelSensorMode: "raw_255",
            GpsSimNumber: "+216 55 111 222",
            GpsSimOperator: "tunisie_telecom",
            GpsInstallationDate: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Vehicle.Should().NotBeNull();
        result.Vehicle!.GpsSimNumber.Should().Be("+216 55 111 222");
        result.Vehicle.GpsSimOperator.Should().Be("tunisie_telecom");

        var updatedDevice = await context.GpsDevices.FindAsync(30);
        updatedDevice!.SimNumber.Should().Be("+216 55 111 222");
        updatedDevice.SimOperator.Should().Be("tunisie_telecom");
    }

    [Fact]
    public async Task UpdateVehicle_WithNewGpsDevice_SavesSimNumber()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);

        var vehicle = new Vehicle
        {
            Id = 200,
            Name = "No GPS Vehicle",
            Type = "camion",
            Status = "available",
            HasGps = false,
            GpsDeviceId = null,
            CompanyId = 1,
            Mileage = 0,
            FuelType = "diesel"
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var handler = new UpdateAdminVehicleCommandHandler(context);

        var command = new UpdateAdminVehicleCommand(
            Id: 200,
            Name: null, Type: null, Brand: null, Model: null,
            Plate: null, Year: null, Color: null, Status: null,
            HasGps: true, Mileage: null,
            FuelType: null, FuelTankCapacity: null, CompanyId: null,
            GpsDeviceId: null,
            GpsImei: "NEW_IMEI_FOR_UPDATE",
            GpsMat: "MAT-UPD",
            GpsBrand: "NEMS", GpsModel: "L",
            GpsFirmwareVersion: "3.0",
            GpsFuelSensorMode: "liters",
            GpsSimNumber: "0600112233",
            GpsSimOperator: "ooredoo",
            GpsInstallationDate: new DateTime(2024, 3, 15)
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var device = await context.GpsDevices.FirstAsync();
        device.SimNumber.Should().Be("0600112233");
        device.SimOperator.Should().Be("ooredoo");
        device.Brand.Should().Be("NEMS");
        device.Model.Should().Be("L");
    }

    [Fact]
    public async Task UpdateVehicle_RemoveGps_DoesNotCrash()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);

        var device = new GpsDevice
        {
            Id = 40,
            DeviceUid = "REMOVE_DEV",
            CompanyId = 1,
            Status = "assigned",
            SimNumber = "+216 00 000 000"
        };
        context.GpsDevices.Add(device);

        var vehicle = new Vehicle
        {
            Id = 300,
            Name = "Remove GPS Test",
            Type = "camion",
            Status = "available",
            HasGps = true,
            GpsDeviceId = 40,
            CompanyId = 1,
            Mileage = 0,
            FuelType = "diesel"
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        var handler = new UpdateAdminVehicleCommandHandler(context);

        var command = new UpdateAdminVehicleCommand(
            Id: 300,
            Name: null, Type: null, Brand: null, Model: null,
            Plate: null, Year: null, Color: null, Status: null,
            HasGps: false, Mileage: null,
            FuelType: null, FuelTankCapacity: null, CompanyId: null,
            GpsDeviceId: null, GpsImei: null, GpsMat: null,
            GpsBrand: null, GpsModel: null,
            GpsFirmwareVersion: null, GpsFuelSensorMode: null,
            GpsSimNumber: null, GpsSimOperator: null,
            GpsInstallationDate: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var updatedVehicle = await context.Vehicles.FindAsync(300);
        updatedVehicle!.HasGps.Should().BeFalse();
        updatedVehicle.GpsDeviceId.Should().BeNull();

        var freedDevice = await context.GpsDevices.FindAsync(40);
        freedDevice!.Status.Should().Be("unassigned");
        freedDevice.SimNumber.Should().Be("+216 00 000 000"); // Preserved
    }

    #endregion

    #region GpsDeviceResolver Tests

    [Fact]
    public async Task GpsDeviceResolver_CreateNewDevice_SetsBasicFields()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();

        // Act
        var (device, error) = await GpsDeviceResolver.ResolveAsync(
            context, companyId: 1, gpsDeviceId: null, gpsImei: "RESOLVER_IMEI", gpsMat: "RES-MAT");

        // Assert
        error.Should().BeNull();
        device.Should().NotBeNull();
        device!.DeviceUid.Should().Be("RESOLVER_IMEI");
        device.Mat.Should().Be("RES-MAT");
        device.CompanyId.Should().Be(1);
    }

    [Fact]
    public async Task GpsDeviceResolver_FindExistingByImei_ReturnsDevice()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        var existing = new GpsDevice
        {
            Id = 50,
            DeviceUid = "FIND_ME_IMEI",
            CompanyId = 1,
            Status = "unassigned",
            SimNumber = "existing_phone"
        };
        context.GpsDevices.Add(existing);
        await context.SaveChangesAsync();

        // Act
        var (device, error) = await GpsDeviceResolver.ResolveAsync(
            context, companyId: 1, gpsDeviceId: null, gpsImei: "FIND_ME_IMEI", gpsMat: null);

        // Assert
        error.Should().BeNull();
        device.Should().NotBeNull();
        device!.Id.Should().Be(50);
        device.SimNumber.Should().Be("existing_phone"); // Preserved from DB
    }

    [Fact]
    public async Task GpsDeviceResolver_NoImeiNoId_ReturnsNull()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();

        // Act
        var (device, error) = await GpsDeviceResolver.ResolveAsync(
            context, companyId: 1, gpsDeviceId: null, gpsImei: null, gpsMat: null);

        // Assert
        device.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public async Task GpsDeviceResolver_MapToDto_IncludesSimFields()
    {
        // Arrange
        var vehicle = new Vehicle
        {
            Id = 1,
            Name = "DTO Test",
            Type = "camion",
            Status = "available",
            HasGps = true,
            CompanyId = 1,
            Mileage = 0,
            GpsDevice = new GpsDevice
            {
                Id = 60,
                DeviceUid = "DTO_IMEI",
                SimNumber = "+216 11 222 333",
                SimOperator = "orange_tunisie",
                Brand = "NEMS",
                Model = "S",
                Mat = "DTO-MAT",
                FuelSensorMode = "raw_255",
                CompanyId = 1,
                Status = "assigned"
            },
            Societe = new Societe
            {
                Id = 1,
                Name = "Test Co",
                Type = "transport",
                Email = "co@test.com"
            }
        };

        // Act
        var dto = GpsDeviceResolver.MapToDto(vehicle);

        // Assert
        dto.GpsSimNumber.Should().Be("+216 11 222 333");
        dto.GpsSimOperator.Should().Be("orange_tunisie");
        dto.GpsImei.Should().Be("DTO_IMEI");
        dto.GpsMat.Should().Be("DTO-MAT");
        dto.GpsBrand.Should().Be("NEMS");
        dto.GpsModel.Should().Be("S");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task CreateVehicle_WithEmptySimNumber_DoesNotSetSimNumber()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        var handler = new CreateAdminVehicleCommandHandler(context);

        var command = new CreateAdminVehicleCommand(
            Name: "Empty SIM",
            Type: "camion",
            Brand: null, Model: null, Plate: "ESM-001",
            Year: 2024, Color: null, Status: "available",
            HasGps: true, Mileage: 0,
            FuelType: "diesel", FuelTankCapacity: null,
            CompanyId: 1,
            GpsDeviceId: null,
            GpsImei: "EMPTYSIM001",
            GpsMat: null,
            GpsBrand: null, GpsModel: null,
            GpsFirmwareVersion: null, GpsFuelSensorMode: null,
            GpsSimNumber: "",  // Empty string
            GpsSimOperator: "  ",  // Whitespace
            GpsInstallationDate: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var device = await context.GpsDevices.FirstAsync();
        device.SimNumber.Should().BeNull(); // Empty string should not be set
        device.SimOperator.Should().BeNull(); // Whitespace should not be set
    }

    [Fact]
    public async Task CreateVehicle_InvalidCompany_ReturnsFalse()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        // Don't seed company
        var handler = new CreateAdminVehicleCommandHandler(context);

        var command = new CreateAdminVehicleCommand(
            Name: "Bad Company",
            Type: "camion",
            Brand: null, Model: null, Plate: null,
            Year: null, Color: null, Status: null,
            HasGps: false, Mileage: null,
            FuelType: null, FuelTankCapacity: null,
            CompanyId: 999,
            GpsDeviceId: null, GpsImei: null, GpsMat: null,
            GpsBrand: null, GpsModel: null,
            GpsFirmwareVersion: null, GpsFuelSensorMode: null,
            GpsSimNumber: null, GpsSimOperator: null,
            GpsInstallationDate: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Société");
    }

    [Fact]
    public async Task CreateVehicle_WithoutGps_DoesNotCreateDevice()
    {
        // Arrange
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        var handler = new CreateAdminVehicleCommandHandler(context);

        var command = new CreateAdminVehicleCommand(
            Name: "No GPS",
            Type: "citadine",
            Brand: null, Model: null, Plate: "NGP-001",
            Year: 2024, Color: null, Status: "available",
            HasGps: false, Mileage: 0,
            FuelType: "essence", FuelTankCapacity: null,
            CompanyId: 1,
            GpsDeviceId: null, GpsImei: null, GpsMat: null,
            GpsBrand: null, GpsModel: null,
            GpsFirmwareVersion: null, GpsFuelSensorMode: null,
            GpsSimNumber: "+216 ignored", // Should be ignored since HasGps=false
            GpsSimOperator: "ooredoo",
            GpsInstallationDate: null
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var devices = await context.GpsDevices.CountAsync();
        devices.Should().Be(0); // No device created
    }

    #endregion

    #region Helpers

    private static async Task SeedCompany(TestGisDbContext context)
    {
        var sub = TestDataBuilder.CreateSubscriptionType();
        context.SubscriptionTypes.Add(sub);
        var company = TestDataBuilder.CreateSociete(id: 1, subscriptionTypeId: 1);
        context.Societes.Add(company);
        await context.SaveChangesAsync();
    }

    #endregion
}
