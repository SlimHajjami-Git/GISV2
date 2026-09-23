using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.DataPort;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Services;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Services;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Costs;

/// <summary>
/// Décision de Karim du 16/09/2026, étendue : un avoir fournisseur (<c>credit_note</c>) et
/// un remboursement d'assurance (<c>insurance_refund</c>) sont des CRÉDITS déduits des
/// coûts, PARTOUT.
///
/// <para>Constat : le tableau de bord GPA, les rapports de coûts et l'écran Dépenses
/// passaient déjà par <see cref="VehicleCostCategory"/>, mais cinq lecteurs additionnaient
/// encore <c>vehicle_costs.amount</c> en brut — résumé de l'écran Coûts, tendance, stats et
/// synthèse des coûts du tableau de bord GPS, contexte de l'assistant IA. Pour 600
/// d'assurance, 150 d'avoir et 100 de remboursement, ils annonçaient 850 au lieu de 350.
/// Et une ligne ancienne au type « avoir » (écrite avant la liste blanche) était comptée
/// en dépense, puis changeait de signe au premier aller-retour du classeur Excel.</para>
/// </summary>
public class CreditsDeduitsPartoutTests
{
    private const int CompanyId = 7;
    private const int AdminUserId = 1;

    // ─────────────────────────────── règle unique ───────────────────────────────

    [Theory]
    [InlineData("avoir")]
    [InlineData("Avoir")]
    [InlineData("Avoir fournisseur")]
    [InlineData("AVOIR  FOURNISSEUR")]
    [InlineData("credit note")]
    [InlineData("Crédit Note")]
    [InlineData("Remboursement assurance")]
    [InlineData("remb. assurance")]
    public void Un_type_ancien_synonyme_d_un_credit_est_deduit(string type)
    {
        VehicleCostCategory.Classify(type).Should().Be((CostCategory.Other, -1));
        VehicleCostCategory.SignedAmount(type, 150m).Should().Be(-150m);
    }

    [Theory]
    [InlineData("credit")]
    [InlineData("Crédit / Leasing")]
    [InlineData("insurance")]
    [InlineData("Assurance")]
    [InlineData("amende")]
    [InlineData("avoirs divers")]
    [InlineData("xyz")]
    [InlineData(null)]
    public void Une_depense_ordinaire_reste_une_depense(string? type)
    {
        VehicleCostCategory.SignedAmount(type, 150m).Should().Be(150m);
    }

    [Theory]
    [InlineData("avoir")]
    [InlineData("credit note")]
    [InlineData("remb. assurance")]
    [InlineData("credit_note")]
    [InlineData("insurance_refund")]
    [InlineData("amende")]
    public void L_aller_retour_export_import_ne_change_pas_le_signe(string type)
    {
        // L'export écrit le libellé, l'import relit le code : même signe avant et après.
        var (relu, _) = ExpenseImportRow.ParseType(ExpenseImportRow.TypeLabel(type));

        VehicleCostCategory.Classify(relu).Sign.Should().Be(VehicleCostCategory.Classify(type).Sign);
    }

    [Fact]
    public async Task Le_cout_d_exploitation_deduit_une_ligne_ancienne_au_type_avoir()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" });
        ctx.VehicleCosts.AddRange(
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance", Amount = 600m, Date = Aout(2) },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "Avoir", Amount = 150m, Date = Aout(20) });
        await ctx.SaveChangesAsync();

        var aggregate = await OperatingCostAggregator.LoadAsync(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            null, null, CancellationToken.None);

        // Depuis le 18/09/2026 le crédit ne diminue plus « Autres » : il a son
        // seau à lui. Seul le TOTAL est net, et il n'a pas bougé.
        var total = aggregate.Vehicles.Single().Total;
        (total.Other, total.Credit, total.Total).Should().Be((600m, -150m, 450m));
    }

    [Fact]
    public async Task Le_total_signe_est_calcule_en_base_par_type()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.VehicleCosts.AddRange(Depenses(DateTime.UtcNow));
        await ctx.SaveChangesAsync();

