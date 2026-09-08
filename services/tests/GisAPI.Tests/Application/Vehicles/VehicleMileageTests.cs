using FluentAssertions;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Application.Features.Vehicles.Commands.CorrectMileage;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// Kilométrage d'un véhicule sans boîtier : le relevé saisi doit remonter dans la
/// fiche (recette du 08/09/2026 — « quand je mets le nouveau kilométrage lors du
/// remplissage de donnée, il ne se met pas à jour dans Véhicules »), et une valeur
/// aberrante doit pouvoir être corrigée, ce qu'aucun écran ne permettait.
/// </summary>
public class VehicleMileageTests
{
    private static Vehicle V(int mileage) => new() { Id = 1, CompanyId = 1, Plate = "524 TDF 75", Mileage = mileage };

    [Fact]
    public void Un_releve_superieur_fait_avancer_le_compteur()
    {
        var v = V(46_800);

        VehicleMileage.Advance(v, 47_455).Should().BeTrue();

        v.Mileage.Should().Be(47_455);
    }

    [Theory]
    [InlineData(46_800)]  // égal : rien à faire
    [InlineData(12_000)]  // ticket ancien saisi après coup
    public void Un_releve_inferieur_ou_egal_laisse_le_compteur_en_place(int reading)
    {
        var v = V(46_800);

        VehicleMileage.Advance(v, reading).Should().BeFalse();

        v.Mileage.Should().Be(46_800);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Un_releve_absent_ou_nul_ne_touche_a_rien(int? reading)
    {
        var v = V(46_800);

        VehicleMileage.Advance(v, reading).Should().BeFalse();

        v.Mileage.Should().Be(46_800);
    }

    [Fact]
    public void Sans_vehicule_la_propagation_ne_leve_pas()
    {
        VehicleMileage.Advance((Vehicle?)null, 50_000).Should().BeFalse();
    }

    [Fact]
    public void Le_releve_long_des_pleins_est_accepte()
    {
        var v = V(46_800);

        VehicleMileage.Advance(v, 47_455L).Should().BeTrue();

        v.Mileage.Should().Be(47_455);
    }

    // ── Correction assumée ────────────────────────────────────────────────────

    private static async Task<TestGisDbContext> SeededAsync(int mileage)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(V(mileage));
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static CorrectVehicleMileageCommandHandler Handler(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(1).Object);

    [Fact]
    public async Task La_correction_fait_redescendre_le_compteur_et_laisse_une_trace()
    {
        using var ctx = await SeededAsync(145_000);

        var result = await Handler(ctx).Handle(
            new CorrectVehicleMileageCommand(1, 46_800, "faute de frappe à l'import du 03/09"),
            CancellationToken.None);

        result.PreviousMileage.Should().Be(145_000);
        result.Mileage.Should().Be(46_800);
        ctx.Vehicles.Single().Mileage.Should().Be(46_800);

        var log = ctx.AuditLogs.Single();
        log.Action.Should().Be("vehicle_mileage_corrected");
        log.EntityId.Should().Be(1);
        log.OldValues!["mileage"].Should().Be(145_000);
        log.NewValues!["mileage"].Should().Be(46_800);
        // Le séparateur de milliers dépend de la culture (espace insécable en fr-FR) :
        // on compare sur les chiffres, pas sur la mise en forme.
        var description = new string(log.Description!.Where(c => !char.IsWhiteSpace(c)).ToArray());
        description.Should().Contain("145000").And.Contain("46800").And.Contain("importdu03/09");
    }

    [Fact]
    public async Task Une_correction_a_la_hausse_est_permise_aussi()
    {
        using var ctx = await SeededAsync(10_000);

        await Handler(ctx).Handle(
            new CorrectVehicleMileageCommand(1, 52_220, "relevé constaté sur le tableau de bord"),
            CancellationToken.None);

        ctx.Vehicles.Single().Mileage.Should().Be(52_220);
    }

    [Fact]
    public async Task Une_valeur_identique_ne_journalise_rien()
    {
        using var ctx = await SeededAsync(46_800);

        var result = await Handler(ctx).Handle(
            new CorrectVehicleMileageCommand(1, 46_800, "vérification"), CancellationToken.None);

        result.Mileage.Should().Be(46_800);
        ctx.AuditLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Le_vehicule_d_une_autre_societe_est_introuvable()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 9, CompanyId = 2, Plate = "AUTRE", Mileage = 1000 });
        await ctx.SaveChangesAsync();

        var act = () => Handler(ctx).Handle(
            new CorrectVehicleMileageCommand(9, 500, "tentative"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData(-1, "motif")]          // kilométrage négatif
    [InlineData(50_000, "")]           // motif vide
    [InlineData(50_000, "   ")]        // motif blanc
    public void Le_validateur_refuse_les_saisies_incompletes(int mileage, string reason)
    {
        var validator = new CorrectVehicleMileageCommandValidator();

        validator.Validate(new CorrectVehicleMileageCommand(1, mileage, reason))
            .IsValid.Should().BeFalse();
    }
}
