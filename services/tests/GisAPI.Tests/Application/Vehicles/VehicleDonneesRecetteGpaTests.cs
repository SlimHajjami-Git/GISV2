using FluentAssertions;
using GisAPI.Application.Features.Vehicles.Commands.CreateVehicle;
using GisAPI.Application.Features.Vehicles.Commands.PatchVehicle;
using GisAPI.Application.Features.Vehicles.Queries.GetVehicleDetails;
using GisAPI.Application.Features.Vehicles.Queries.GetVehicles;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using MediatR;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// Pertes de données silencieuses sur la fiche véhicule — campagne de test
/// Calypso GPA du 11/09/2026 : le contrat d'acquisition saisi à la création
/// était jeté (DEF-003), le PATCH faisait reculer le compteur sans motif
/// (DEF-004), et l'état d'immobilisation n'était pas restitué par les routes de
/// lecture, si bien qu'un véhicule immobilisé repassait « disponible » au
/// premier rechargement (DEF-008).
/// </summary>
public class VehicleDonneesRecetteGpaTests
{
    private const int CompanyId = 7;

    private static CreateVehicleCommandHandler CreateHandler(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object, new Mock<IPublisher>().Object);

    private static Vehicle Veh(int id, int mileage = 8_000) => new()
    {
        Id = id,
        CompanyId = CompanyId,
        Name = "QA-LEA",
        Type = "Utilitaire",
        Plate = "QA-002-LEA",
        Status = "available",
        Mileage = mileage
    };

    // ── DEF-003 : contrat d'acquisition saisi à la création ───────────────────

    [Fact]
    public async Task Le_contrat_de_leasing_saisi_a_la_creation_est_enregistre_avec_son_echeancier()
    {
        using var ctx = TestDbContextFactory.Create();

        var id = await CreateHandler(ctx).Handle(new CreateVehicleCommand(
            Name: "QA-LEA Commercial test",
            Type: "Utilitaire",
            Brand: null, Model: null, Plate: "QA-002-LEA", Year: null, Color: null,
            Mileage: 8_000, FuelType: "Diesel", FuelTankCapacity: 60,
            GpsDeviceId: null, NewGpsDevice: null,
            AcquisitionType: "leasing",
            PurchasePrice: 3_000m,
            PurchaseDate: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            LeasingMonthlyPayment: 250m,
            LeasingDurationMonths: 12,
            LeasingStartDate: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            LeasingPaymentDay: 10), CancellationToken.None);

        var vehicle = ctx.Vehicles.Single(v => v.Id == id);
        vehicle.AcquisitionType.Should().Be("leasing");
        vehicle.PurchasePrice.Should().Be(3_000m);
        vehicle.PurchaseDate.Should().Be(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        vehicle.LeasingMonthlyPayment.Should().Be(250m);
        vehicle.LeasingDurationMonths.Should().Be(12);
        vehicle.LeasingPaymentDay.Should().Be(10);

        // 12 mensualités + l'apport : l'échéancier est généré dès la création,
        // comme après une modification du contrat.
        var lines = ctx.AcquisitionPayments.Where(p => p.VehicleId == id).ToList();
        lines.Count(p => p.Kind == AcquisitionPayment.Kinds.Mensualite).Should().Be(12);
        lines.Count(p => p.Kind == AcquisitionPayment.Kinds.Apport).Should().Be(1);
    }

    [Fact]
    public async Task Un_vehicule_cree_sans_contrat_reste_en_achat_comptant_sans_echeancier()
    {
        using var ctx = TestDbContextFactory.Create();

        var id = await CreateHandler(ctx).Handle(new CreateVehicleCommand(
            Name: "QA sans contrat", Type: "Citadine",
            Brand: null, Model: null, Plate: "QA-001", Year: null, Color: null),
            CancellationToken.None);

        ctx.Vehicles.Single(v => v.Id == id).AcquisitionType.Should().Be("purchase");
        ctx.AcquisitionPayments.Should().BeEmpty();
    }

    // ── DEF-004 : le PATCH ne fait plus reculer le compteur ───────────────────

    private static PatchVehicleCommand Patch(int id, int? mileage) => new(
        Id: id, SpeedLimit: null, DepartmentId: null, FuelType: null,
        Brand: null, Model: null, Plate: null, Year: null, Color: null,
        Mileage: mileage, FuelTankCapacity: null,
        AcquisitionType: null, PurchasePrice: null, LeasingMonthlyPayment: null,
        LeasingDurationMonths: null, LeasingStartDate: null, LeasingPaymentDay: null,
        RegistrationDate: null, PurchaseDate: null);

    private static async Task<TestGisDbContext> AvecVehiculeAsync(int mileage = 8_000)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(Veh(85, mileage));
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static PatchVehicleCommandHandler PatchHandler(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object);

    [Fact]
    public async Task Le_patch_refuse_un_kilometrage_inferieur_comme_le_put()
    {
        using var ctx = await AvecVehiculeAsync(8_000);

        var act = () => PatchHandler(ctx).Handle(Patch(85, 10), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*inférieur au kilométrage*");
        ctx.Vehicles.Single().Mileage.Should().Be(8_000);
    }

    [Fact]
    public async Task Le_patch_accepte_un_kilometrage_superieur()
    {
        using var ctx = await AvecVehiculeAsync(8_000);

        await PatchHandler(ctx).Handle(Patch(85, 9_500), CancellationToken.None);

        ctx.Vehicles.Single().Mileage.Should().Be(9_500);
    }

    [Theory]
    [InlineData(null)]  // champ absent du corps : le formulaire d'acquisition n'envoie pas le compteur
    [InlineData(0)]     // champ non renseigné
    public async Task Le_patch_sans_kilometrage_laisse_le_compteur_en_place(int? mileage)
    {
        using var ctx = await AvecVehiculeAsync(8_000);

        await PatchHandler(ctx).Handle(Patch(85, mileage), CancellationToken.None);

        ctx.Vehicles.Single().Mileage.Should().Be(8_000);
    }

    // ── DEF-008 : l'immobilisation est restituée par les routes de lecture ────

    [Fact]
    public async Task La_liste_et_la_fiche_portent_l_etat_d_immobilisation()
    {
        using var ctx = TestDbContextFactory.Create();
        var vehicle = Veh(85);
        vehicle.IsImmobilized = true;
        vehicle.ImmobilizationReason = "QA-immobilisé pour contrôle";
        vehicle.ImmobilizationStartedAt = new DateTime(2026, 9, 11, 8, 0, 0, DateTimeKind.Utc);
        ctx.Vehicles.Add(vehicle);
        await ctx.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(CompanyId).Object;

        var liste = await new GetVehiclesQueryHandler(ctx, tenant)
            .Handle(new GetVehiclesQuery(PageSize: 500), CancellationToken.None);
        var listed = liste.Items.Single();
        listed.IsImmobilized.Should().BeTrue();
        listed.ImmobilizationReason.Should().Be("QA-immobilisé pour contrôle");
        listed.ImmobilizationStartedAt.Should().NotBeNull();

        var fiche = await new GetVehicleDetailsQueryHandler(ctx, tenant)
            .Handle(new GetVehicleDetailsQuery(85), CancellationToken.None);
        fiche!.IsImmobilized.Should().BeTrue();
        fiche.ImmobilizationReason.Should().Be("QA-immobilisé pour contrôle");
        fiche.ImmobilizationStartedAt.Should().NotBeNull();
    }
}
