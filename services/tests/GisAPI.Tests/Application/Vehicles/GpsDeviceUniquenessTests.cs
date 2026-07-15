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
/// Anti-doublons IMEI / MAT / SIM à la création et modification de véhicule.
/// Régression pour l'audit du 15/07/2026 : ~40 doublons en prod (ex: deux boîtiers
/// partageant la SIM 92002732 → confusion HTZ 159 / 262 TU 8165).
/// </summary>
public class GpsDeviceUniquenessTests
{
    // ── Normalisation ──

    [Theory]
    [InlineData("92 002 732", "92002732")]
    [InlineData("  nr08g0838 ", "NR08G0838")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_StripsSpacesAndUppercases(string? input, string expected)
    {
        GpsDeviceUniquenessGuard.Normalize(input).Should().Be(expected);
    }

    // ── Guard direct ──

    [Fact]
    public async Task FindConflict_DetectsSimDuplicate_EvenWithSpacingDifferences()
    {
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        context.GpsDevices.Add(NewDevice(1, "IMEI-A", "MAT-A", "92 002 732"));
        await context.SaveChangesAsync();

        var conflict = await GpsDeviceUniquenessGuard.FindConflictAsync(
            context, excludeDeviceId: 0, imei: "IMEI-B", mat: "MAT-B", sim: "92002732");

        conflict.Should().NotBeNull();
        conflict.Should().Contain("numéro SIM");
    }

    [Fact]
    public async Task FindConflict_IgnoresSelf()
    {
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        context.GpsDevices.Add(NewDevice(1, "IMEI-A", "MAT-A", "92002732"));
        await context.SaveChangesAsync();

        var conflict = await GpsDeviceUniquenessGuard.FindConflictAsync(
            context, excludeDeviceId: 1, imei: "IMEI-A", mat: "MAT-A", sim: "92002732");

        conflict.Should().BeNull();
    }

    [Fact]
    public async Task FindConflict_NamesTheConflictingVehicle()
    {
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        var device = NewDevice(1, "IMEI-A", "MAT-A", "92002732");
        context.GpsDevices.Add(device);
        context.Vehicles.Add(new Vehicle { Id = 10, Name = "262 TU 8165", Plate = "262 TU 8165", Type = "voiture", CompanyId = 1, GpsDeviceId = 1 });
        await context.SaveChangesAsync();

        var conflict = await GpsDeviceUniquenessGuard.FindConflictAsync(
            context, 0, null, "mat-a", null);

        conflict.Should().NotBeNull();
        conflict.Should().Contain("MAT").And.Contain("262 TU 8165");
    }

    // ── Création admin ──

    [Fact]
    public async Task CreateVehicle_WithDuplicateSim_IsRejected()
    {
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        context.GpsDevices.Add(NewDevice(1, "EXISTING-IMEI", "EXISTING-MAT", "92002732"));
        await context.SaveChangesAsync();
        var handler = new CreateAdminVehicleCommandHandler(context);

        var result = await handler.Handle(Command(imei: "NEW-IMEI", mat: "NEW-MAT", sim: "92 002 732"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("SIM");
        (await context.Vehicles.CountAsync()).Should().Be(0, "le véhicule ne doit pas être créé");
    }

    [Fact]
    public async Task CreateVehicle_WithDuplicateMat_IsRejected()
    {
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        context.GpsDevices.Add(NewDevice(1, "EXISTING-IMEI", "NR08G0838", "11111111"));
        await context.SaveChangesAsync();
        var handler = new CreateAdminVehicleCommandHandler(context);

        var result = await handler.Handle(Command(imei: "NEW-IMEI", mat: "nr08g0838", sim: "22222222"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("MAT");
    }

    [Fact]
    public async Task CreateVehicle_ReusingExistingImei_IsAllowed_ReassignsDevice()
    {
        // Réutiliser un boîtier EXISTANT par son IMEI n'est pas un doublon :
        // le resolver rattache la même ligne gps_devices (comportement historique).
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        context.GpsDevices.Add(NewDevice(1, "SAME-IMEI", "MAT-X", "33333333"));
        await context.SaveChangesAsync();
        var handler = new CreateAdminVehicleCommandHandler(context);

        var result = await handler.Handle(Command(imei: "SAME-IMEI", mat: null, sim: null), CancellationToken.None);

        result.Success.Should().BeTrue();
        (await context.GpsDevices.CountAsync()).Should().Be(1, "aucun second boîtier ne doit être créé");
    }

    [Fact]
    public async Task CreateVehicle_UniqueIdentifiers_Succeeds()
    {
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        context.GpsDevices.Add(NewDevice(1, "OTHER-IMEI", "OTHER-MAT", "44444444"));
        await context.SaveChangesAsync();
        var handler = new CreateAdminVehicleCommandHandler(context);

        var result = await handler.Handle(Command(imei: "FRESH-IMEI", mat: "FRESH-MAT", sim: "55555555"), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    // ── Modification admin ──

    [Fact]
    public async Task UpdateVehicle_SettingSimOfAnotherDevice_IsRejected()
    {
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        context.GpsDevices.Add(NewDevice(1, "IMEI-1", "MAT-1", "92002732"));
        context.GpsDevices.Add(NewDevice(2, "IMEI-2", "MAT-2", "66666666"));
        context.Vehicles.Add(new Vehicle { Id = 20, Name = "HTZ 159", Plate = "HTZ 159", Type = "voiture", CompanyId = 1, GpsDeviceId = 2, HasGps = true });
        await context.SaveChangesAsync();
        var handler = new UpdateAdminVehicleCommandHandler(context);

        var result = await handler.Handle(new UpdateAdminVehicleCommand(
            20, null, null, null, null, null, null, null, null, true, null, null, null, null,
            GpsDeviceId: null, GpsImei: null, GpsMat: null,
            GpsBrand: null, GpsModel: null, GpsFirmwareVersion: null, GpsFuelSensorMode: null,
            GpsSimNumber: "92002732", GpsSimOperator: null, GpsInstallationDate: null
        ), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("SIM");
        (await context.GpsDevices.AsNoTracking().FirstAsync(d => d.Id == 2)).SimNumber
            .Should().Be("66666666", "la SIM ne doit pas être écrasée");
    }

    [Fact]
    public async Task UpdateVehicle_KeepingOwnSim_Succeeds()
    {
        using var context = TestDbContextFactory.Create();
        await SeedCompany(context);
        context.GpsDevices.Add(NewDevice(2, "IMEI-2", "MAT-2", "66666666"));
        context.Vehicles.Add(new Vehicle { Id = 20, Name = "HTZ 159", Plate = "HTZ 159", Type = "voiture", CompanyId = 1, GpsDeviceId = 2, HasGps = true });
        await context.SaveChangesAsync();
        var handler = new UpdateAdminVehicleCommandHandler(context);

        var result = await handler.Handle(new UpdateAdminVehicleCommand(
            20, "HTZ 159 bis", null, null, null, null, null, null, null, true, null, null, null, null,
            null, null, null, null, null, null, null,
            GpsSimNumber: "66666666", GpsSimOperator: null, GpsInstallationDate: null
        ), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    // ── Helpers ──

    private static GpsDevice NewDevice(int id, string imei, string? mat, string? sim) => new()
    {
        Id = id,
        DeviceUid = imei,
        Mat = mat,
        SimNumber = sim,
        CompanyId = 1,
        Status = "unassigned",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static CreateAdminVehicleCommand Command(string? imei, string? mat, string? sim) => new(
        Name: "Nouveau véhicule",
        Type: "voiture",
        Brand: null, Model: null, Plate: "999 TU 9999",
        Year: 2026, Color: null, Status: "available",
        HasGps: true, Mileage: 0,
        FuelType: "diesel", FuelTankCapacity: null,
        CompanyId: 1,
        GpsDeviceId: null,
        GpsImei: imei,
        GpsMat: mat,
        GpsBrand: null, GpsModel: null, GpsFirmwareVersion: null, GpsFuelSensorMode: null,
        GpsSimNumber: sim, GpsSimOperator: null, GpsInstallationDate: null
    );

    private static async Task SeedCompany(TestGisDbContext context)
    {
        var sub = TestDataBuilder.CreateSubscriptionType();
        context.SubscriptionTypes.Add(sub);
        var company = TestDataBuilder.CreateSociete(id: 1, subscriptionTypeId: 1);
        context.Societes.Add(company);
        await context.SaveChangesAsync();
    }
}
