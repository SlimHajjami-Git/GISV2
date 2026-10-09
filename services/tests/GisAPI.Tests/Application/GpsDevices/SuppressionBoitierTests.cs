using FluentAssertions;
using GisAPI.Application.Features.Admin.GpsDevices;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.GpsDevices;

/// <summary>
/// Demande de Slim du 09/10/2026 : « dans l'interface admin il faut avoir la possibilité de
/// supprimer un boîtier ; si le boîtier est supprimé, toutes ses informations et relations
/// dans la base doivent être supprimées aussi (véhicule, position, carburant…) ».
///
/// Ce que ces tests épinglent : l'inventaire dit la vérité table par table, la confirmation
/// par l'IMEI est obligatoire, un boîtier qui porte l'historique d'un AUTRE véhicule vivant
/// est refusé, les grosses tables partent par lots, le véhicule rattaché part avec ses
/// pleins saisis à la main, ses notifications et ses tournées, les dossiers de sinistre sont
/// conservés détachés, et l'IMEI finit en liste de blocage (débloquable).
/// </summary>
public class SuppressionBoitierTests
{
    private const int Belive = 1;
    private const int Hertz = 4;
    private const int Boitier = 190939;
    private const string Imei = "860141076687632";
    private const int Vehicule = 524;

    // ── Inventaire ──

    [Fact]
    public async Task L_inventaire_compte_ce_que_porte_le_boitier_et_son_vehicule()
    {
        using var context = TestDbContextFactory.Create();
        await SemerBoitierAvecVehiculeAsync(context, positions: 7);

        var impact = await GpsDeviceDeletionInventory.BuildAsync(context, Boitier, CancellationToken.None);

        impact.Should().NotBeNull();
        impact!.Imei.Should().Be(Imei);
        impact.CompanyName.Should().Be("HERTZ");
        impact.ConfirmationToken.Should().Be(Imei);
        impact.Refusal.Should().BeNull();
        impact.Vehicle.Should().NotBeNull();
        impact.Vehicle!.Plate.Should().Be("HTZ 345");
        impact.Vehicle.FuelEntries.Should().Be(2);
        impact.Vehicle.Tours.Should().Be(1);
        impact.Vehicle.Notifications.Should().Be(1);
        impact.Vehicle.AccidentFilesKept.Should().Be(1);
        Lignes(impact, "gps_positions").Should().Be(7);
        Lignes(impact, "gps_alerts").Should().Be(3);
        Lignes(impact, "vehicle_stops").Should().Be(2);
        Lignes(impact, "fuel_records").Should().Be(2);
        Lignes(impact, "device_commands").Should().Be(1);
        Lignes(impact, "battery_start_readings").Should().Be(1);
        Lignes(impact, "notifications").Should().Be(1, "notification du boîtier (immobilisation)");
        impact.AccidentFilesKept.Should().Be(2, "un dossier sur le véhicule, un sur le boîtier");
        impact.Warnings.Should().Contain(w => w.Contains("HTZ 345")).And.Contain(w => w.Contains("sinistre"));
        impact.ActiveRecently.Should().BeTrue();
    }

    [Fact]
    public void Le_jeton_de_confirmation_est_SUPPRIMER_quand_l_identifiant_n_est_pas_lisible()
    {
        GpsDeviceDeletionInventory.ConfirmationTokenFor("860141076690214at+cclk=\"26/10/09").Should().Be("SUPPRIMER");
        GpsDeviceDeletionInventory.ConfirmationTokenFor("NR09G12692").Should().Be("NR09G12692");
        GpsDeviceDeletionInventory.ConfirmationMatches(" 8601 4107 6687 632 ", Imei).Should().BeTrue();
        GpsDeviceDeletionInventory.ConfirmationMatches("supprimer", "SUPPRIMER").Should().BeTrue();
        GpsDeviceDeletionInventory.ConfirmationMatches("", Imei).Should().BeFalse();
        GpsDeviceDeletionInventory.ConfirmationMatches("860141076687633", Imei).Should().BeFalse();
    }

