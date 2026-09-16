using FluentAssertions;
using GisAPI.Application.Features.Drivers.Commands;
using GisAPI.Application.Features.Vehicles.Commands.UpdateVehicle;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using MediatR;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// Affectation chauffeur ↔ véhicule — campagne de test Calypso GPA du
/// 11/09/2026 : modifier la couleur d'un véhicule effaçait son chauffeur
/// (DEF-006, le formulaire n'envoie aucun champ chauffeur et le handler écrasait
/// quand même), et les deux jambes de l'affectation vivaient chacune de leur
/// côté — un chauffeur affecté depuis l'écran Chauffeurs n'apparaissait pas dans
/// la colonne Chauffeur de l'écran Véhicules (DEF-005).
/// </summary>
public class VehicleDriverAssignmentTests
{
    private const int CompanyId = 7;

    private static Vehicle Veh(int id, string plate = "QA-002-LEA") => new()
    {
        Id = id,
        CompanyId = CompanyId,
        Name = $"QA véhicule {id}",
        Type = "Utilitaire",
        Plate = plate,
        Status = "available",
        Mileage = 8_000,
        Color = "Blanc"
    };

    private static Driver Chauffeur(int id, int? vehicleId = null) => new()
    {
        Id = id,
        CompanyId = CompanyId,
        FirstName = "QA-Chauffeur",
        LastName = $"QA-{id}",
        AssignedVehicleId = vehicleId,
        Status = "active"
    };

    private static UpdateVehicleCommandHandler VehicleHandler(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object);

    private static CreateDriverCommandHandler CreateDriverHandler(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object, new Mock<IPublisher>().Object);

    private static UpdateDriverCommandHandler UpdateDriverHandler(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object);

    /// Corps EXACT du formulaire véhicule : aucun champ chauffeur ni superviseur.
    private static UpdateVehicleCommand CorpsDuFormulaire(int id, string couleur, int? assignedDriverId = null) => new(
        Id: id, Name: "QA véhicule", Type: "Utilitaire",
        Brand: null, Model: null, Plate: "QA-002-LEA", Year: null, Color: couleur,
        Status: "available", Mileage: 0, FuelType: "Diesel", FuelTankCapacity: 50,
        AssignedDriverId: assignedDriverId, AssignedSupervisorId: null,
        AcquisitionType: null, PurchasePrice: null, PurchaseDate: null,
        LeasingMonthlyPayment: null, LeasingDurationMonths: null, LeasingStartDate: null,
        LeasingPaymentDay: null, RegistrationDate: null,
        InsuranceStartDate: null, InsuranceExpiry: null, InsuranceReminderDays: null,
        TaxStartDate: null, TaxExpiry: null, TaxReminderDays: null,
        TechnicalInspectionStartDate: null, TechnicalInspectionExpiry: null,
        TechnicalInspectionReminderDays: null);

    private static UpdateDriverCommand CorpsChauffeur(int id, int? assignedVehicleId) => new(
        Id: id, FirstName: "QA-Chauffeur", LastName: $"QA-{id}",
        Email: null, Phone: null, PermitNumber: null, PermitType: null, PermitExpiry: null,
        CIN: null, DateOfBirth: null, HireDate: null,
        AssignedVehicleId: assignedVehicleId, Status: null);

    // ── DEF-006 : le formulaire véhicule ne touche plus au chauffeur ──────────

    [Fact]
    public async Task Changer_la_couleur_ne_vide_plus_le_chauffeur()
    {
        using var ctx = TestDbContextFactory.Create();
        var vehicle = Veh(84);
        vehicle.AssignedDriverId = 73;
        vehicle.AssignedSupervisorId = 5;
        ctx.Vehicles.Add(vehicle);
        ctx.Drivers.Add(Chauffeur(73, 84));
        await ctx.SaveChangesAsync();

        await VehicleHandler(ctx).Handle(CorpsDuFormulaire(84, "Bleu"), CancellationToken.None);

        var saved = ctx.Vehicles.Single();
        saved.Color.Should().Be("Bleu");
        saved.AssignedDriverId.Should().Be(73, "un champ absent du corps n'est pas un champ vidé");
        saved.AssignedSupervisorId.Should().Be(5);
        ctx.Drivers.Single().AssignedVehicleId.Should().Be(84);
    }

