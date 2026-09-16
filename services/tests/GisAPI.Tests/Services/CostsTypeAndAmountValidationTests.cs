using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Features.Notifications.Events;
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

namespace GisAPI.Tests.Services;

/// <summary>
/// POST/PUT /api/costs, campagne de test GPA :
/// <list type="bullet">
///   <item>DEF-040 — n'importe quelle catégorie était acceptée : « xyz » créait une
///     catégorie fantôme dans le résumé et les rapports ;</item>
///   <item>DEF-050 — un montant négatif (-50) était enregistré et venait en déduction
///     de tous les totaux.</item>
/// </list>
/// Tests sur le VRAI contrôleur ; la liste des catégories légitimes est recopiée
/// ici depuis chaque écrivain (et non lue dans la liste blanche) pour que retirer un
/// type utilisé par un écran fasse échouer le test.
/// </summary>
public class CostsTypeAndAmountValidationTests
{
    private const int CompanyId = 1;
    private const int AdminUserId = 1;
    private static readonly DateTime Day = new(2026, 9, 14, 8, 0, 0, DateTimeKind.Utc);

    private static ICurrentTenantService Admin()
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(AdminUserId);
        m.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static CostsController Controller(TestGisDbContext ctx)
    {
        var tenant = Admin();
        var publisher = new Mock<IPublisher>();
        publisher
            .Setup(p => p.Publish(It.IsAny<AdminActionNotificationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new CostsController(
            ctx, tenant, publisher.Object,
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

    private static async Task<TestGisDbContext> ContexteAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 32, Name = "Utilitaire", Plate = "GA-214-RK", CompanyId = CompanyId });
        ctx.VehicleCosts.AddRange(
            new VehicleCost { Id = 1, CompanyId = CompanyId, VehicleId = 32, Type = "insurance", Amount = 625m, Date = Day },
            // Ligne historique d'une catégorie hors liste : elle doit rester modifiable.
            new VehicleCost { Id = 2, CompanyId = CompanyId, VehicleId = 32, Type = "Divers atelier", Amount = 40m, Date = Day });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static string Message(IActionResult result) =>
        JsonSerializer.SerializeToElement(result.Should().BeOfType<BadRequestObjectResult>().Subject.Value)
            .GetProperty("message").GetString()!;

    // ─────────────────────────────── catégorie (DEF-040) ───────────────────────────────

    [Fact]
    public async Task Creation_categorie_inconnue_refusee_en_francais_sans_ecriture()
    {
        using var ctx = await ContexteAsync();

        var created = await Controller(ctx).CreateCost(new VehicleCost
        {
            VehicleId = 32, Type = "xyz", Description = "QA catégorie libre", Amount = 1m, Date = Day
        });

        Message(created.Result!).Should().Be("Catégorie de dépense inconnue : « xyz ». Choisissez une catégorie de la liste.");
        (await ctx.VehicleCosts.AsNoTracking().CountAsync()).Should().Be(2, "rien ne doit être enregistré");
    }

    public static IEnumerable<object[]> CategoriesEcritesParLApplication() => new[]
    {
        // Écran Dépenses, « Nouvelle dépense » (catégories hors carburant/entretien/réparation).
        "maintenance", "insurance", "technical_inspection", "tax", "registration", "transport_permit",
        "peage", "stationnement", "amende", "autre",
        // Scan de facture IA (InvoiceExtractionService) et écran Coûts (vehicle-costs).
        "fuel", "repair", "toll", "parking", "fine", "other",
        // Module Sinistres (phases 5 et 6).
        "insurance_refund",
        // Codes de l'import Excel.
        "reparation", "credit", "achat", "lavage",
    }.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(CategoriesEcritesParLApplication))]
    public async Task Creation_toute_categorie_ecrite_par_l_application_est_acceptee_telle_quelle(string type)
    {
        using var ctx = await ContexteAsync();

        var created = await Controller(ctx).CreateCost(new VehicleCost
        {
            VehicleId = 32, Type = type, Amount = 12.5m, Liters = 5m, PricePerLiter = 2.5m, Date = Day
        });

        created.Result.Should().BeOfType<CreatedAtActionResult>();
        (await ctx.VehicleCosts.AsNoTracking().CountAsync(c => c.Type == type && c.Amount == 12.5m)).Should().Be(1);
    }

