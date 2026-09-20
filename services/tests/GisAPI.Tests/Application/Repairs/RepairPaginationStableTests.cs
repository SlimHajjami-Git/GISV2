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
/// Pagination de GET /api/repairs, 20/09/2026.
///
/// L'écran Réparations enchaîne désormais les pages pour charger tout le parc, afin que
/// ses compteurs et sa recherche portent sur la société entière et non sur les 100
/// premières lignes. Cela change la nature du tri : tant qu'on ne demandait qu'UNE page,
/// l'ordre entre deux réparations de même date était sans conséquence ; avec Skip/Take,
/// un ordre instable fait revenir une ligne sur deux pages et en escamote une autre —
/// l'écran se croit complet et affiche un coût total faux, sans le moindre message.
/// PostgreSQL ne garantit rien entre lignes de même repair_date : le départage est
/// explicite.
///
/// On vérifie aussi les bornes de pageSize : la route l'accepte tel quel, et une valeur
/// démesurée ferait matérialiser tout le parc avec ses véhicules et ses pièces.
/// </summary>
public class RepairPaginationStableTests
{
    private const int CompanyId = 7;
    private static readonly DateTime MemeJour = new(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);

    private static ICurrentTenantService Admin()
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(1);
        m.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    /// <summary>Dix réparations à la MÊME date : le pire cas pour un tri sans départage.</summary>
    private static async Task<TestGisDbContext> ParcMemeDateAsync(int combien = 10)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Commercial 01", Plate = "111 TU 1", CompanyId = CompanyId });
        for (var i = 1; i <= combien; i++)
            ctx.Repairs.Add(new Repair
            {
                Id = i, SocieteId = CompanyId, VehicleId = 1, Reference = $"REP-{i:D4}",
                RepairDate = MemeJour, LaborCost = 10m * i, PartsCost = 0m, TotalCost = 10m * i,
                Status = "completed"
            });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static Task<RepairsListResult> Page(
        TestGisDbContext ctx, int page, int pageSize) =>
        new GetRepairsQueryHandler(ctx, Admin())
            .Handle(new GetRepairsQuery(Page: page, PageSize: pageSize), CancellationToken.None);

    [Fact]
    public async Task Enchainer_les_pages_rend_chaque_reparation_une_fois_et_une_seule()
    {
        using var ctx = await ParcMemeDateAsync(10);

        var vus = new List<int>();
        for (var p = 1; p <= 5; p++)
            vus.AddRange((await Page(ctx, p, 2)).Items.Select(i => i.Id));

        vus.Should().HaveCount(10, "cinq pages de deux couvrent le parc");
        vus.Should().OnlyHaveUniqueItems("une ligne qui revient sur deux pages gonfle le coût total");
        vus.Should().BeEquivalentTo(Enumerable.Range(1, 10), "aucune réparation ne doit disparaître");
    }

    [Fact]
    public async Task Le_cout_total_reconstitue_page_par_page_egale_le_cout_reel()
    {
        using var ctx = await ParcMemeDateAsync(10);
        var attendu = Enumerable.Range(1, 10).Sum(i => 10m * i);   // 550

        var cumul = 0m;
        for (var p = 1; p <= 5; p++)
            cumul += (await Page(ctx, p, 2)).Items.Sum(i => i.TotalCost);

        cumul.Should().Be(attendu, "c'est le chiffre que l'écran affiche en « Coût total »");
    }

    [Fact]
    public async Task Les_pages_se_suivent_dans_un_ordre_stable()
    {
        using var ctx = await ParcMemeDateAsync(10);

        var premiere = (await Page(ctx, 1, 4)).Items.Select(i => i.Id).ToList();
        var encore = (await Page(ctx, 1, 4)).Items.Select(i => i.Id).ToList();

        encore.Should().Equal(premiere, "deux appels identiques doivent rendre la même page");
        premiere.Should().Equal(new[] { 10, 9, 8, 7 }, "à date égale, l'identifiant décroissant départage");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Une_taille_de_page_absurde_ne_vide_pas_la_liste(int taille)
    {
        using var ctx = await ParcMemeDateAsync(3);

        var res = await Page(ctx, 1, taille);

        res.Items.Should().HaveCount(1, "la taille est ramenée à une ligne, jamais à zéro ni à un Take négatif");
        res.TotalCount.Should().Be(3, "le total du parc reste juste");
    }

    [Fact]
    public async Task Une_taille_de_page_demesuree_est_plafonnee()
    {
        using var ctx = await ParcMemeDateAsync(3);

        var res = await Page(ctx, 1, 10_000_000);

        res.PageSize.Should().Be(GetRepairsQueryHandler.TaillePageMax,
            "la réponse annonce la taille RÉELLEMENT appliquée, sinon l'appelant croit avoir tout reçu");
        res.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task Une_page_negative_est_ramenee_a_la_premiere()
    {
        using var ctx = await ParcMemeDateAsync(3);

        var res = await Page(ctx, -2, 2);

        res.Page.Should().Be(1);
        res.Items.Select(i => i.Id).Should().Equal(new[] { 3, 2 });
    }
}
