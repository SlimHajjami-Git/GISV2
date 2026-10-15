using FluentAssertions;
using GisAPI.Application.Features.VehicleMaintenance;
using GisAPI.Application.Features.VehicleMaintenance.Commands;
using GisAPI.Application.Services;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.MaintenanceTemplates;

/// <summary>
/// Deux modèles de vidange sur un même véhicule (campagne de tests GPA du 06/10/2026, erreur
/// n°17, véhicule 43) : la vidange faite sur l'un laissait l'autre « en retard ». Les noms des
/// cas ci-dessous sont ceux des modèles réels de production (15/10/2026).
/// </summary>
public class EntretiensDuMemeTypeTests
{
    [Theory]
    [InlineData("Vidange + filtre à huile", "Vidange Moteur")]
    [InlineData("VIDANGE MOTEUR HUILE + FIL HUILE", "Vidange moteur 10000")]
    [InlineData("Vidange moteur 5000", "Vidange moteur 10000")]
    [InlineData("Changement pneus", "CHANGMENT PNEUS")]
    [InlineData("Chaine de distribution", "Changement Kit Chaîne Distribution ")]
    [InlineData("Bougie", "BOUGIE D'ALLUMAGE")]
    [InlineData("Filtres air + habitacle", "FILTRE AIR")]
    public void Meme_type(string a, string b) =>
        MaintenanceTemplateFamily.SameFamily(a, b).Should().BeTrue();

    [Theory]
    [InlineData("Bougie", "Vidange moteur 10000")]          // même catégorie, même intervalle
    [InlineData("Huile boite de vitesse", "Vidange Moteur")]
    [InlineData("FILTRE AIR", "FILTRE CLIM")]
    [InlineData("FILTRE AIR", "Filtre à Essence")]
    [InlineData("Changement pneus", "Changement Kit Chaîne Distribution")]
    [InlineData("46564546", "46564546")]                     // nom sans mot : pas de famille
    public void Types_differents(string a, string b) =>
        MaintenanceTemplateFamily.SameFamily(a, b).Should().BeFalse();

    private static async Task<TestGisDbContext> VehiculeAvecVidangeAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 43, Name = "Service 05", Plate = "GM-347-TR", CompanyId = 1, Mileage = 160_000 });
        ctx.MaintenanceTemplates.AddRange(
            new MaintenanceTemplate { Id = 8, Name = "Vidange + filtre à huile", Category = "Moteur", Priority = "medium", IntervalKm = 10_000, IntervalMonths = 6, CompanyId = 1 },
            new MaintenanceTemplate { Id = 10, Name = "Vidange Moteur", Category = "Moteur", Priority = "medium", IntervalKm = 10_000, CompanyId = 1 },
            new MaintenanceTemplate { Id = 7, Name = "Révision périodique", Category = "Moteur", Priority = "medium", IntervalKm = 20_000, CompanyId = 1 });
        ctx.VehicleMaintenanceSchedules.Add(new VehicleMaintenanceSchedule { Id = 40, VehicleId = 43, TemplateId = 8, CompanyId = 1, NextDueKm = 170_000, Status = "ok" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static Task<int> AffecterAsync(TestGisDbContext ctx, int templateId, bool allowSimilar = false) =>
        new AssignMaintenanceTemplateCommandHandler(ctx,
                new MaintenanceSchedulerService(ctx, NullLogger<MaintenanceSchedulerService>.Instance),
                TestDbContextFactory.CreateMockTenantService().Object)
            .Handle(new AssignMaintenanceTemplateCommand(43, templateId, allowSimilar), CancellationToken.None);

    [Fact]
    public async Task Une_seconde_vidange_est_refusee_avec_le_nom_de_la_premiere()
    {
        using var ctx = await VehiculeAvecVidangeAsync();

        var act = () => AffecterAsync(ctx, 10);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("Ce véhicule a déjà l'entretien « Vidange + filtre à huile », du même type que « Vidange Moteur »*");
        (await ctx.VehicleMaintenanceSchedules.CountAsync(s => s.VehicleId == 43)).Should().Be(1);
    }

    [Fact]
    public async Task Apres_confirmation_la_seconde_vidange_est_affectee()
    {
        using var ctx = await VehiculeAvecVidangeAsync();

        (await AffecterAsync(ctx, 10, allowSimilar: true)).Should().BeGreaterThan(0);
        (await ctx.VehicleMaintenanceSchedules.CountAsync(s => s.VehicleId == 43)).Should().Be(2);
    }

    [Fact]
    public async Task Un_entretien_d_un_autre_type_s_affecte_sans_question()
    {
        using var ctx = await VehiculeAvecVidangeAsync();

        (await AffecterAsync(ctx, 7)).Should().BeGreaterThan(0);
    }
}