        (await VehicleCostCategory.SignedTotalAsync(ctx.VehicleCosts.AsNoTracking()))
            .Should().Be(400m, "600 + 50 − 150 − 100");
    }

    private static DateTime Aout(int day) => new(2026, 8, day, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>600 d'assurance + 50 de carburant, 150 d'avoir et 100 de remboursement : net 400.</summary>
    private static VehicleCost[] Depenses(DateTime date, int vehicleId = 1) =>
    [
        new() { VehicleId = vehicleId, CompanyId = CompanyId, Type = "insurance", Amount = 600m, Date = date },
        new() { VehicleId = vehicleId, CompanyId = CompanyId, Type = "fuel", Amount = 50m, Liters = 30m, Date = date },
        new() { VehicleId = vehicleId, CompanyId = CompanyId, Type = "credit_note", Amount = 150m, Date = date },
        new() { VehicleId = vehicleId, CompanyId = CompanyId, Type = "insurance_refund", Amount = 100m, Date = date },
    ];

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);

    private static ClaimsPrincipal Admin() => new(new ClaimsIdentity(new[]
    {
        new Claim("companyId", CompanyId.ToString()),
        new Claim(ClaimTypes.NameIdentifier, AdminUserId.ToString()),
        new Claim(ClaimTypes.Role, "company_admin")
    }, "test"));

    // ─────────────────────────────── GET /api/costs/summary ───────────────────────────────

    [Fact]
    public async Task Le_resume_de_l_ecran_Couts_deduit_avoir_et_remboursement()
    {
        using var ctx = TestDbContextFactory.Create();
        var jour = new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId });
        ctx.VehicleCosts.AddRange(Depenses(jour));
        await ctx.SaveChangesAsync();

        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns(CompanyId);
        tenant.Setup(x => x.UserId).Returns(AdminUserId);
        tenant.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        tenant.Setup(x => x.IsAuthenticated).Returns(true);
        var publisher = new Mock<IPublisher>();
        publisher.Setup(p => p.Publish(It.IsAny<AdminActionNotificationEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var controller = new CostsController(ctx, tenant.Object, publisher.Object,
            Mock.Of<IInvoiceExtractionService>(), Mock.Of<IWebHostEnvironment>(), Mock.Of<ILogger<CostsController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Admin() } }
        };

        var ok = (await controller.GetCostSummary(new DateTime(2026, 9, 1), new DateTime(2026, 9, 30))).Should().BeOfType<OkObjectResult>().Subject;
        var resume = Json(ok.Value);

        resume.GetProperty("TotalAmount").GetDecimal().Should().Be(400m);
        var parType = resume.GetProperty("ByType").EnumerateArray()
            .ToDictionary(t => t.GetProperty("Type").GetString()!, t => t.GetProperty("Total").GetDecimal());
        parType["credit_note"].Should().Be(-150m);
        parType["insurance_refund"].Should().Be(-100m);
        parType.Values.Sum().Should().Be(400m, "le détail par type retombe sur le total");
        resume.GetProperty("TotalFuelLiters").GetDecimal().Should().Be(30m);
    }

    // ─────────────────────────────── tableau de bord GPS ───────────────────────────────

    /// <summary>GisDbContext en mémoire, sans les colonnes propres à PostgreSQL (même montage que AvoirFournisseurTests).</summary>
    private sealed class ContexteEnMemoire : GisDbContext
    {
        public ContexteEnMemoire(DbContextOptions<GisDbContext> options)
            : base(options, TestDbContextFactory.CreateMockTenantService(CompanyId).Object) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToList())
                foreach (var property in entity.GetDeclaredProperties().ToList())
                    if (!Simple(property.ClrType) && !property.IsKey() && !property.IsForeignKey())
                        modelBuilder.Entity(entity.ClrType).Ignore(property.Name);
        }

        private static bool Simple(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
                || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(byte[])
                || t == typeof(DateOnly) || t == typeof(TimeOnly);
        }
    }

    private static async Task<GisDbContext> ParcEnMemoireAsync(params VehicleCost[] depenses)
    {
        var ctx = new ContexteEnMemoire(new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
            .Options);
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" });
        ctx.VehicleCosts.AddRange(depenses);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static DashboardController Tableau(GisDbContext ctx)
    {
        var sante = new Mock<IVehicleHealthScoreService>();
        sante.Setup(s => s.CalculateAllScoresAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<VehicleHealthResult>());

        return new DashboardController(ctx, Mock.Of<IMediator>(), new MemoryCache(new MemoryCacheOptions()),
            sante.Object, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Admin() } }
        };
    }

    [Fact]
    public async Task Les_stats_du_tableau_de_bord_deduisent_les_credits_du_mois()
    {
        var debutDuMois = DateTime.SpecifyKind(new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1), DateTimeKind.Utc);
        using var ctx = await ParcEnMemoireAsync(Depenses(debutDuMois));

        var ok = (await Tableau(ctx).GetDashboardStats()).Should().BeOfType<OkObjectResult>().Subject;

        var couts = Json(ok.Value).GetProperty("Costs");
        couts.GetProperty("ThisMonth").GetDecimal().Should().Be(400m);
        couts.GetProperty("FuelThisMonth").GetDecimal().Should().Be(50m);
    }

    /// <summary>
    /// Règle du 18/09/2026 : les quatre postes d'un tableau de bord n'ont pas la place
    /// d'une ligne de crédit, la déduction tombe donc sur « Réparations » — et « Autres »
    /// reste BRUT, comme dans les rapports détaillés. Le total, lui, ne bouge pas.
    /// </summary>
    [Fact]
    public async Task La_synthese_des_couts_du_tableau_de_bord_deduit_les_credits_des_Reparations()
    {
        var annee = DateTime.UtcNow.Year;
        using var ctx = await ParcEnMemoireAsync(Depenses(new DateTime(annee, 1, 2, 10, 0, 0, DateTimeKind.Utc)));

        var ok = (await Tableau(ctx).GetCostSummary("year")).Should().BeOfType<OkObjectResult>().Subject;

        var synthese = Json(ok.Value);
        synthese.GetProperty("FuelCost").GetDecimal().Should().Be(50m);
        synthese.GetProperty("OtherCost").GetDecimal().Should().Be(600m, "l'assurance reste brute");
        synthese.GetProperty("RepairCost").GetDecimal().Should().Be(-250m, "aucune réparation, 150 d'avoir et 100 de remboursement");
        synthese.GetProperty("TotalCost").GetDecimal().Should().Be(400m, "le total est le même qu'avant la règle");
    }

    [Fact]
    public async Task La_tendance_des_couts_du_tableau_de_bord_deduit_les_credits()
    {
        var annee = DateTime.UtcNow.Year;
        var courant = new DateTime(annee, 1, 2, 10, 0, 0, DateTimeKind.Utc);
        using var ctx = await ParcEnMemoireAsync(
        [
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance", Amount = 1000m, Date = new DateTime(annee - 1, 6, 1, 10, 0, 0, DateTimeKind.Utc) },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance", Amount = 1000m, Date = courant },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "credit_note", Amount = 300m, Date = courant },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance_refund", Amount = 200m, Date = courant },
        ]);

        var ok = (await Tableau(ctx).GetWidgetData("year")).Should().BeOfType<OkObjectResult>().Subject;

        Json(ok.Value).GetProperty("trends").GetProperty("expenses").GetDouble()
            .Should().Be(-50.0, "net 500 contre 1 000 l'an dernier ; en brut (1 500) la tendance montait de 50 %");
    }

    // ─────────────────────────────── assistant IA ───────────────────────────────

    private static AiChatController Assistant(TestGisDbContext ctx, Mock<ILlmService> llm)
    {
        var sante = new Mock<IVehicleHealthScoreService>();
        sante.Setup(s => s.CalculateAllScoresAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<VehicleHealthResult>());
        sante.Setup(s => s.CalculateScoreAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new VehicleHealthResult { VehicleId = 1, Score = 80, Level = "good" });

        // Crédit IA de la société (22/09/2026) : sans société connue, l'appel serait refusé.
        AiCreditTestData.EnsureSocieteAvecCredit(ctx, CompanyId);
        return new AiChatController(ctx, llm.Object, sante.Object, NullLogger<AiChatController>.Instance,
            TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Admin() } }
        };
    }

    private static async Task<TestGisDbContext> ParcAsync(params VehicleCost[] depenses)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" });
        ctx.VehicleCosts.AddRange(depenses);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    /// <summary>
    /// Règle du 18/09/2026 appliquée à l'assistant : postes BRUTS, ligne d'avoirs à part,
    /// total net — la convention des rapports, puisque c'est de rapports que le client parle.
    /// L'assistant rangeait le crédit DANS « Autres » sous un intitulé muet : 600 d'assurance
    /// et 250 d'avoirs s'annonçaient « Autres: 350 », un chiffre absent de tous les écrans.
    /// </summary>
    [Fact]
    public async Task Le_rapport_de_flotte_IA_transmet_des_postes_bruts_et_une_ligne_d_avoirs()
    {
        using var ctx = await ParcAsync(
        [
            .. Depenses(DateTime.UtcNow.AddDays(-2)),
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "repair", Amount = 200m, Date = DateTime.UtcNow.AddDays(-2) },
        ]);
        string? prompt = null;
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .Callback<string, List<LlmMessage>, int, CancellationToken>((system, _, _, _) => prompt = system)
           .ReturnsAsync(new LlmResponse("analyse", 1200));

        var ok = (await Assistant(ctx, llm).GenerateFleetReport(new FleetReportRequest("month", null)))
            .Should().BeOfType<OkObjectResult>().Subject;

        var rapport = Json(ok.Value);
        rapport.GetProperty("fleetSummary").GetProperty("totalCosts").GetDecimal().Should().Be(600m, "650 + 200 − 150 − 100");
        var repartition = rapport.GetProperty("charts").GetProperty("costBreakdown");
        repartition.GetProperty("fuel").GetDecimal().Should().Be(50m);
        repartition.GetProperty("repairs").GetDecimal().Should().Be(200m);
        repartition.GetProperty("other").GetDecimal().Should().Be(600m, "l'assurance reste brute, comme au rapport");
        repartition.GetProperty("credits").GetDecimal().Should().Be(250m, "150 d'avoir + 100 de remboursement, à part");
        rapport.GetProperty("vehicleDetails")[0].GetProperty("totalCosts").GetDecimal().Should().Be(600m);

        prompt.Should().Contain($"Coûts totaux nets: {600m:N0}")
            .And.Contain($"Autres: {600m:N0}")
            .And.Contain($"Avoirs et remboursements: {-250m:N0} déjà déduits du total")
            // L'intitulé net d'avant reste absent : c'est lui qui faisait mentir l'assistant.
            .And.NotContain($"Autres: {350m:N0}");
    }

    [Fact]
    public async Task La_repartition_du_rapport_IA_retombe_sur_le_total_quand_les_credits_depassent_les_autres_frais()
    {
        // Avoir de garage sur une période sans frais divers : « Autres » vaut 0 parce qu'il
        // n'y a rien dedans, et l'avoir entier passe dans « credits ». La légende doit
        // retomber sur le total — sans la ligne de crédit, 1 000 s'affichaient pour 700.
        var jour = DateTime.UtcNow.AddDays(-2);
        using var ctx = await ParcAsync(
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "repair", Amount = 1000m, Date = jour },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "credit_note", Amount = 300m, Date = jour });
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LlmResponse("analyse", 1200));

        var ok = (await Assistant(ctx, llm).GenerateFleetReport(new FleetReportRequest("month", null)))
            .Should().BeOfType<OkObjectResult>().Subject;

        var rapport = Json(ok.Value);
        var total = rapport.GetProperty("fleetSummary").GetProperty("totalCosts").GetDecimal();
        var r = rapport.GetProperty("charts").GetProperty("costBreakdown");
        decimal Part(string nom) => r.GetProperty(nom).GetDecimal();

        total.Should().Be(700m);
        (Part("repairs"), Part("other"), Part("credits")).Should().Be((1000m, 0m, 300m));
        (Part("fuel") + Part("maintenance") + Part("repairs") + Part("other") - Part("credits"))
            .Should().Be(total, "la légende retombe sur le total affiché");
    }

    [Fact]
    public async Task Le_diagnostic_IA_d_un_vehicule_affiche_les_credits_en_deduction()
    {
        var jour = DateTime.UtcNow.AddDays(-3);
        using var ctx = await ParcAsync(Depenses(jour));
        string? prompt = null;
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<CancellationToken>()))
           .Callback<string, List<LlmMessage>, CancellationToken>((system, _, _) => prompt = system)
           .ReturnsAsync(new LlmResponse("diagnostic", 900));

        (await Assistant(ctx, llm).GenerateReport(1)).Should().BeOfType<OkObjectResult>();

        prompt.Should().Contain($"Total des coûts listés (crédits déduits): {400m:N0}")
            .And.Contain($"| credit_note | N/A | {-150m:N0}")
            .And.Contain($"| insurance_refund | N/A | {-100m:N0}");
    }

    // ─────────────────────────────── scan de facture ───────────────────────────────

    private static async Task<(InvoiceExtraction Extraction, string Prompt)> ScannerAsync(string reponseIa)
    {
        string prompt = "";
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.ExtractJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .Callback<string, string, string?, int, CancellationToken>((system, _, _, _, _) => prompt = system)
           .ReturnsAsync(new LlmResponse(reponseIa, 900));
        var service = new InvoiceExtractionService(llm.Object, NullLogger<InvoiceExtractionService>.Instance);
        var resultat = await service.ExtractAsync(Encoding.UTF8.GetBytes("photo"), "image/jpeg", "avoir.jpg", CancellationToken.None);
        return (resultat.Extraction, prompt);
    }

    [Fact]
    public async Task Le_prompt_du_scan_demande_si_le_document_est_un_avoir()
    {
        var (_, prompt) = await ScannerAsync("""{"amountTTC":120,"category":"repair"}""");

        prompt.Should().Contain("\"isCreditNote\": boolean")
            .And.Contain("Facture d'avoir").And.Contain("Credit note")
            // Une seule règle : le titre du document. « mention explicite » laissait l'IA élargir.
            .And.NotContain("mention explicite");
    }

    [Fact]
    public async Task Un_avoir_aux_montants_imprimes_en_positif_devient_un_avoir_fournisseur()
    {
        var (x, _) = await ScannerAsync("""
        {"supplierName":"GARAGE EL AMEN","invoiceNumber":"AV-0043","amountHT":100.84,"amountTVA":19.16,"amountTTC":120,
         "currency":"TND","category":"repair","confidence":"high","isCreditNote":true,
         "items":[{"label":"Retour plaquettes","amount":120,"category":"repair"}]}
        """);

        x.IsCreditNote.Should().BeTrue();
        x.Category.Should().Be("credit_note", "en « repair » il partait en DÉPENSE et gonflait les coûts de 120");
        (x.AmountHT, x.AmountTVA, x.AmountTTC).Should().Be((100.84m, 19.16m, 120m));
        x.Items!.Single().Amount.Should().Be(120m, "des lignes imprimées en positif ne changent pas de signe");
    }

    [Fact]
    public async Task Un_avoir_aux_lignes_negatives_et_au_total_positif_est_rendu_positif()
    {
        var (x, _) = await ScannerAsync("""
        {"amountTTC":80,"category":"maintenance","isCreditNote":true,
         "items":[{"label":"Filtre repris","amount":-50},{"label":"Huile reprise","amount":-30}]}
        """);

        x.Category.Should().Be("credit_note");
        x.Items!.Select(i => i.Amount).Should().Equal(50m, 30m);
    }

    [Theory]
    [InlineData("""{"amountTTC":120,"category":"repair"}""", false)]
    [InlineData("""{"amountTTC":120,"category":"repair","isCreditNote":false}""", false)]
    [InlineData("""{"amountTTC":120,"category":"repair","isCreditNote":null}""", false)]
    [InlineData("""{"amountTTC":120,"category":"repair","isCreditNote":"peut-être"}""", false)]
    [InlineData("""{"amountTTC":120,"category":"repair","isCreditNote":"true"}""", true)]
    [InlineData("""{"amountTTC":120,"category":"repair","isCreditNote":"oui"}""", true)]
    public void Le_drapeau_avoir_est_lu_avec_tolerance_et_absent_vaut_facture(string json, bool avoir)
    {
        var lu = InvoiceExtractionService.Parse(json);

        lu.IsCreditNote.Should().Be(avoir);
        var converti = InvoiceExtractionService.AsCreditNoteIfDetected(lu);
        converti.Category.Should().Be(avoir ? "credit_note" : "repair");
        converti.AmountTTC.Should().Be(120m);
    }
}