    [Theory]
    // Synonymes rencontrés en base ou libellés de l'écran : enregistrés sous le code
    // que les rapports ventilent (« carburant » tombait en « Autres »).
    [InlineData("carburant", "fuel")]
    [InlineData("entretien", "maintenance")]
    [InlineData("réparation", "reparation")]
    [InlineData("assurance", "insurance")]
    [InlineData("visite_technique", "technical_inspection")]
    [InlineData("vignette", "tax")]
    [InlineData("taxe", "tax")]
    [InlineData("wash", "lavage")]
    [InlineData("autres", "autre")]
    [InlineData("Carte grise", "registration")]
    public async Task Creation_synonyme_connu_enregistree_sous_son_code(string type, string code)
    {
        using var ctx = await ContexteAsync();

        var created = await Controller(ctx).CreateCost(new VehicleCost
        {
            VehicleId = 32, Type = type, Amount = 12.5m, Date = Day
        });

        created.Result.Should().BeOfType<CreatedAtActionResult>();
        (await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id > 2)).Type.Should().Be(code);
    }

    [Fact]
    public async Task Creation_carburant_ecrit_en_francais_ventile_et_calcule_comme_fuel()
    {
        using var ctx = await ContexteAsync();

        await Controller(ctx).CreateCost(new VehicleCost
        {
            VehicleId = 32, Type = "Carburant", Amount = 0m, Liters = 40m, PricePerLiter = 1.8m, Date = Day
        });

        var ligne = await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id > 2);
        ligne.Type.Should().Be("fuel");
        ligne.Amount.Should().Be(72m);
        GisAPI.Application.Features.Reports.Common.VehicleCostCategory.IsFuel(ligne.Type).Should().BeTrue();
    }

    [Fact]
    public async Task Creation_categorie_connue_ecrite_en_majuscules_enregistree_sous_son_code()
    {
        using var ctx = await ContexteAsync();

        var created = await Controller(ctx).CreateCost(new VehicleCost
        {
            VehicleId = 32, Type = " Amende ", Amount = 90m, Date = Day
        });

        created.Result.Should().BeOfType<CreatedAtActionResult>();
        (await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Amount == 90m)).Type.Should().Be("amende");
    }

    [Fact]
    public async Task Modification_vers_une_categorie_inconnue_refusee_ligne_intacte()
    {
        using var ctx = await ContexteAsync();

        var result = await Controller(ctx).UpdateCost(1, new VehicleCost
        {
            Type = "xyz", Description = "détournée", Amount = 625m, Date = Day
        });

        Message(result).Should().StartWith("Catégorie de dépense inconnue");
        ctx.ChangeTracker.Clear();
        (await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id == 1)).Type.Should().Be("insurance");
    }

    [Fact]
    public async Task Modification_d_une_ligne_historique_hors_liste_sans_changer_sa_categorie_acceptee()
    {
        using var ctx = await ContexteAsync();

        (await Controller(ctx).UpdateCost(2, new VehicleCost
        {
            Type = "Divers atelier", Description = "corrigée", Amount = 45m, Date = Day
        })).Should().BeOfType<NoContentResult>();

        ctx.ChangeTracker.Clear();
        var ligne = await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id == 2);
        ligne.Type.Should().Be("Divers atelier");
        ligne.Amount.Should().Be(45m);
    }

    [Fact]
    public async Task Modification_sans_changer_un_synonyme_historique_ne_reecrit_pas_la_categorie()
    {
        using var ctx = await ContexteAsync();
        ctx.VehicleCosts.Add(new VehicleCost { Id = 3, CompanyId = CompanyId, VehicleId = 32, Type = "entretien", Amount = 80m, Date = Day });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        (await Controller(ctx).UpdateCost(3, new VehicleCost
        {
            Type = "entretien", Amount = 85m, Date = Day
        })).Should().BeOfType<NoContentResult>();

        ctx.ChangeTracker.Clear();
        (await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id == 3)).Type.Should().Be("entretien");
    }

    // ─────────────────────────────── montant (DEF-050) ───────────────────────────────

    [Theory]
    [InlineData(-50)]
    [InlineData(0)]
    public async Task Creation_montant_negatif_ou_nul_refusee_sans_ecriture(int amount)
    {
        using var ctx = await ContexteAsync();

        var created = await Controller(ctx).CreateCost(new VehicleCost
        {
            VehicleId = 32, Type = "maintenance", Description = "QA montant negatif", Amount = amount, Date = Day
        });

        // Le conseil renvoie au dossier sinistre : l'écran Dépenses ne propose pas de
        // catégorie « Remboursement assurance ».
        Message(created.Result!).Should().Be(
            "Le montant doit être supérieur à zéro. Un remboursement d'assurance se saisit en positif dans le dossier sinistre.");
        (await ctx.VehicleCosts.AsNoTracking().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Creation_montant_au_dela_de_la_colonne_refusee_au_lieu_d_une_erreur_500()
    {
        using var ctx = await ContexteAsync();

        var created = await Controller(ctx).CreateCost(new VehicleCost
        {
            VehicleId = 32, Type = "insurance", Amount = 100_000_000m, Date = Day
        });

        Message(created.Result!).Should().StartWith("Montant trop élevé");
    }

    [Fact]
    public async Task Creation_carburant_montant_calcule_depuis_litres_et_prix_acceptee()
    {
        using var ctx = await ContexteAsync();

        var created = await Controller(ctx).CreateCost(new VehicleCost
        {
            VehicleId = 32, Type = "fuel", Amount = 0m, Liters = 40m, PricePerLiter = 1.8m, Date = Day
        });

        created.Result.Should().BeOfType<CreatedAtActionResult>();
        (await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Type == "fuel")).Amount.Should().Be(72m);
    }

    [Fact]
    public async Task Modification_vers_un_montant_negatif_refusee_ligne_intacte()
    {
        using var ctx = await ContexteAsync();

        var result = await Controller(ctx).UpdateCost(1, new VehicleCost
        {
            Type = "insurance", Amount = -625m, Date = Day
        });

        Message(result).Should().StartWith("Le montant doit être supérieur à zéro.");
        ctx.ChangeTracker.Clear();
        (await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id == 1)).Amount.Should().Be(625m);
    }
}