    [Fact]
    public async Task Zero_desaffecte_explicitement_le_chauffeur_des_deux_cotes()
    {
        using var ctx = TestDbContextFactory.Create();
        var vehicle = Veh(84);
        vehicle.AssignedDriverId = 73;
        ctx.Vehicles.Add(vehicle);
        ctx.Drivers.Add(Chauffeur(73, 84));
        await ctx.SaveChangesAsync();

        await VehicleHandler(ctx).Handle(CorpsDuFormulaire(84, "Blanc", assignedDriverId: 0), CancellationToken.None);

        ctx.Vehicles.Single().AssignedDriverId.Should().BeNull();
        ctx.Drivers.Single().AssignedVehicleId.Should().BeNull();
    }

    // ── DEF-005 : les deux jambes de l'affectation restent cohérentes ─────────

    [Fact]
    public async Task Affecter_depuis_le_vehicule_renseigne_aussi_le_chauffeur()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(Veh(85));
        ctx.Drivers.Add(Chauffeur(75));
        await ctx.SaveChangesAsync();

        await VehicleHandler(ctx).Handle(CorpsDuFormulaire(85, "Blanc", assignedDriverId: 75), CancellationToken.None);

        ctx.Vehicles.Single().AssignedDriverId.Should().Be(75);
        ctx.Drivers.Single().AssignedVehicleId.Should().Be(85);
    }

    [Fact]
    public async Task Creer_un_chauffeur_affecte_renseigne_aussi_le_vehicule()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(Veh(85));
        await ctx.SaveChangesAsync();

        var dto = await CreateDriverHandler(ctx).Handle(new CreateDriverCommand(
            FirstName: "QA-Chauffeur", LastName: "QA-V1",
            Email: null, Phone: null, PermitNumber: null, PermitType: null, PermitExpiry: null,
            CIN: null, DateOfBirth: null, HireDate: null,
            AssignedVehicleId: 85), CancellationToken.None);

        ctx.Vehicles.Single().AssignedDriverId.Should().Be(dto.Id);
    }

    [Fact]
    public async Task Changer_le_vehicule_d_un_chauffeur_libere_l_ancien_vehicule()
    {
        using var ctx = TestDbContextFactory.Create();
        var ancien = Veh(84, "QA-084");
        ancien.AssignedDriverId = 73;
        ctx.Vehicles.Add(ancien);
        ctx.Vehicles.Add(Veh(85, "QA-085"));
        ctx.Drivers.Add(Chauffeur(73, 84));
        await ctx.SaveChangesAsync();

        await UpdateDriverHandler(ctx).Handle(CorpsChauffeur(73, 85), CancellationToken.None);

        ctx.Vehicles.Single(v => v.Id == 84).AssignedDriverId.Should().BeNull();
        ctx.Vehicles.Single(v => v.Id == 85).AssignedDriverId.Should().Be(73);
    }

    [Fact]
    public async Task Retirer_le_vehicule_d_un_chauffeur_vide_la_colonne_chauffeur_du_vehicule()
    {
        using var ctx = TestDbContextFactory.Create();
        var vehicle = Veh(85);
        vehicle.AssignedDriverId = 73;
        ctx.Vehicles.Add(vehicle);
        ctx.Drivers.Add(Chauffeur(73, 85));
        await ctx.SaveChangesAsync();

        await UpdateDriverHandler(ctx).Handle(CorpsChauffeur(73, null), CancellationToken.None);

        ctx.Vehicles.Single().AssignedDriverId.Should().BeNull();
        ctx.Drivers.Single().AssignedVehicleId.Should().BeNull();
    }

    [Fact]
    public async Task Un_vehicule_n_a_qu_un_chauffeur_l_ancien_est_libere()
    {
        using var ctx = TestDbContextFactory.Create();
        var vehicle = Veh(85);
        vehicle.AssignedDriverId = 73;
        ctx.Vehicles.Add(vehicle);
        ctx.Drivers.Add(Chauffeur(73, 85));
        ctx.Drivers.Add(Chauffeur(74));
        await ctx.SaveChangesAsync();

        await VehicleHandler(ctx).Handle(CorpsDuFormulaire(85, "Blanc", assignedDriverId: 74), CancellationToken.None);

        ctx.Vehicles.Single().AssignedDriverId.Should().Be(74);
        ctx.Drivers.Single(d => d.Id == 73).AssignedVehicleId.Should().BeNull();
        ctx.Drivers.Single(d => d.Id == 74).AssignedVehicleId.Should().Be(85);
    }

    [Fact]
    public async Task Une_affectation_deja_divergente_est_reparee_au_prochain_enregistrement()
    {
        using var ctx = TestDbContextFactory.Create();
        // Donnée héritée : le véhicule pointe le chauffeur, le chauffeur ne pointe rien.
        var vehicle = Veh(85);
        vehicle.AssignedDriverId = 75;
        ctx.Vehicles.Add(vehicle);
        ctx.Drivers.Add(Chauffeur(75));
        await ctx.SaveChangesAsync();

        await VehicleHandler(ctx).Handle(CorpsDuFormulaire(85, "Blanc", assignedDriverId: 75), CancellationToken.None);

        ctx.Drivers.Single().AssignedVehicleId.Should().Be(85);
    }

    [Theory]
    [InlineData(90)]  // chauffeur d'une autre société
    [InlineData(404)] // identifiant inconnu (la clé étrangère le refusait en 500)
    public async Task Un_chauffeur_hors_de_la_societe_du_vehicule_est_refuse(int chauffeurDemande)
    {
        using var ctx = TestDbContextFactory.Create();
        var vehicle = Veh(85);
        vehicle.AssignedDriverId = 73;
        ctx.Vehicles.Add(vehicle);
        ctx.Drivers.Add(Chauffeur(73, 85));
        ctx.Drivers.Add(new Driver { Id = 90, CompanyId = 99, FirstName = "Autre", LastName = "Société" });
        await ctx.SaveChangesAsync();

        var act = () => VehicleHandler(ctx).Handle(
            CorpsDuFormulaire(85, "Rouge", assignedDriverId: chauffeurDemande), CancellationToken.None);

        // DomainException exacte = 400 avec le message (ExceptionHandlingMiddleware).
        (await act.Should().ThrowExactlyAsync<DomainException>())
            .Which.Message.Should().Contain("chauffeur de sa propre société");

        // Relu depuis la base : rien n'a bougé, ni le véhicule ni les chauffeurs.
        ctx.ChangeTracker.Clear();
        var saved = ctx.Vehicles.Single();
        saved.AssignedDriverId.Should().Be(73);
        saved.Color.Should().Be("Blanc");
        ctx.Drivers.Single(d => d.Id == 73).AssignedVehicleId.Should().Be(85);
        ctx.Drivers.Single(d => d.Id == 90).AssignedVehicleId.Should().BeNull();
    }

    // ── Volet chauffeur : désaffecter reste possible, 0 vaut null ─────────────

    [Fact]
    public async Task Zero_cote_chauffeur_desaffecte_comme_aucun_vehicule()
    {
        using var ctx = TestDbContextFactory.Create();
        var vehicle = Veh(85);
        vehicle.AssignedDriverId = 73;
        ctx.Vehicles.Add(vehicle);
        ctx.Drivers.Add(Chauffeur(73, 85));
        await ctx.SaveChangesAsync();

        await UpdateDriverHandler(ctx).Handle(CorpsChauffeur(73, 0), CancellationToken.None);

        ctx.Drivers.Single().AssignedVehicleId.Should().BeNull();
        ctx.Vehicles.Single().AssignedDriverId.Should().BeNull();
    }
}
