using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Features.DataPort;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyCostReport;
using GisAPI.Application.Services;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using DashboardService = global::GisAPI.Services.DashboardService;

namespace GisAPI.Tests.Application.Costs;

/// <summary>
/// Défauts repérés pendant la campagne GPA, hors fiches (liste M9, 17/09/2026) :
/// <list type="bullet">
///   <item>un type ancien « carburant » (écrit avant la liste blanche DEF-040) était rangé en
///     « Autres » par tous les rapports, alors que la table des synonymes de l'import en fait
///     du carburant ; de même « Réparation accident », libellé écrit par l'export ;</item>
///   <item>l'écran Coûts modifie par PUT /api/costs/{id}, qui remplace litres, carburant et
///     prix au litre par ce qu'il reçoit ; GET /api/costs ne les renvoyait pas, si bien que
///     corriger la description d'un plein effaçait ses litres et son prix au litre.</item>
/// </list>
/// </summary>
public class CoutsSynonymesEtPleinModifieTests
{
    private const int CompanyId = 7;
    private const int AdminUserId = 1;
    private static DateTime Sept(int day) => new(2026, 9, day, 9, 0, 0, DateTimeKind.Utc);

    // ─────────────────────────────── ventilation des synonymes ───────────────────────────────

    [Theory]
    [InlineData("carburant", CostCategory.Fuel)]
    [InlineData(" Carburant ", CostCategory.Fuel)]
    [InlineData("CARBURANT", CostCategory.Fuel)]
    [InlineData("Réparation accident", CostCategory.Repair)]
    [InlineData("reparation  accident", CostCategory.Repair)]
    // « é » décomposé (e + accent combinant) : même mot pour l'utilisateur. Écrit en séquence
    // d'échappement : un éditeur qui normalise en NFC en ferait un « é » précomposé, déjà classé.
    [InlineData("Re\u0301paration", CostCategory.Repair)]
    [InlineData("Entretien", CostCategory.Maintenance)]
    public void Un_type_ancien_synonyme_d_un_poste_est_range_dans_ce_poste(string type, CostCategory attendu)
    {
        VehicleCostCategory.Classify(type).Should().Be((attendu, 1));
    }

    [Theory]
    [InlineData("fuel")]
    [InlineData("maintenance")]
    [InlineData("reparation")]
    [InlineData("repair")]
    [InlineData("insurance")]
    [InlineData("insurance_refund")]
    [InlineData("credit_note")]
    [InlineData("tax")]
    [InlineData("amende")]
    [InlineData("credit")]
    [InlineData("autre")]
    public void Le_libelle_ecrit_par_l_export_se_ventile_comme_son_code(string code)
    {
        VehicleCostCategory.Classify(ExpenseImportRow.TypeLabel(code))
            .Should().Be(VehicleCostCategory.Classify(code));
    }

    [Theory]
    [InlineData("carburant diesel")]
    [InlineData("Carte grise")]
    [InlineData("Crédit / Leasing")]
    [InlineData("xyz")]
    [InlineData(null)]
    public void Un_type_hors_table_reste_en_autres(string? type)
    {
        VehicleCostCategory.Classify(type).Should().Be((CostCategory.Other, 1));
    }

    [Fact]
    public async Task Les_rapports_rangent_un_plein_carburant_en_carburant()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Logistique", Plate = "GH-619-XC", CompanyId = CompanyId });
        ctx.VehicleCosts.AddRange(
            new VehicleCost { Id = 1, CompanyId = CompanyId, VehicleId = 1, Type = "carburant", Amount = 80, Date = Sept(3) },
            new VehicleCost { Id = 2, CompanyId = CompanyId, VehicleId = 1, Type = "Réparation accident", Amount = 300, Date = Sept(4) },
            new VehicleCost { Id = 3, CompanyId = CompanyId, VehicleId = 1, Type = "insurance", Amount = 600, Date = Sept(5) });
        await ctx.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(CompanyId).Object;

