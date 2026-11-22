using FluentAssertions;
using GisAPI.Application.Features.FuelEntries.Commands;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using MediatR;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// Import de 405 pleins d'octobre (20/11/2026) : 8 fiches véhicule portées à une valeur
/// fausse par une faute de frappe dans le kilométrage — 221 030 km puis 2 215 595 km,
/// 433 988 puis 464 624 km six jours plus tard, une Caddy neuve à 3 900 km portée à
/// 1 525 727 km. Un compteur ne reculant jamais, la fiche restait fausse sur la carte et
/// figée sur les véhicules au compteur CAN. Les séquences ci-dessous sont celles du client.
/// </summary>
public class ReleveInvraisemblableTests
{
    private const int CompanyId = 15;
    private const int Gazole = 1;
    private static readonly DateTime Maintenant = new(2026, 11, 20, 9, 24, 0, DateTimeKind.Utc);

    private static Vehicle V(int mileage, DateTime? updatedAt = null) =>
        new() { Id = 1, CompanyId = CompanyId, Plate = "263 TU 6994", Mileage = mileage, UpdatedAt = updatedAt ?? Maintenant };

    // ── Face à la fiche ─────────────────────────────────────────────────────────

    [Fact]
    public void Une_Caddy_a_3900_km_ne_passe_pas_a_1525727_km()
    {
        var motif = VehicleMileage.EcartInvraisemblable(V(3_900), 1_525_727, Maintenant);

        motif.Should().NotBeNull();
        Chiffres(motif!).Should().Contain("1525727").And.Contain("3900");
    }

    [Fact]
    public void L_ecart_normal_entre_tableau_de_bord_et_boitier_passe()
    {
        // Même client, même jour : compteur CAN 639 482, tableau de bord 652 830.
        VehicleMileage.EcartInvraisemblable(V(639_482), 652_830, Maintenant).Should().BeNull();
    }

    [Fact]
    public void Une_fiche_a_zero_ne_borne_rien_sauf_le_plafond()
    {
        VehicleMileage.EcartInvraisemblable(V(0), 450_000, Maintenant).Should().BeNull();
        VehicleMileage.EcartInvraisemblable(V(0), 2_215_595, Maintenant).Should().NotBeNull();
    }

    [Fact]
    public void Une_fiche_restee_sans_releve_trois_mois_laisse_rouler_le_vehicule()
    {
        // Véhicule sans boîtier : 25 000 km en 90 jours, c'est un véhicule qui travaille.
        var fiche = V(50_000, Maintenant.AddDays(-90));

        VehicleMileage.EcartInvraisemblable(fiche, 75_000, Maintenant).Should().BeNull();
        VehicleMileage.EcartInvraisemblable(fiche, 500_000, Maintenant).Should().NotBeNull();
    }

    [Fact]
    public void Advance_ne_porte_pas_la_fiche_a_une_valeur_aberrante()
    {
        var v = V(3_900);

        VehicleMileage.Advance(v, 1_525_727, Maintenant).Should().BeFalse();
        v.Mileage.Should().Be(3_900);

        VehicleMileage.Advance(v, 4_150, Maintenant).Should().BeTrue();
        v.Mileage.Should().Be(4_150);
    }

    [Fact]
    public void Le_kilometrage_declare_du_vehicule_reste_libre()
    {
        // Colonne « km » de l'import des véhicules, comme le formulaire : moyen assumé
        // de remonter une fiche trop basse (boîtier posé sur un véhicule à 150 000 km).
        var v = V(2_000);

        VehicleMileage.AdvanceDeclared(v, 150_000).Should().BeTrue();
        v.Mileage.Should().Be(150_000);
    }

    // ── Saisie et import des pleins ─────────────────────────────────────────────

    private static async Task<TestGisDbContext> ParcAsync(int fiche, params (DateTime Date, long Km)[] relevesAnterieurs)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.FuelTypes.Add(new FuelType { Id = Gazole, Code = "diesel", Name = "Gazole" });
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Caddy", Plate = "187 TU 8531", CompanyId = CompanyId, Mileage = fiche });
        foreach (var (date, km) in relevesAnterieurs)
            ctx.FuelEntries.Add(new FuelEntry
            {
                CompanyId = CompanyId, VehicleId = 1, VehiclePlate = "187 TU 8531", FuelTypeId = Gazole,
                Volume = 40, PricePerLiter = 2.2m, TotalAmount = 88, InvoiceDate = date, OdometerKm = km
            });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static Task<int> PleinAsync(TestGisDbContext ctx, DateTime date, long km) =>
        new CreateFuelEntryCommandHandler(ctx, TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: 1).Object, Mock.Of<IPublisher>())
            .Handle(new CreateFuelEntryCommand("187 TU 8531", Gazole, 18.14m, 2.2m, date, null, null, null, null, km),
                CancellationToken.None);

    private static DateTime Oct(int jour) => new(2026, 10, jour, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Deux_chiffres_permutes_sont_refuses_face_au_releve_precedent()
    {
        using var ctx = await ParcAsync(437_679, (Oct(12), 433_407), (Oct(17), 433_988));

        var act = () => PleinAsync(ctx, Oct(23), 464_624);

        var erreur = await act.Should().ThrowAsync<DomainException>();
        Chiffres(erreur.Which.Message).Should().Contain("464624").And.Contain("433988").And.Contain("17/10/2026");
        ctx.FuelEntries.Should().HaveCount(2, "la ligne refusée n'est pas enregistrée");
        ctx.Vehicles.Single().Mileage.Should().Be(437_679);
    }

    [Fact]
    public async Task La_bonne_valeur_passe()
    {
        using var ctx = await ParcAsync(437_679, (Oct(12), 433_407), (Oct(17), 433_988));

        (await PleinAsync(ctx, Oct(23), 434_624)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Un_chiffre_en_trop_sans_releve_precedent_est_refuse_face_a_la_fiche()
    {
        using var ctx = await ParcAsync(3_900);

        var act = () => PleinAsync(ctx, Oct(5), 1_525_727);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*vérifiez la valeur*");
        ctx.Vehicles.Single().Mileage.Should().Be(3_900);
    }

    [Fact]
    public async Task Un_mois_de_pleins_reguliers_au_dessus_du_compteur_boitier_passe_et_avance_la_fiche()
    {
        // 209 TU 6190 : compteur CAN 639 482, pleins d'octobre de 646 549 à 652 830.
        using var ctx = await ParcAsync(639_482, (Oct(1), 646_549), (Oct(28), 652_100));

        (await PleinAsync(ctx, Oct(31), 652_830)).Should().BeGreaterThan(0);

        ctx.Vehicles.Single().Mileage.Should().Be(652_830);
    }

    [Fact]
    public async Task Un_premier_releve_sur_une_fiche_GPS_partie_de_zero_passe()
    {
        // 263 TU 6998 : fiche GPS à 80 km, tableau de bord à 5 468 km.
        using var ctx = await ParcAsync(80);

        (await PleinAsync(ctx, Oct(30), 5_468)).Should().BeGreaterThan(0);

        ctx.Vehicles.Single().Mileage.Should().Be(5_468);
    }

    /// <summary>Le séparateur de milliers dépend de la culture : on compare les chiffres seuls.</summary>
    private static string Chiffres(string texte) => new(texte.Where(c => !char.IsWhiteSpace(c) && c != ' ').ToArray());
}
