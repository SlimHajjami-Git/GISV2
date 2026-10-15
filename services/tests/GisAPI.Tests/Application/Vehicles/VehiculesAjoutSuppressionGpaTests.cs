using FluentAssertions;
using GisAPI.Application.Features.Vehicles.Commands.CreateVehicle;
using GisAPI.Application.Features.Vehicles.Commands.DeleteVehicle;
using GisAPI.Application.Features.Vehicles.Queries.GetVehicleDeletionPreview;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// Règle donnée par Karim le 15/10/2026 : un client Calypso GPS n'ajoute ni ne supprime de
/// véhicule (ils arrivent avec leur boîtier, par l'équipe Calypso) ; seul Calypso GPA le fait.
/// La suppression efface l'historique (choix A) — sauf les pleins et les sinistres, conservés
/// détachés du véhicule comme dans l'administration : la fenêtre de confirmation chiffre les deux.
/// </summary>
public class VehiculesAjoutSuppressionGpaTests
{
    private const int CompanyId = 7;
    private static readonly DateTime Jour = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<TestGisDbContext> SocieteAsync(bool gps)
    {
        var ctx = TestDbContextFactory.Create();
        var abonnement = TestDataBuilder.CreateSubscriptionType(1);
        abonnement.ModuleMonitoring = gps;
        ctx.SubscriptionTypes.Add(abonnement);
        ctx.Societes.Add(TestDataBuilder.CreateSociete(CompanyId));
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Clio", Plate = "GE-768-HJ", CompanyId = CompanyId, Status = "available", Mileage = 10_000 });
        ctx.FuelEntries.AddRange(
            new FuelEntry { Id = 1, VehicleId = 1, CompanyId = CompanyId, VehiclePlate = "GE-768-HJ", Volume = 40, PricePerLiter = 2, TotalAmount = 80, InvoiceDate = Jour },
            new FuelEntry { Id = 2, VehicleId = 1, CompanyId = CompanyId, VehiclePlate = "GE-768-HJ", Volume = 30, PricePerLiter = 2, TotalAmount = 60, InvoiceDate = Jour });
        ctx.VehicleCosts.Add(new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "parking", Description = "Parking", Amount = 12m, Date = Jour });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static Mock<GisAPI.Domain.Interfaces.ICurrentTenantService> Client(bool systemAdmin = false)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: 1);
        tenant.Setup(t => t.IsSystemAdmin).Returns(systemAdmin);
        return tenant;
    }

    private static Task<int> CreerAsync(TestGisDbContext ctx, bool systemAdmin = false) =>
        new CreateVehicleCommandHandler(ctx, Client(systemAdmin).Object, Mock.Of<IPublisher>())
            .Handle(new CreateVehicleCommand("Kangoo", "utilitaire", "Renault", "Kangoo", "QA-15-BIS", 2024, null, 0, "diesel"), CancellationToken.None);

    private static Task SupprimerAsync(TestGisDbContext ctx) =>
        new DeleteVehicleCommandHandler(ctx, Client().Object).Handle(new DeleteVehicleCommand(1), CancellationToken.None);

    private static Task<VehicleDeletionPreviewDto> ApercuAsync(TestGisDbContext ctx) =>
        new GetVehicleDeletionPreviewQueryHandler(ctx, Client().Object).Handle(new GetVehicleDeletionPreviewQuery(1), CancellationToken.None);

    [Fact]
    public async Task Un_client_GPS_ne_cree_pas_de_vehicule()
    {
        using var ctx = await SocieteAsync(gps: true);

        var act = () => CreerAsync(ctx);

        await act.Should().ThrowAsync<ForbiddenAccessException>()
            .WithMessage("Les véhicules d'un compte GPS sont ajoutés par l'équipe Calypso*");
        (await ctx.Vehicles.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Une_societe_sans_abonnement_n_ajoute_pas_de_vehicule_comme_a_l_ecran()
    {
        using var ctx = await SocieteAsync(gps: false);
        (await ctx.Societes.SingleAsync(s => s.Id == CompanyId)).SubscriptionTypeId = null;
        await ctx.SaveChangesAsync();

        await ((Func<Task>)(() => CreerAsync(ctx))).Should().ThrowAsync<ForbiddenAccessException>();
    }

    [Fact]
    public async Task L_equipe_Calypso_cree_toujours_un_vehicule_dans_un_compte_GPS()
    {
        using var ctx = await SocieteAsync(gps: true);

        (await CreerAsync(ctx, systemAdmin: true)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Un_client_GPA_cree_un_vehicule()
    {
        using var ctx = await SocieteAsync(gps: false);

        (await CreerAsync(ctx)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Un_client_GPS_ne_supprime_pas_de_vehicule()
    {
        using var ctx = await SocieteAsync(gps: true);

        await ((Func<Task>)(() => SupprimerAsync(ctx))).Should().ThrowAsync<ForbiddenAccessException>()
            .WithMessage("Les véhicules d'un compte GPS sont retirés par l'équipe Calypso*");
        await ((Func<Task>)(() => ApercuAsync(ctx))).Should().ThrowAsync<ForbiddenAccessException>();
        (await ctx.Vehicles.CountAsync()).Should().Be(1);
        (await ctx.FuelEntries.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task La_fenetre_de_confirmation_chiffre_l_historique_qui_sera_efface()
    {
        using var ctx = await SocieteAsync(gps: false);

        var apercu = await ApercuAsync(ctx);

        apercu.Plate.Should().Be("GE-768-HJ");
        apercu.History.Select(h => $"{h.Count} {h.Label}").Should().Equal("1 dépense");
        apercu.Kept.Select(h => $"{h.Count} {h.Label}").Should().Equal("2 pleins");
    }

    [Fact]
    public async Task Un_client_GPA_supprime_le_vehicule_et_son_historique_les_pleins_restent_avec_la_plaque()
    {
        using var ctx = await SocieteAsync(gps: false);

        await SupprimerAsync(ctx);

        ctx.ChangeTracker.Clear();
        (await ctx.Vehicles.CountAsync()).Should().Be(0);
        (await ctx.VehicleCosts.CountAsync()).Should().Be(0);
        (await ctx.FuelEntries.Select(f => new { f.VehicleId, f.VehiclePlate }).ToListAsync())
            .Should().AllSatisfy(f => { f.VehicleId.Should().BeNull(); f.VehiclePlate.Should().Be("GE-768-HJ"); });
    }
}
