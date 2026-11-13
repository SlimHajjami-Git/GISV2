using FluentAssertions;
using GisAPI.Application.Features.Vehicles.Commands.SyncMileage;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// Incident NEMS L du 09/11/2026 : « Synchroniser » recopiait le dernier compteur du boîtier sans
/// contrôle et a écrasé six fiches (242… → 589 824 km). Mêmes règles que l'ingestion désormais :
/// plafond, jamais de recul, saut maximal ; et refus d'un boîtier aux données CAN non fiables.
/// </summary>
public class SyncMileageGuardTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    // ── Règles ──

    [Fact]
    public void Compteur_de_l_incident_refuse_saut_de_560_000_km_en_un_jour() =>
        SyncMileageCommandHandler.Refusal(648_701, 87_727, Now.AddDays(-1), Now).Should().Contain("maximum plausible");

    [Fact]
    public void Compteur_invraisemblable_refuse_quel_que_soit_l_historique() =>
        SyncMileageCommandHandler.Refusal(2_000_000, 0, null, Now).Should().Contain("invraisemblable");

    [Fact]
    public void Un_kilometrage_ne_recule_jamais() =>
        SyncMileageCommandHandler.Refusal(79_870, 79_897, Now.AddHours(-1), Now).Should().Contain("ne recule pas");

    [Theory]
    [InlineData(87_727, 87_900, 0)]      // même jour, 173 km : sous les 200 km toujours admis
    [InlineData(87_727, 88_500, 1)]      // un jour, 773 km : sous 200 + 1 500
    [InlineData(62_749, 75_000, 10)]     // fiche en retard de 10 jours
    [InlineData(0, 116_972, 0)]          // fiche vide (véhicule neuf dans l'application)
    public void Compteurs_plausibles_acceptes(int fiche, long compteur, int joursDepuisMaj) =>
        SyncMileageCommandHandler.Refusal(compteur, fiche, Now.AddDays(-joursDepuisMaj), Now).Should().BeNull();

    // ── Commande ──

    [Fact]
    public async Task Synchronisation_normale_met_la_fiche_a_jour()
    {
        var (db, handler) = Setup(fiche: 87_727, compteurs: new long[] { 87_850 });

        var r = await handler.Handle(new SyncMileageCommand(1), CancellationToken.None);

        r.Updated.Should().BeTrue();
        r.NewMileage.Should().Be(87_850);
        r.Message.Should().BeNull();
        (await db.Vehicles.AsNoTracking().SingleAsync()).Mileage.Should().Be(87_850);
    }

    [Fact]
    public async Task Compteur_faux_de_l_incident_ne_touche_pas_la_fiche()
    {
        var (db, handler) = Setup(fiche: 116_972, compteurs: new long[] { 116_980, 589_824 });

        var r = await handler.Handle(new SyncMileageCommand(1), CancellationToken.None);

        r.Updated.Should().BeFalse();
        r.Message.Should().Contain("maximum plausible");
        (await db.Vehicles.AsNoTracking().SingleAsync()).Mileage.Should().Be(116_972);
    }

    [Fact]
    public async Task Firmware_L_ne_fait_plus_reculer_la_fiche()
    {
        var (db, handler) = Setup(fiche: 79_897, compteurs: new long[] { 79_870 }, firmware: "L");

        var r = await handler.Handle(new SyncMileageCommand(1), CancellationToken.None);

        r.Updated.Should().BeFalse();
        (await db.Vehicles.AsNoTracking().SingleAsync()).Mileage.Should().Be(79_897);
    }

    [Fact]
    public async Task Boitier_aux_donnees_CAN_non_fiables_synchronisation_suspendue()
    {
        var (db, handler) = Setup(fiche: 50_422, compteurs: new long[] { 50_500 }, untrusted: true);

        var r = await handler.Handle(new SyncMileageCommand(1), CancellationToken.None);

        r.Updated.Should().BeFalse();
        r.Message.Should().Contain("en cours de vérification");
        (await db.Vehicles.AsNoTracking().SingleAsync()).Mileage.Should().Be(50_422);
    }

    private static (TestGisDbContext db, SyncMileageCommandHandler handler) Setup(
        int fiche, long[] compteurs, string firmware = "L", bool untrusted = false)
    {
        var db = TestDbContextFactory.Create();
        db.GpsDevices.Add(new GpsDevice { Id = 10, CompanyId = 1, DeviceUid = "860000000000010", FirmwareVersion = firmware, FmsUntrusted = untrusted });
        db.Vehicles.Add(new Vehicle { Id = 1, CompanyId = 1, Name = "249 TU 3525", Plate = "249 TU 3525", Mileage = fiche, GpsDeviceId = 10 });
        var t = DateTime.UtcNow.AddMinutes(-compteurs.Length);
        foreach (var km in compteurs)
        {
            t = t.AddMinutes(1);
            db.GpsPositions.Add(new GpsPosition { DeviceId = 10, RecordedAt = t, Latitude = 36.8, Longitude = 10.18, OdometerKm = km });
        }
        db.SaveChanges();
        // La fiche vient d'être touchée : le saut admis est celui d'une journée en cours.
        db.Vehicles.Single().UpdatedAt = DateTime.UtcNow.AddHours(-1);
        db.SaveChanges();
        db.ChangeTracker.Clear();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1, userId: 1);
        return (db, new SyncMileageCommandHandler(db, tenant.Object));
    }
}
