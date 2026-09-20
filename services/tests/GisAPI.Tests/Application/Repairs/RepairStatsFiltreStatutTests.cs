using FluentAssertions;
using GisAPI.Application.Features.Repairs.Handlers;
using GisAPI.Application.Features.Repairs.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Repairs;

/// <summary>
/// Filtre STATUT des compteurs de l'écran Réparations (GET /api/repairs/stats), 20/09/2026.
///
/// L'écran s'arrêtait à la première page de 100 lignes et calculait ses quatre compteurs
/// dessus : une société de 250 réparations lisait « 100 Total réparations ». Les compteurs
/// se demandent désormais au serveur, qui compte toute la société — mais /stats ne
/// connaissait que vehicleId, fromDate et toDate. Sans le statut, filtrer « Terminée »
/// à l'écran donnait des compteurs portant sur tous les statuts.
///
/// Règle de référence conservée : une réparation annulée COMPTE dans le nombre et reste
/// hors des montants.
/// </summary>
public class RepairStatsFiltreStatutTests
{
    private const int CompanyId = 7;
    private static readonly DateTime Day = new(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);

    private static ICurrentTenantService Admin()
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(1);
        m.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static Repair Repair(int id, string status, decimal labor, decimal parts, int vehicleId = 1) => new()
    {
        Id = id, SocieteId = CompanyId, VehicleId = vehicleId, Reference = $"REP-{id}", RepairDate = Day,
        LaborCost = labor, PartsCost = parts, TotalCost = labor + parts, Status = status
    };

    /// <summary>Deux véhicules, les quatre statuts, plus une casse ancienne « Cancelled ».</summary>
    private static async Task<TestGisDbContext> ParcAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Commercial 01", Plate = "111 TU 1", CompanyId = CompanyId });
        ctx.Vehicles.Add(new Vehicle { Id = 2, Name = "Logistique 01", Plate = "222 TU 2", CompanyId = CompanyId });
        ctx.Repairs.AddRange(
            Repair(1, "completed", 40m, 60m),
            Repair(2, "completed", 100m, 200m, vehicleId: 2),
            Repair(3, "pending", 50m, 0m),
            Repair(4, "in_progress", 70m, 30m, vehicleId: 2),
            Repair(5, "cancelled", 200m, 300m),
            Repair(6, "Cancelled", 1_000m, 0m, vehicleId: 2));
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static Task<GisAPI.Application.Features.Repairs.RepairStatsDto> Stats(
        TestGisDbContext ctx, string? status = null, int? vehicleId = null) =>
        new GetRepairStatsQueryHandler(ctx, Admin())
            .Handle(new GetRepairStatsQuery(VehicleId: vehicleId, Status: status), CancellationToken.None);

    [Fact]
    public async Task Sans_statut_les_compteurs_portent_sur_toute_la_societe()
    {
        using var ctx = await ParcAsync();

        var stats = await Stats(ctx);

        stats.TotalRepairs.Should().Be(6);
        stats.PendingRepairs.Should().Be(2);   // en attente + en cours
        stats.CompletedRepairs.Should().Be(2);
        stats.CancelledRepairs.Should().Be(2);
        stats.TotalCost.Should().Be(550m);     // 100 + 300 + 50 + 100, les annulées exclues
    }

    [Fact]
    public async Task Filtre_terminee_ne_compte_que_les_terminees()
    {
        using var ctx = await ParcAsync();

        var stats = await Stats(ctx, "completed");

        stats.TotalRepairs.Should().Be(2);
        stats.CompletedRepairs.Should().Be(2);
        stats.PendingRepairs.Should().Be(0);
        stats.CancelledRepairs.Should().Be(0);
        stats.TotalCost.Should().Be(400m);
        stats.AverageCost.Should().Be(200m);
        stats.TotalLaborCost.Should().Be(140m);
        stats.TotalPartsCost.Should().Be(260m);
    }

    [Fact]
    public async Task Filtre_en_attente_et_en_cours_restent_deux_statuts_distincts()
    {
        using var ctx = await ParcAsync();

        var attente = await Stats(ctx, "pending");
        attente.TotalRepairs.Should().Be(1);
        attente.PendingRepairs.Should().Be(1);
        attente.TotalCost.Should().Be(50m);

        var enCours = await Stats(ctx, "in_progress");
        enCours.TotalRepairs.Should().Be(1);
        enCours.PendingRepairs.Should().Be(1, "le compteur « En attente » de l'écran regroupe les deux");
        enCours.TotalCost.Should().Be(100m);
    }

    [Fact]
    public async Task Filtre_annulee_les_compte_et_laisse_le_cout_a_zero()
    {
        using var ctx = await ParcAsync();

        var stats = await Stats(ctx, "cancelled");

        // La casse ancienne « Cancelled » est reprise : sinon le tableau montrait deux
        // lignes annulées et le compteur en annonçait une.
        stats.TotalRepairs.Should().Be(2);
        stats.CancelledRepairs.Should().Be(2);
        stats.TotalCost.Should().Be(0m, "une réparation annulée ne coûte rien");
        stats.AverageCost.Should().Be(0m);
        stats.TotalLaborCost.Should().Be(0m);
        stats.TotalPartsCost.Should().Be(0m);
    }

    [Fact]
    public async Task Statut_vide_ou_blanc_ne_filtre_rien()
    {
        using var ctx = await ParcAsync();

        (await Stats(ctx, "")).TotalRepairs.Should().Be(6);
        (await Stats(ctx, "   ")).TotalRepairs.Should().Be(6);
    }

    [Fact]
    public async Task Statut_inconnu_ne_rend_aucune_ligne_plutot_que_tout_le_parc()
    {
        using var ctx = await ParcAsync();

        var stats = await Stats(ctx, "bidule");

        stats.TotalRepairs.Should().Be(0);
        stats.TotalCost.Should().Be(0m);
    }

    [Fact]
    public async Task Statut_et_vehicule_se_combinent()
    {
        using var ctx = await ParcAsync();

        var stats = await Stats(ctx, "completed", vehicleId: 2);

        stats.TotalRepairs.Should().Be(1);
        stats.CompletedRepairs.Should().Be(1);
        stats.TotalCost.Should().Be(300m);
    }

    [Fact]
    public async Task Les_compteurs_filtres_se_recoupent_toujours()
    {
        using var ctx = await ParcAsync();

        foreach (var statut in new string?[] { null, "pending", "in_progress", "completed", "cancelled" })
        {
            var stats = await Stats(ctx, statut);
            (stats.PendingRepairs + stats.CompletedRepairs + stats.CancelledRepairs)
                .Should().Be(stats.TotalRepairs, $"compteurs du filtre « {statut ?? "tous"} »");
        }
    }
}
