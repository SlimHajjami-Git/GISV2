using FluentAssertions;
using GisAPI.Application.Features.Repairs;
using GisAPI.Application.Features.Repairs.Commands;
using GisAPI.Application.Features.Repairs.Handlers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Repairs;

/// <summary>
/// Garde-fous de la création et de la modification d'une réparation (recette du 16/09/2026).
///
///   • Le véhicule n'était jamais cherché DANS la société : un vehicleId étranger créait une
///     ligne rattachée à la société de l'appelant (comptée dans totalCount, absente de la liste)
///     et un vehicleId inexistant remontait en 500 par violation de clé étrangère.
///   • La référence REP-AAAAMM-NNNN était calculée comme « nombre de réparations + 1 » : après
///     une suppression, la création suivante réattribuait un numéro encore vivant et deux
///     factures portaient la même référence.
/// </summary>
public class RepairCreationGuardsTests
{
    private const int CompanyId = 7;
    private const int OtherCompanyId = 5;

    private static ICurrentTenantService Tenant()
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(1);
        m.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static TestGisDbContext Seed()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Opel", Plate = "111 TU 1", CompanyId = CompanyId, Mileage = 10_000 },
            new Vehicle { Id = 20, Name = "Etranger", Plate = "999 TU 9", CompanyId = OtherCompanyId, Mileage = 84_438 });
        ctx.SaveChanges();
        return ctx;
    }

    private static CreateRepairCommandHandler CreateHandler(TestGisDbContext ctx) =>
        new(ctx, Tenant(), Mock.Of<IPublisher>());

    private static CreateRepairCommand Creation(int vehicleId, string description = "QA réparation") =>
        new(vehicleId, null, description, new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc),
            0, 50m, null, null, new List<CreateRepairPartRequest>(), "autre");

    private static UpdateRepairCommand Modification(int repairId, int vehicleId) =>
        new(repairId, vehicleId, null, "QA modification", new DateTime(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc),
            0, 50m, "completed", null, null, new List<CreateRepairPartRequest>(), "autre");

    // ── DEF-019 : le véhicule doit appartenir à la société ─────────────────────

    [Fact]
    public async Task Creer_une_reparation_sur_le_vehicule_d_une_autre_societe_est_refuse()
    {
        using var ctx = Seed();

        var act = () => CreateHandler(ctx).Handle(Creation(20), CancellationToken.None);

        // 404 et message affichable tel quel par le formulaire Dépenses, comme POST /api/costs.
        (await act.Should().ThrowAsync<NotFoundException>()).And.Message.Should().Be("Véhicule introuvable.");
        (await ctx.Repairs.CountAsync()).Should().Be(0, "aucune ligne ne doit rester rattachée à la société de l'appelant");
        (await ctx.Vehicles.FirstAsync(v => v.Id == 20)).Mileage.Should().Be(84_438, "le véhicule étranger n'est pas touché");
    }

    [Fact]
    public async Task Creer_une_reparation_sur_un_vehicule_inexistant_est_refuse_avant_la_base()
    {
        using var ctx = Seed();

        var act = () => CreateHandler(ctx).Handle(Creation(999_999), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>("c'est un 404, pas une violation de clé étrangère en 500");
        (await ctx.Repairs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Modifier_une_reparation_ne_permet_pas_de_la_rattacher_a_un_vehicule_etranger()
    {
        using var ctx = Seed();
        var id = await CreateHandler(ctx).Handle(Creation(1), CancellationToken.None);

        var act = () => new UpdateRepairCommandHandler(ctx, Tenant())
            .Handle(Modification(id, 20), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        (await ctx.Repairs.AsNoTracking().FirstAsync(r => r.Id == id)).VehicleId
            .Should().Be(1, "la réparation reste sur son véhicule");
    }

    [Fact]
    public async Task Une_reparation_sur_un_vehicule_de_la_societe_passe_et_releve_le_kilometrage()
    {
        using var ctx = Seed();

        var id = await CreateHandler(ctx).Handle(
            Creation(1) with { MileageAtRepair = 12_500 }, CancellationToken.None);

        var repair = await ctx.Repairs.AsNoTracking().FirstAsync(r => r.Id == id);
        repair.VehicleId.Should().Be(1);
        repair.SocieteId.Should().Be(CompanyId);
        (await ctx.Vehicles.AsNoTracking().FirstAsync(v => v.Id == 1)).Mileage.Should().Be(12_500);
    }

    // ── Relecture du 22/09/2026 : « Annulée » est de nouveau proposée à l'écran ──

    [Fact]
    public async Task Passer_une_reparation_a_Annulee_ne_fait_pas_avancer_le_compteur()
    {
        using var ctx = Seed();
        var id = await CreateHandler(ctx).Handle(Creation(1), CancellationToken.None);

        // Même saisie qu'à l'écran : statut « Annulée » et kilométrage corrigé dans le
        // même enregistrement. L'import et OdometerReadings écartent déjà une annulée.
        var ok = await new UpdateRepairCommandHandler(ctx, Tenant()).Handle(
            Modification(id, 1) with { Status = "Cancelled", MileageAtRepair = 180_000 }, CancellationToken.None);

        ok.Should().BeTrue();
        (await ctx.Repairs.AsNoTracking().FirstAsync(r => r.Id == id)).Status.Should().Be("cancelled");
        (await ctx.Vehicles.AsNoTracking().FirstAsync(v => v.Id == 1)).Mileage
            .Should().Be(10_000, "une réparation annulée ne décrit aucun passage à l'atelier");
    }

    [Fact]
    public async Task Modifier_une_reparation_terminee_fait_toujours_avancer_le_compteur()
    {
        using var ctx = Seed();
        var id = await CreateHandler(ctx).Handle(Creation(1), CancellationToken.None);

        await new UpdateRepairCommandHandler(ctx, Tenant()).Handle(
            Modification(id, 1) with { MileageAtRepair = 12_000 }, CancellationToken.None);

        (await ctx.Vehicles.AsNoTracking().FirstAsync(v => v.Id == 1)).Mileage.Should().Be(12_000);
    }

    // ── DEF-020 : la référence ne se déduit pas d'un comptage ──────────────────

    [Fact]
    public async Task Une_reference_supprimee_n_est_jamais_reattribuee()
    {
        using var ctx = Seed();
        var handler = CreateHandler(ctx);

        var a = await handler.Handle(Creation(1, "A"), CancellationToken.None);
        var b = await handler.Handle(Creation(1, "B"), CancellationToken.None);
        (await new DeleteRepairCommandHandler(ctx, Tenant()).Handle(new DeleteRepairCommand(a), CancellationToken.None))
            .Should().BeTrue();
        var c = await handler.Handle(Creation(1, "C"), CancellationToken.None);

        var references = await ctx.Repairs.AsNoTracking()
            .Where(r => r.Id == b || r.Id == c)
            .Select(r => r.Reference)
            .ToListAsync();

        references.Should().OnlyHaveUniqueItems("deux factures ne peuvent pas porter la même référence");
    }

    [Fact]
    public async Task La_reference_reprend_le_plus_grand_numero_du_mois()
    {
        using var ctx = Seed();
        var mois = DateTime.UtcNow.ToString("yyyyMM");
        // Numéro déjà sorti, bien au-delà du nombre de lignes : le comptage l'aurait ignoré.
        ctx.Repairs.Add(new Repair
        {
            Id = 900, SocieteId = CompanyId, VehicleId = 1, Reference = $"REP-{mois}-0042",
            RepairDate = DateTime.UtcNow, Status = "completed"
        });
        await ctx.SaveChangesAsync();

        var id = await CreateHandler(ctx).Handle(Creation(1), CancellationToken.None);

        (await ctx.Repairs.AsNoTracking().FirstAsync(r => r.Id == id)).Reference
            .Should().Be($"REP-{mois}-0043");
    }

    [Fact]
    public async Task La_reference_ignore_les_numeros_d_une_autre_societe()
    {
        using var ctx = Seed();
        var mois = DateTime.UtcNow.ToString("yyyyMM");
        ctx.Repairs.Add(new Repair
        {
            Id = 901, SocieteId = OtherCompanyId, VehicleId = 20, Reference = $"REP-{mois}-0500",
            RepairDate = DateTime.UtcNow, Status = "completed"
        });
        await ctx.SaveChangesAsync();

        var id = await CreateHandler(ctx).Handle(Creation(1), CancellationToken.None);

        (await ctx.Repairs.AsNoTracking().FirstAsync(r => r.Id == id)).Reference
            .Should().Be($"REP-{mois}-0001", "chaque société a sa propre suite");
    }
}
