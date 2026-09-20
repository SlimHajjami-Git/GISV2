using System.Security.Claims;
using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.DataPort;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyFleetReport;
using GisAPI.Application.Services;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Costs;

/// <summary>
/// Catégorie « Avoir fournisseur » (décision de Karim du 16/09/2026, suite de DEF-050).
///
/// <para>Constat : depuis DEF-050, POST/PUT /api/costs refusent un montant ≤ 0 et une
/// catégorie hors liste. Un avoir fournisseur — facture négative lue par le scan IA —
/// ne pouvait plus être enregistré (« Montant supérieur à zéro requis. »). Règle
/// retenue : même convention que le remboursement d'assurance, type <c>credit_note</c>
/// à montant POSITIF, compté en crédit par <see cref="VehicleCostCategory"/>.</para>
/// </summary>
public class AvoirFournisseurTests
{
    private const int CompanyId = 7;
    private const int AdminUserId = 1;
    private static readonly DateTime Jour = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);
    private static DateTime Aout(int day) => new(2026, 8, day, 10, 0, 0, DateTimeKind.Utc);

    // ─────────────────────────────── ventilation ───────────────────────────────

    [Theory]
    [InlineData("credit_note")]
    [InlineData(" CREDIT_NOTE ")]
    public void L_avoir_est_un_credit_range_en_Autres(string type)
    {
        VehicleCostCategory.Classify(type).Should().Be((CostCategory.Other, -1));
        VehicleCostCategory.SignedAmount(type, 120m).Should().Be(-120m);
    }

    [Fact]
    public void Le_credit_leasing_et_les_depenses_ordinaires_ne_sont_pas_des_credits()
    {
        VehicleCostCategory.SignedAmount("credit", 100m).Should().Be(100m, "« credit » est le Crédit / Leasing de l'import Excel");
        VehicleCostCategory.SignedAmount("insurance", 100m).Should().Be(100m);
        VehicleCostCategory.SignedAmount("insurance_refund", 100m).Should().Be(-100m);
    }

    // ─────────────────────────────── POST / PUT /api/costs ───────────────────────────────

    private static CostsController CostsControleur(TestGisDbContext ctx)
    {
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns(CompanyId);
        tenant.Setup(x => x.UserId).Returns(AdminUserId);
        tenant.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        tenant.Setup(x => x.IsAuthenticated).Returns(true);

        var publisher = new Mock<IPublisher>();
        publisher
            .Setup(p => p.Publish(It.IsAny<AdminActionNotificationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new CostsController(
            ctx, tenant.Object, publisher.Object,
            new Mock<IInvoiceExtractionService>().Object,
            new Mock<IWebHostEnvironment>().Object,
            new Mock<ILogger<CostsController>>().Object)
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

    private static async Task<TestGisDbContext> ParcAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 32, Name = "Utilitaire 02", Plate = "GA-214-RK", CompanyId = CompanyId });
        ctx.VehicleCosts.Add(new VehicleCost { Id = 1, CompanyId = CompanyId, VehicleId = 32, Type = "autre", Amount = 40m, Date = Jour });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    [Theory]
    [InlineData("credit_note")]
    [InlineData("avoir")]
    [InlineData("Avoir fournisseur")]
    [InlineData("AVOIR FOURNISSEUR")]
    [InlineData("credit note")]
    [InlineData("Crédit Note")]
    public async Task Creation_d_un_avoir_acceptee_et_enregistree_sous_credit_note(string type)
    {
        using var ctx = await ParcAsync();

        var created = await CostsControleur(ctx).CreateCost(new VehicleCost
        {
            VehicleId = 32, Type = type, Description = "Avoir retour pneus", Amount = 120m, Date = Jour
        });

        created.Result.Should().BeOfType<CreatedAtActionResult>();
        var ligne = await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id != 1);
        ligne.Type.Should().Be("credit_note");
        ligne.Amount.Should().Be(120m, "un avoir est stocké en positif, le signe vient de la catégorie");
    }

    [Theory]
    [InlineData(-120)]
    [InlineData(0)]
    public async Task Un_avoir_negatif_ou_nul_reste_refuse(int amount)
    {
        using var ctx = await ParcAsync();

        var created = await CostsControleur(ctx).CreateCost(new VehicleCost
        {
            VehicleId = 32, Type = "credit_note", Amount = amount, Date = Jour
        });

        var refus = created.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        System.Text.Json.JsonSerializer.SerializeToElement(refus.Value).GetProperty("message").GetString()
            .Should().StartWith("Le montant doit être supérieur à zéro.").And.Contain("« Avoir fournisseur »");
        (await ctx.VehicleCosts.AsNoTracking().CountAsync()).Should().Be(1, "rien ne doit être enregistré");
    }

    [Fact]
    public async Task Une_depense_peut_etre_requalifiee_en_avoir()
    {
        using var ctx = await ParcAsync();

        (await CostsControleur(ctx).UpdateCost(1, new VehicleCost
        {
            Type = "Avoir", Description = "Avoir garage", Amount = 40m, Date = Jour
        })).Should().BeOfType<NoContentResult>();

        ctx.ChangeTracker.Clear();
        (await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id == 1)).Type.Should().Be("credit_note");
    }

    // ─────────────────────────────── rapports ───────────────────────────────

    [Fact]
    public async Task Le_rapport_mensuel_flotte_deduit_l_avoir_sous_son_libelle()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" });
        ctx.VehicleCosts.AddRange(
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance", Amount = 600, Date = Aout(2) },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "credit_note", Amount = 150, Date = Aout(20) });
        await ctx.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(CompanyId).Object;
        var rapport = await new GetMonthlyFleetReportQueryHandler(ctx, tenant)
            .Handle(new GetMonthlyFleetReportQuery(2026, 8), CancellationToken.None);

        var aggregate = await OperatingCostAggregator.LoadAsync(ctx, tenant,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            null, null, CancellationToken.None);

        // « Autres » reste BRUT depuis le 18/09/2026, l'avoir a son seau ; le total est net.
        var seaux = aggregate.Vehicles.Single().Total;
        (seaux.Other, seaux.Credit, seaux.Total).Should().Be((600m, -150m, 450m));
        var categories = rapport.CostAnalysis.ByCategory;
        categories.Single(c => c.Category == "Avoir fournisseur").Amount.Should().Be(-150m);
        categories.Sum(c => c.Amount).Should().Be(rapport.CostAnalysis.TotalOperationalCost);
        rapport.CostAnalysis.TotalOperationalCost.Should().Be(450m);
    }

    // ─────────────────────────────── port Excel ───────────────────────────────

    /// <summary>GisDbContext en mémoire, sans les colonnes propres à PostgreSQL (même montage que DataPortExportImportTests).</summary>
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

    private static GisDbContext ContexteExcel()
    {
        var ctx = new ContexteEnMemoire(new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
            .Options);
        ctx.Vehicles.Add(new Vehicle { Id = 38, Name = "Terrain 01", Plate = "GG-852-BD", CompanyId = CompanyId, Status = "available" });
        return ctx;
    }

    private static DataPortController PortControleur(GisDbContext ctx) =>
        new(ctx, NullLogger<DataPortController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("companyId", CompanyId.ToString()) }))
                }
            }
        };

    private static async Task<DataPortController.ImportSummary> ImporterAsync(GisDbContext ctx, byte[] classeur)
    {
        var file = new FormFile(new MemoryStream(classeur), 0, classeur.Length, "file", "calypso-donnees.xlsx");
        var ok = (await PortControleur(ctx).Import(file)).Should().BeOfType<OkObjectResult>().Subject;
        return ok.Value.Should().BeOfType<DataPortController.ImportSummary>().Subject;
    }

    [Theory]
    [InlineData("Avoir fournisseur")]
    [InlineData("avoir")]
    [InlineData("Credit note")]
    [InlineData("credit_note")]
    public void L_import_relit_l_avoir_sous_son_code(string cellule)
    {
        ExpenseImportRow.TypeLabel("credit_note").Should().Be("Avoir fournisseur");
        ExpenseImportRow.ParseType(cellule).Should().Be(("credit_note", (string?)null));
        ExpenseImportRow.ParseType("Crédit / Leasing").Type.Should().Be("credit", "le leasing ne devient pas un avoir");
    }

    [Fact]
    public async Task Export_puis_import_d_un_avoir_aller_retour_sans_doublon()
    {
        byte[] classeur;
        using (var source = ContexteExcel())
        {
            source.VehicleCosts.Add(new VehicleCost
            {
                Id = 1, VehicleId = 38, CompanyId = CompanyId, Type = "credit_note", Amount = 150m,
                Description = "Avoir retour pneus", Date = Jour, ReceiptNumber = "AV-2026-12"
            });
            await source.SaveChangesAsync();
            classeur = ((await PortControleur(source).Export()).Should().BeOfType<FileContentResult>().Subject).FileContents;
        }

        using (var wb = new XLWorkbook(new MemoryStream(classeur)))
        {
            var ligne = wb.Worksheet("Dépenses").Row(2);
            ligne.Cell(3).GetString().Should().Be("Avoir fournisseur");
            ligne.Cell(5).GetString().Should().Be("150", "l'avoir est exporté en positif, comme il est stocké");
        }

        using var cible = ContexteExcel();
        await cible.SaveChangesAsync();

        var bilan = await ImporterAsync(cible, classeur);
        bilan.ExpensesCreated.Should().Be(1);
        bilan.Notes.Should().NotContain(n => n.Contains("non reconnu"));

        cible.ChangeTracker.Clear();
        var avoir = await cible.VehicleCosts.AsNoTracking().SingleAsync();
        (avoir.Type, avoir.Amount, avoir.ReceiptNumber).Should().Be(("credit_note", 150m, "AV-2026-12"));

        (await ImporterAsync(cible, classeur)).ExpensesCreated.Should().Be(0, "réimporter le classeur ne recrée pas l'avoir");
    }

    // ─────────────────────────────── scan de facture IA ───────────────────────────────

    private static async Task<(InvoiceExtraction Extraction, Mock<ILlmService> Llm)> ScannerAsync(string reponseIa)
    {
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.ExtractJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LlmResponse(reponseIa, 900));
        var service = new InvoiceExtractionService(llm.Object, NullLogger<InvoiceExtractionService>.Instance);
        var resultat = await service.ExtractAsync(Encoding.UTF8.GetBytes("photo"), "image/jpeg", "avoir.jpg", CancellationToken.None);
        return (resultat.Extraction, llm);
    }

    [Fact]
    public async Task Un_scan_au_total_negatif_devient_un_avoir_en_valeur_absolue()
    {
        var (x, _) = await ScannerAsync("""
        {"supplierName":"GARAGE EL AMEN","invoiceNumber":"AV-0042","date":"2026-09-10",
         "amountHT":-100.84,"amountTVA":-19.16,"amountTTC":"-120,000","currency":"TND","category":"repair",
         "confidence":"high","items":[{"label":"Retour plaquettes","amount":-120,"category":"repair"}]}
        """);

        x.IsCreditNote.Should().BeTrue();
        x.Category.Should().Be("credit_note");
        (x.AmountHT, x.AmountTVA, x.AmountTTC).Should().Be((100.84m, 19.16m, 120m));
        x.Items!.Single().Amount.Should().Be(120m, "les lignes suivent le total");
        x.Items!.Single().Category.Should().Be("repair");
        x.SupplierName.Should().Be("GARAGE EL AMEN");
    }

    [Fact]
    public async Task Un_scan_au_total_positif_ne_change_pas()
    {
        var (x, _) = await ScannerAsync("""
        {"amountHT":100,"amountTVA":19,"amountTTC":120,"category":"repair","confidence":"high",
         "items":[{"label":"Plaquettes","amount":120,"category":"repair"}]}
        """);

        x.IsCreditNote.Should().BeFalse();
        x.Category.Should().Be("repair");
        x.AmountTTC.Should().Be(120m);
        x.Items!.Single().Amount.Should().Be(120m);
    }

    [Fact]
    public void Un_avoir_aux_lignes_imprimees_en_positif_garde_ses_lignes_et_un_HT_seul_negatif_suffit()
    {
        var lignesPositives = new InvoiceExtraction(null, null, null, null, null, -80m, "TND", "maintenance", null, null, "medium",
            new List<InvoiceLineItem> { new("Filtre repris", 50m, "maintenance"), new("Huile reprise", 30m, "maintenance") });

        var avoir = InvoiceExtractionService.AsCreditNoteIfDetected(lignesPositives);

        avoir.AmountTTC.Should().Be(80m);
        avoir.Items!.Select(i => i.Amount).Should().Equal(50m, 30m);

        var htSeul = new InvoiceExtraction(null, null, null, -45m, null, null, "TND", "other", null, null, "low");
        var converti = InvoiceExtractionService.AsCreditNoteIfDetected(htSeul);
        (converti.Category, converti.AmountHT, converti.AmountTTC, converti.IsCreditNote)
            .Should().Be(("credit_note", 45m, (decimal?)null, true));
    }
}