    [Fact]
    public async Task L_inventaire_refuse_un_boitier_qui_porte_l_historique_d_un_autre_vehicule_vivant()
    {
        // Boîtier remplacé : ses arrêts et relevés carburant appartiennent au véhicule 340,
        // aujourd'hui équipé d'un autre boîtier. Le supprimer amputerait ce véhicule.
        using var context = TestDbContextFactory.Create();
        await SemerSocietesAsync(context);
        context.GpsDevices.Add(Fiche(202106, "860141076672402", "NR08G0900", Belive, "unassigned", DateTime.UtcNow.AddMonths(-2)));
        context.GpsDevices.Add(Fiche(563078, "860141076672999", "NR08G0901", Hertz, "assigned", DateTime.UtcNow));
        context.Vehicles.Add(Vehicule_(340, "242 TU 7511", 563078, Hertz));
        context.VehicleStops.Add(new VehicleStop { Id = 1, VehicleId = 340, DeviceId = 202106, StartTime = DateTime.UtcNow.AddMonths(-3) });
        await context.SaveChangesAsync();

        var impact = await GpsDeviceDeletionInventory.BuildAsync(context, 202106, CancellationToken.None);

        impact!.Refusal.Should().NotBeNull().And.Contain("242 TU 7511").And.Contain("#563078");

        var result = await Handler(context).Handle(new DeleteGpsDeviceCommand(202106, "860141076672402"), CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("242 TU 7511");
        (await context.GpsDevices.CountAsync()).Should().Be(2);
        (await context.GpsDeviceBlocklist.CountAsync()).Should().Be(0, "un refus ne bloque rien");
    }

    // ── Suppression ──

    [Fact]
    public async Task Une_confirmation_fausse_ne_touche_a_rien()
    {
        using var context = TestDbContextFactory.Create();
        await SemerBoitierAvecVehiculeAsync(context, positions: 7);

        var result = await Handler(context).Handle(new DeleteGpsDeviceCommand(Boitier, "860141076687633"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Confirmation incorrecte").And.Contain(Imei);
        (await context.GpsDevices.CountAsync()).Should().Be(1);
        (await context.GpsPositions.CountAsync()).Should().Be(7);
        (await context.Vehicles.CountAsync()).Should().Be(1);
        (await context.GpsDeviceBlocklist.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Un_boitier_inconnu_rend_introuvable()
    {
        using var context = TestDbContextFactory.Create();
        await SemerSocietesAsync(context);

        var result = await Handler(context).Handle(new DeleteGpsDeviceCommand(424242, "x"), CancellationToken.None);

        result.NotFound.Should().BeTrue();
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task La_suppression_emporte_tout_ce_que_porte_le_boitier_par_lots_et_le_bloque()
    {
        using var context = TestDbContextFactory.Create();
        await SemerBoitierSansVehiculeAsync(context, positions: 7);
        var handler = Handler(context);
        handler.BatchSize = 3; // 7 positions → 3 + 3 + 1 : la boucle est prouvée

        var result = await handler.Handle(new DeleteGpsDeviceCommand(Boitier, " 8601 4107 6687 632 "), CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        result.VehicleDeleted.Should().BeFalse();
        result.TotalRows.Should().Be(7 + 3 + 2 + 2 + 1 + 1 + 1 + 1 + 1 + 1 + 1);
        (await context.GpsDevices.CountAsync(d => d.Id == Boitier)).Should().Be(0);
        (await context.GpsPositions.CountAsync()).Should().Be(0);
        (await context.GpsAlerts.CountAsync()).Should().Be(0);
        (await context.VehicleStops.CountAsync()).Should().Be(0);
        (await context.FuelRecords.CountAsync()).Should().Be(0);
        (await context.DeviceCommands.CountAsync()).Should().Be(0);
        (await context.DeviceEvents.CountAsync()).Should().Be(0);
        (await context.TowEvents.CountAsync()).Should().Be(0);
        (await context.BatteryStartReadings.CountAsync()).Should().Be(0);
        (await context.GeofenceEvents.CountAsync()).Should().Be(0);
        (await context.PoiVisits.CountAsync()).Should().Be(0);
        (await context.Notifications.CountAsync(n => n.ReferenceType == "device")).Should().Be(0);

        // Le dossier de sinistre survit, détaché du boîtier ; l'IMEI reste en texte.
        var sinistre = await context.AccidentEvents.AsNoTracking().FirstAsync(a => a.Id == 7);
        sinistre.GpsDeviceId.Should().BeNull();
        sinistre.DeviceUid.Should().Be(Imei);

        var blocage = await context.GpsDeviceBlocklist.AsNoTracking().FirstAsync(b => b.DeviceUid == Imei);
        blocage.DeviceIdBefore.Should().Be(Boitier);
        blocage.CompanyIdBefore.Should().Be(Hertz);
        blocage.Mat.Should().Be("NR08G1116");

        var audit = await context.AuditLogs.AsNoTracking().FirstAsync(a => a.Action == "gps_device_deleted");
        audit.EntityId.Should().Be(Boitier);
        audit.EntityName.Should().Be(Imei);
        audit.Description.Should().Contain(Imei).And.Contain("aucun véhicule");
    }

    [Fact]
    public async Task La_suppression_emporte_le_vehicule_ses_pleins_ses_notifications_et_ses_tournees()
    {
        using var context = TestDbContextFactory.Create();
        await SemerBoitierAvecVehiculeAsync(context, positions: 4);

        var result = await Handler(context).Handle(new DeleteGpsDeviceCommand(Boitier, Imei), CancellationToken.None);

        result.Success.Should().BeTrue(result.Message);
        result.VehicleDeleted.Should().BeTrue();
        (await context.GpsDevices.CountAsync(d => d.Id == Boitier)).Should().Be(0);
        (await context.Vehicles.CountAsync(v => v.Id == Vehicule)).Should().Be(0);
        (await context.FuelEntries.CountAsync()).Should().Be(0, "les pleins saisis à la main partent avec le véhicule (demande du 09/10)");
        (await context.Notifications.CountAsync()).Should().Be(0, "notifications du véhicule et du boîtier");
        (await context.Tours.CountAsync()).Should().Be(0);
        (await context.TourWaypoints.CountAsync()).Should().Be(0);
        (await context.GpsPositions.CountAsync()).Should().Be(0);

        // Le dossier de sinistre du véhicule reste, détaché, avec son libellé figé.
        var sinistre = await context.AccidentEvents.AsNoTracking().FirstAsync(a => a.Id == 8);
        sinistre.VehicleId.Should().BeNull();
        sinistre.VehicleLabel.Should().Be("HTZ 345");

        var audit = await context.AuditLogs.AsNoTracking().FirstAsync(a => a.Action == "gps_device_deleted");
        audit.Description.Should().Contain("HTZ 345");
    }

    // ── Liste de blocage ──

    [Fact]
    public async Task Debloquer_retire_l_IMEI_et_le_journalise()
    {
        using var context = TestDbContextFactory.Create();
        await SemerSocietesAsync(context);
        context.GpsDeviceBlocklist.Add(new GpsDeviceBlocklistEntry { DeviceUid = Imei, DeviceIdBefore = Boitier, CompanyIdBefore = Hertz, Mat = "NR08G1116" });
        await context.SaveChangesAsync();

        var liste = await new GetGpsDeviceBlocklistQueryHandler(context).Handle(new GetGpsDeviceBlocklistQuery(), CancellationToken.None);
        liste.Should().ContainSingle(b => b.DeviceUid == Imei);

        var ok = await new UnblockGpsDeviceCommandHandler(context, Tenant()).Handle(new UnblockGpsDeviceCommand($" {Imei} "), CancellationToken.None);
        ok.Should().BeTrue();
        (await context.GpsDeviceBlocklist.CountAsync()).Should().Be(0);
        (await context.AuditLogs.CountAsync(a => a.Action == "gps_device_unblocked")).Should().Be(1);

        (await new UnblockGpsDeviceCommandHandler(context, Tenant()).Handle(new UnblockGpsDeviceCommand("inconnu"), CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Reinscrire_un_IMEI_bloque_depuis_l_Admin_leve_son_blocage()
    {
        using var context = TestDbContextFactory.Create();
        await SemerSocietesAsync(context);
        context.GpsDeviceBlocklist.Add(new GpsDeviceBlocklistEntry { DeviceUid = Imei, DeviceIdBefore = Boitier, CompanyIdBefore = Hertz });
        await context.SaveChangesAsync();

        var (device, error) = await GisAPI.Application.Features.Admin.Vehicles.Services.GpsDeviceResolver
            .ResolveAsync(context, Hertz, null, Imei, "NR08G1116");
        await context.SaveChangesAsync();

        error.Should().BeNull();
        device.Should().NotBeNull();
        device!.DeviceUid.Should().Be(Imei);
        (await context.GpsDeviceBlocklist.CountAsync()).Should().Be(0, "le boîtier est réinstallé");
    }

    [Fact]
    public async Task L_inventaire_refuse_aussi_quand_un_autre_vehicule_vivant_n_a_que_des_remorquages_ou_des_zones()
    {
        using var context = TestDbContextFactory.Create();
        await SemerSocietesAsync(context);
        context.GpsDevices.Add(Fiche(202106, "860141076672402", "NR08G0900", Belive, "unassigned", DateTime.UtcNow.AddMonths(-2)));
        context.GpsDevices.Add(Fiche(563078, "860141076672999", "NR08G0901", Hertz, "assigned", DateTime.UtcNow));
        context.Vehicles.Add(Vehicule_(340, "242 TU 7511", 563078, Hertz));
        context.GeofenceEvents.Add(new GeofenceEvent { Id = 9, DeviceId = 202106, VehicleId = 340 });
        await context.SaveChangesAsync();

        var impact = await GpsDeviceDeletionInventory.BuildAsync(context, 202106, CancellationToken.None);

        impact!.Refusal.Should().NotBeNull().And.Contain("242 TU 7511");
    }

    // ── Liste admin des boîtiers ──

    [Fact]
    public async Task La_liste_admin_montre_les_fiches_sans_vehicule_et_filtre_par_societe_et_texte()
    {
        using var context = TestDbContextFactory.Create();
        await SemerSocietesAsync(context);
        context.GpsDevices.Add(Fiche(1, "860141076600001", "NR08G0001", Hertz, "assigned", DateTime.UtcNow));
        context.GpsDevices.Add(Fiche(2, "860141076600002", "NR08G0002", Hertz, "unassigned", null));
        context.GpsDevices.Add(Fiche(3, "NR09G12692", null, Belive, "unassigned", null));
        context.Vehicles.Add(Vehicule_(10, "HTZ 100", 1, Hertz));
        await context.SaveChangesAsync();
        var handler = new GetAdminGpsDevicesQueryHandler(context);

        var tout = await handler.Handle(new GetAdminGpsDevicesQuery(null, false, null), CancellationToken.None);
        tout.Should().HaveCount(3);
        tout.First(d => d.DeviceId == 1).Plate.Should().Be("HTZ 100");
        tout.First(d => d.DeviceId == 1).CompanyName.Should().Be("HERTZ");

        var sansVehicule = await handler.Handle(new GetAdminGpsDevicesQuery(null, true, null), CancellationToken.None);
        sansVehicule.Select(d => d.DeviceId).Should().BeEquivalentTo(new[] { 2, 3 });

        var hertz = await handler.Handle(new GetAdminGpsDevicesQuery(Hertz, false, null), CancellationToken.None);
        hertz.Select(d => d.DeviceId).Should().BeEquivalentTo(new[] { 1, 2 });

        var recherche = await handler.Handle(new GetAdminGpsDevicesQuery(null, false, "htz 100"), CancellationToken.None);
        recherche.Select(d => d.DeviceId).Should().BeEquivalentTo(new[] { 1 });
    }

    // ── Aides ──

    private static int Lignes(GpsDeviceDeletionImpactDto impact, string table) =>
        impact.Rows.First(r => r.Table == table).Rows;

    private static DeleteGpsDeviceCommandHandler Handler(TestGisDbContext context) =>
        new(context, Tenant(), NullLogger<DeleteGpsDeviceCommandHandler>.Instance);

    private static ICurrentTenantService Tenant()
    {
        var tenant = new Mock<ICurrentTenantService>();
        tenant.SetupGet(t => t.UserId).Returns(1);
        tenant.SetupGet(t => t.CompanyId).Returns(Belive);
        tenant.SetupGet(t => t.IsSystemAdmin).Returns(true);
        return tenant.Object;
    }

    private static GpsDevice Fiche(int id, string imei, string? mat, int companyId, string status, DateTime? lastCommunication) => new()
    {
        Id = id,
        DeviceUid = imei,
        Mat = mat,
        CompanyId = companyId,
        Status = status,
        LastCommunication = lastCommunication,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Vehicle Vehicule_(int id, string plate, int? deviceId, int companyId) => new()
    {
        Id = id,
        Name = plate,
        Plate = plate,
        Type = "voiture",
        Status = "available",
        CompanyId = companyId,
        GpsDeviceId = deviceId,
        HasGps = deviceId.HasValue,
        Mileage = 0,
        FuelType = "diesel"
    };

    private static async Task SemerSocietesAsync(TestGisDbContext context)
    {
        context.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        var belive = TestDataBuilder.CreateSociete(id: Belive, subscriptionTypeId: 1);
        belive.Name = "BELIVE";
        var hertz = TestDataBuilder.CreateSociete(id: Hertz, subscriptionTypeId: 1);
        hertz.Name = "HERTZ";
        context.Societes.AddRange(belive, hertz);
        await context.SaveChangesAsync();
    }

    /// <summary>Fiche #190939 (HERTZ) avec tout ce qu'un boîtier peut porter, sans véhicule.</summary>
    private static async Task SemerBoitierSansVehiculeAsync(TestGisDbContext context, int positions)
    {
        await SemerSocietesAsync(context);
        context.GpsDevices.Add(Fiche(Boitier, Imei, "NR08G1116", Hertz, "unassigned", DateTime.UtcNow.AddHours(-1)));
        var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < positions; i++)
            context.GpsPositions.Add(new GpsPosition { DeviceId = Boitier, RecordedAt = start.AddMinutes(i), Latitude = 36.8, Longitude = 10.18, IsValid = true });
        for (var i = 1; i <= 3; i++)
            context.GpsAlerts.Add(new GpsAlert { Id = i, DeviceId = Boitier, Timestamp = start });
        context.VehicleStops.Add(new VehicleStop { Id = 1, VehicleId = Vehicule, DeviceId = Boitier, StartTime = start });
        context.VehicleStops.Add(new VehicleStop { Id = 2, VehicleId = Vehicule, DeviceId = Boitier, StartTime = start.AddHours(1) });
        context.FuelRecords.Add(new FuelRecord { Id = 1, VehicleId = Vehicule, DeviceId = Boitier, RecordedAt = start });
        context.FuelRecords.Add(new FuelRecord { Id = 2, VehicleId = Vehicule, DeviceId = Boitier, RecordedAt = start.AddHours(1) });
        context.DeviceCommands.Add(new DeviceCommand { Id = 1, DeviceId = Boitier });
        context.DeviceEvents.Add(new DeviceEvent { Id = 1, DeviceId = Boitier, EventAt = start });
        context.TowEvents.Add(new TowEvent { Id = 1, DeviceId = Boitier, VehicleId = Vehicule, StartedAt = start, LastSeenAt = start });
        context.BatteryStartReadings.Add(new BatteryStartReading { Id = 1, DeviceId = Boitier, StartAt = start, BatteryRaw = 80 });
        context.GeofenceEvents.Add(new GeofenceEvent { Id = 1, DeviceId = Boitier, VehicleId = Vehicule });
        context.PoiVisits.Add(new PoiVisit { Id = 1, DeviceId = Boitier, VehicleId = Vehicule });
        context.Notifications.Add(new Notification { Id = 1, UserId = 1, CompanyId = Hertz, ReferenceType = "device", ReferenceId = Boitier });
        context.AccidentEvents.Add(new AccidentEvent { Id = 7, GpsDeviceId = Boitier, DeviceUid = Imei, CompanyId = Hertz, IncidentAt = start });
        await context.SaveChangesAsync();
    }

    /// <summary>Même fiche, rattachée au véhicule 524 « HTZ 345 » qui porte pleins, tournée, notification et sinistre.</summary>
    private static async Task SemerBoitierAvecVehiculeAsync(TestGisDbContext context, int positions)
    {
        await SemerBoitierSansVehiculeAsync(context, positions);
        var fiche = await context.GpsDevices.FirstAsync(d => d.Id == Boitier);
        fiche.Status = "assigned";
        context.Vehicles.Add(Vehicule_(Vehicule, "HTZ 345", Boitier, Hertz));
        context.FuelEntries.Add(new FuelEntry { Id = 1, VehicleId = Vehicule, CompanyId = Hertz, InvoiceDate = DateTime.UtcNow });
        context.FuelEntries.Add(new FuelEntry { Id = 2, VehicleId = Vehicule, CompanyId = Hertz, InvoiceDate = DateTime.UtcNow });
        context.Tours.Add(new Tour { Id = 1, VehicleId = Vehicule, CompanyId = Hertz, ScheduledStartTime = DateTime.UtcNow });
        context.TourWaypoints.Add(new TourWaypoint { Id = 1, TourId = 1 });
        context.Notifications.Add(new Notification { Id = 2, UserId = 1, CompanyId = Hertz, ReferenceType = "vehicle", ReferenceId = Vehicule });
        context.AccidentEvents.Add(new AccidentEvent { Id = 8, VehicleId = Vehicule, DeviceUid = Imei, CompanyId = Hertz, IncidentAt = DateTime.UtcNow });
        await context.SaveChangesAsync();
    }
}