        var (fuel, maintenance, repair, other) = await DashboardService.PeriodCostsAsync(
            ctx, CompanyId, null, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc), CancellationToken.None);
        var total = (await OperatingCostAggregator.LoadAsync(ctx, tenant,
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            null, null, CancellationToken.None)).Vehicles.Single().Total;
        var monthly = await new GetMonthlyCostReportQueryHandler(ctx, tenant)
            .Handle(new GetMonthlyCostReportQuery(2026, 9), CancellationToken.None);

        (fuel, maintenance, repair, other).Should().Be((80m, 0m, 300m, 600m));
        (total.Fuel, total.Maintenance, total.Repair, total.Other).Should().Be((fuel, maintenance, repair, other));
        (monthly.TotalFuelCostDzd, monthly.TotalMaintenanceCostDzd, monthly.TotalRepairCostDzd, monthly.TotalOtherCostDzd)
            .Should().Be((fuel, maintenance, repair, other));
    }

    // ─────────────────────────────── modification d'un plein ───────────────────────────────

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static CostsController Controller(TestGisDbContext ctx)
    {
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns(CompanyId);
        tenant.Setup(x => x.UserId).Returns(AdminUserId);
        tenant.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        tenant.Setup(x => x.IsAuthenticated).Returns(true);
        var publisher = new Mock<IPublisher>();
        publisher.Setup(p => p.Publish(It.IsAny<AdminActionNotificationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new CostsController(ctx, tenant.Object, publisher.Object,
            Mock.Of<IInvoiceExtractionService>(), Mock.Of<IWebHostEnvironment>(), Mock.Of<ILogger<CostsController>>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("companyId", CompanyId.ToString()),
                        new Claim(ClaimTypes.NameIdentifier, AdminUserId.ToString())
                    }, "test"))
                }
            }
        };
    }

    /// <summary>
    /// Plein créé par POST à 45,678 L × 2,205 = 100,72 : relu arrondi au centime (45,68 L,
    /// 2,21), dont le produit vaut 100,95 et non 100,72.
    /// </summary>
    private static async Task<TestGisDbContext> PleinAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 32, Name = "Utilitaire", Plate = "GA-214-RK", CompanyId = CompanyId });
        ctx.VehicleCosts.AddRange(
            new VehicleCost
            {
                Id = 1, CompanyId = CompanyId, VehicleId = 32, Type = "fuel", Description = "Plein station",
                Amount = 100.72m, Liters = 45.68m, PricePerLiter = 2.21m, FuelType = "diesel", Date = Sept(10)
            },
            new VehicleCost
            {
                Id = 2, CompanyId = CompanyId, VehicleId = 32, Type = "carburant", Description = "Plein ancien",
                Amount = 40m, Liters = 20m, Date = Sept(11)
            });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    /// <summary>Ligne de GET /api/costs telle que l'écran la reçoit (JSON camelCase), relue en corps de PUT.</summary>
    private static async Task<VehicleCost> LigneListeeAsync(TestGisDbContext ctx, int id)
    {
        var ok = (await Controller(ctx).GetCosts()).Should().BeOfType<OkObjectResult>().Subject;
        var ligne = JsonSerializer.SerializeToElement(ok.Value, Web).EnumerateArray()
            .Single(l => l.GetProperty("id").GetInt32() == id);
        return ligne.Deserialize<VehicleCost>(Web)!;
    }

    [Fact]
    public async Task La_liste_renvoie_le_detail_du_plein()
    {
        using var ctx = await PleinAsync();

        var ligne = await LigneListeeAsync(ctx, 1);

        (ligne.FuelType, ligne.Liters, ligne.PricePerLiter).Should().Be(("diesel", 45.68m, 2.21m));
    }

    [Fact]
    public async Task Corriger_la_description_d_un_plein_garde_litres_prix_au_litre_et_montant()
    {
        using var ctx = await PleinAsync();
        var corps = await LigneListeeAsync(ctx, 1);
        corps.Description = "Plein station Total";

        (await Controller(ctx).UpdateCost(1, corps)).Should().BeOfType<NoContentResult>();

        var plein = await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id == 1);
        plein.Description.Should().Be("Plein station Total");
        (plein.FuelType, plein.Liters, plein.PricePerLiter).Should().Be(("diesel", 45.68m, 2.21m));
        plein.Amount.Should().Be(100.72m, "rien de ce qui définit le montant n'a changé");
    }

    [Fact]
    public async Task Le_montant_saisi_prime_quand_litres_et_prix_au_litre_ne_changent_pas()
    {
        using var ctx = await PleinAsync();
        var corps = await LigneListeeAsync(ctx, 1);
        corps.Amount = 101m;

        (await Controller(ctx).UpdateCost(1, corps)).Should().BeOfType<NoContentResult>();

        (await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id == 1)).Amount.Should().Be(101m);
    }

    [Fact]
    public async Task Changer_les_litres_recalcule_toujours_le_montant()
    {
        using var ctx = await PleinAsync();
        var corps = await LigneListeeAsync(ctx, 1);
        corps.Liters = 50m;

        (await Controller(ctx).UpdateCost(1, corps)).Should().BeOfType<NoContentResult>();

        (await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id == 1)).Amount.Should().Be(110.5m);
    }

    [Fact]
    public async Task Le_resume_compte_les_litres_d_un_plein_ancien_carburant()
    {
        using var ctx = await PleinAsync();

        var ok = (await Controller(ctx).GetCostSummary(new DateTime(2026, 9, 1), new DateTime(2026, 9, 30)))
            .Should().BeOfType<OkObjectResult>().Subject;

        JsonSerializer.SerializeToElement(ok.Value).GetProperty("TotalFuelLiters").GetDecimal().Should().Be(65.68m);
    }
}
