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
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Crédit IA MENSUEL du scan de factures, en jetons, sur le VRAI <see cref="CostsController"/>.
///
/// Historique : recette du 11/09/2026 (« chaque mois ce quota se réinitialise » — l'écran
/// n'affichait pas la date de remise à zéro), puis demande de Slim du 22/09/2026 : le nombre
/// de scans devient une barre de progression d'un crédit en jetons, rechargé le 1er du mois.
/// Ces tests fixent la règle (mois civil, par société, somme des jetons), les refus avant
/// tout appel payant, et le contrat JSON que lit la barre des cinq écrans.
/// </summary>
public class ScanQuotaMonthlyTests : IDisposable
{
    private const int CompanyId = 14;
    private const int OtherCompanyId = 10;

    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly DateTime MonthStart = new(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Dossier temporaire tenant lieu de ContentRootPath (le scan réussi y stocke la facture).</summary>
    private readonly string _racine = Path.Combine(Path.GetTempPath(), "gisv2-credit-ia-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_racine)) Directory.Delete(_racine, recursive: true); } catch { /* ménage */ }
        GC.SuppressFinalize(this);
    }

    /// <param name="companyIdClaim">Société portée par le jeton ; par défaut celle du test.</param>
    /// <param name="extraction">Service d'extraction IA ; par défaut un simulacre qui ne doit pas être appelé.</param>
    private CostsController Controller(TestGisDbContext ctx, int? companyIdClaim = null, IInvoiceExtractionService? extraction = null)
    {
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.CompanyId).Returns(CompanyId);
        tenant.Setup(x => x.UserId).Returns(45);
        tenant.Setup(x => x.UserRoles).Returns(new[] { "company_admin" });
        tenant.Setup(x => x.IsAuthenticated).Returns(true);

        var publisher = new Mock<IPublisher>();
        publisher.Setup(p => p.Publish(It.IsAny<AdminActionNotificationEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var controller = new CostsController(ctx, tenant.Object, publisher.Object,
            extraction ?? new Mock<IInvoiceExtractionService>(MockBehavior.Strict).Object,
            Mock.Of<IWebHostEnvironment>(e => e.ContentRootPath == _racine),
            new Mock<ILogger<CostsController>>().Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim("companyId", (companyIdClaim ?? CompanyId).ToString()),
                    new Claim(ClaimTypes.NameIdentifier, "45"),
                }, "test"))
            }
        };
        return controller;
    }

    private static JsonElement Body(IActionResult result)
    {
        var value = result switch
        {
            ObjectResult o => o.Value,
            _ => throw new InvalidOperationException("réponse inattendue " + result.GetType().Name),
        };
        return JsonSerializer.SerializeToElement(value);
    }

    /// <summary>Société du test avec son réglage (jetons et/ou ancien quota en scans), une
    /// société voisine, et des scans journalisés (société, date, jetons).</summary>
    private static async Task<TestGisDbContext> SeedAsync(
        int? tokens, int? legacyLimit, params (int Company, DateTime At, int Tokens)[] scans)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(new Societe
        {
            Id = CompanyId, Name = "Belive GPA", SubscriptionStatus = "active", IsActive = true,
            InvoiceScanMonthlyTokens = tokens, InvoiceScanMonthlyLimit = legacyLimit
        });
        ctx.Societes.Add(new Societe { Id = OtherCompanyId, Name = "SICOAC", SubscriptionStatus = "active", IsActive = true });
        foreach (var (company, at, t) in scans)
            ctx.InvoiceScanLogs.Add(new InvoiceScanLog { CompanyId = company, UserId = 45, TokensUsed = t, CreatedAt = at });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static Mock<IInvoiceExtractionService> ExtractionQuiCoute(int tokens)
    {
        var mock = new Mock<IInvoiceExtractionService>();
        mock.Setup(s => s.ExtractAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvoiceExtractionResult(
                new InvoiceExtraction("STATION AGIL", "T-1", "2026-09-12", 88m, 16.8m, 104.8m, "TND", "fuel", null, "Gasoil", "high"),
                tokens));
        return mock;
    }

    // ── GET /api/costs/scan-quota : le contrat de la barre ─────────────────────

    [Fact]
    public async Task Le_GET_rend_exactement_le_contrat_de_la_barre()
    {
        using var ctx = await SeedAsync(null, null, (CompanyId, Now, 25_200));

        var body = Body(await Controller(ctx).GetScanQuota(CancellationToken.None));

        body.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[]
        {
            "enabled", "budgetTokens", "usedTokens", "remainingTokens",
            "percentUsed", "scansThisMonth", "estimatedScansLeft", "resetsAt"
        });
        body.GetProperty("enabled").GetBoolean().Should().BeTrue();
        body.GetProperty("budgetTokens").GetInt32().Should().Be(60_000, "défaut plateforme sans réglage");
        body.GetProperty("usedTokens").GetInt32().Should().Be(25_200);
        body.GetProperty("remainingTokens").GetInt32().Should().Be(34_800);
        body.GetProperty("percentUsed").GetInt32().Should().Be(42);
        body.GetProperty("scansThisMonth").GetInt32().Should().Be(1);
        body.GetProperty("estimatedScansLeft").GetInt32().Should().Be(11);
        body.GetProperty("resetsAt").GetDateTime().Should().Be(MonthStart.AddMonths(1), "recharge le 1er du mois suivant");
    }

    [Fact]
    public async Task La_consommation_somme_les_jetons_du_mois_civil_de_la_societe_seulement()
    {
        using var ctx = await SeedAsync(null, null,
            (CompanyId, MonthStart.AddDays(-1), 3_900),        // mois précédent : ne compte plus
            (CompanyId, MonthStart.AddSeconds(-1), 3_800),     // dernière seconde du mois précédent
            (CompanyId, MonthStart, 2_000),                    // première seconde du mois : compte
            (CompanyId, Now, 3_100),
            (OtherCompanyId, Now, 3_700));                     // une autre société : jamais

        var body = Body(await Controller(ctx).GetScanQuota(CancellationToken.None));

        body.GetProperty("usedTokens").GetInt32().Should().Be(5_100);
        body.GetProperty("scansThisMonth").GetInt32().Should().Be(2);
        body.GetProperty("remainingTokens").GetInt32().Should().Be(54_900);
    }

    [Fact]
    public async Task Un_scan_journalise_sans_consommation_compte_pour_3000_jetons()
    {
        using var ctx = await SeedAsync(null, null, (CompanyId, Now, 0), (CompanyId, Now, 2_500));

        var body = Body(await Controller(ctx).GetScanQuota(CancellationToken.None));

        body.GetProperty("usedTokens").GetInt32().Should().Be(5_500, "0 = consommation inconnue → coût moyen, jamais gratuit");
    }

    [Fact]
    public async Task L_ancien_quota_en_scans_est_converti_en_jetons_a_la_lecture()
    {
        using var ctx = await SeedAsync(null, 50);

        var body = Body(await Controller(ctx).GetScanQuota(CancellationToken.None));

        body.GetProperty("budgetTokens").GetInt32().Should().Be(150_000);
        body.GetProperty("estimatedScansLeft").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task Le_credit_en_jetons_prime_sur_l_ancien_quota()
    {
        using var ctx = await SeedAsync(9_000, 50);

        var body = Body(await Controller(ctx).GetScanQuota(CancellationToken.None));

        body.GetProperty("budgetTokens").GetInt32().Should().Be(9_000);
    }

    [Fact]
    public async Task Un_credit_a_zero_se_lit_sur_la_barre_comme_une_fonction_fermee()
    {
        using var ctx = await SeedAsync(0, null);

        var body = Body(await Controller(ctx).GetScanQuota(CancellationToken.None));

        body.GetProperty("enabled").GetBoolean().Should().BeFalse();
        body.GetProperty("budgetTokens").GetInt32().Should().Be(0);
        body.GetProperty("remainingTokens").GetInt32().Should().Be(0, "le bouton doit se griser, pas promettre un scan refusé");
    }

    [Fact]
    public async Task Une_societe_ne_voit_ni_la_consommation_ni_le_credit_d_une_autre()
    {
        using var ctx = await SeedAsync(60_000, null,
            (CompanyId, Now, 3_000),
            (OtherCompanyId, Now, 3_000), (OtherCompanyId, Now, 3_000), (OtherCompanyId, Now, 3_000));
        var voisine = ctx.Societes.Single(s => s.Id == OtherCompanyId);
        voisine.InvoiceScanMonthlyTokens = 9_000;
        await ctx.SaveChangesAsync();

        var body = Body(await Controller(ctx).GetScanQuota(CancellationToken.None));

        body.GetProperty("usedTokens").GetInt32().Should().Be(3_000, "les 9 000 jetons de la société voisine ne comptent pas ici");
        body.GetProperty("budgetTokens").GetInt32().Should().Be(60_000, "son crédit de 9 000 non plus");
    }

    // ── POST scan-invoice : refus AVANT tout appel payant ──────────────────────

    [Fact]
    public async Task Un_credit_a_zero_ferme_la_fonction_pour_toute_la_societe_403()
    {
        using var ctx = await SeedAsync(0, null);

        var result = await Controller(ctx).ScanInvoice(Facture(), CancellationToken.None);

        var refus = result.Should().BeOfType<ObjectResult>().Subject;
        refus.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var body = Body(result);
        body.GetProperty("message").GetString().Should().Be("Le scan de factures IA n'est pas activé pour votre société.");
        body.GetProperty("quota").GetProperty("enabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task L_ancien_quota_a_zero_ferme_toujours_la_fonction()
    {
        using var ctx = await SeedAsync(null, 0);

        var result = await Controller(ctx).ScanInvoice(Facture(), CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Credit_epuise_429_avec_la_date_de_recharge_et_sans_appel_a_l_IA()
    {
        using var ctx = await SeedAsync(6_000, null, (CompanyId, Now, 3_500), (CompanyId, Now, 2_500));

        // Simulacre STRICT : tout appel à l'extraction ferait échouer le test.
        var result = await Controller(ctx).ScanInvoice(Facture(), CancellationToken.None);

        var refus = result.Should().BeOfType<ObjectResult>().Subject;
        refus.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        var body = Body(result);
        body.GetProperty("message").GetString().Should().Be(
            "Crédit IA du mois épuisé (100 %). Il se recharge le "
            + MonthStart.AddMonths(1).ToString("dd'/'MM'/'yyyy", System.Globalization.CultureInfo.InvariantCulture)
            + " ; votre administrateur peut l'augmenter.");
        body.GetProperty("quota").GetProperty("percentUsed").GetInt32().Should().Be(100);
        body.GetProperty("quota").GetProperty("resetsAt").GetDateTime().Should().Be(MonthStart.AddMonths(1));
        Directory.Exists(_racine).Should().BeFalse("le refus passe avant tout stockage de fichier");
    }

    [Fact]
    public async Task Les_scans_du_mois_precedent_ne_consomment_plus_le_credit()
    {
        using var ctx = await SeedAsync(6_000, null, (CompanyId, MonthStart.AddSeconds(-1), 6_000));
        var extraction = ExtractionQuiCoute(2_800);

        var result = await Controller(ctx, extraction: extraction.Object).ScanInvoice(Facture(), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>("le crédit s'est rechargé le 1er");
    }

    [Fact]
    public async Task Scan_reussi_journalise_les_jetons_et_rend_le_credit_relu()
    {
        using var ctx = await SeedAsync(60_000, null, (CompanyId, Now, 25_200));
        var extraction = ExtractionQuiCoute(3_400);

        var result = await Controller(ctx, extraction: extraction.Object).ScanInvoice(Facture(), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        var quota = Body(result).GetProperty("quota");
        quota.GetProperty("usedTokens").GetInt32().Should().Be(28_600);
        quota.GetProperty("scansThisMonth").GetInt32().Should().Be(2);
        quota.GetProperty("percentUsed").GetInt32().Should().Be(47);
        quota.GetProperty("estimatedScansLeft").GetInt32().Should().Be(10);
        ctx.InvoiceScanLogs.Single(l => l.TokensUsed == 3_400).CompanyId.Should().Be(CompanyId);
    }

    [Fact]
    public async Task Le_dernier_scan_peut_depasser_le_budget_puis_le_suivant_est_refuse()
    {
        // 1 000 jetons restants : le scan part (on ne connaît son coût qu'après l'appel).
        using var ctx = await SeedAsync(60_000, null, (CompanyId, Now, 59_000));
        var extraction = ExtractionQuiCoute(3_500);

        var premier = await Controller(ctx, extraction: extraction.Object).ScanInvoice(Facture(), CancellationToken.None);
        premier.Should().BeOfType<OkObjectResult>();
        var quota = Body(premier).GetProperty("quota");
        quota.GetProperty("percentUsed").GetInt32().Should().Be(100);
        quota.GetProperty("remainingTokens").GetInt32().Should().Be(0);

        var second = await Controller(ctx, extraction: extraction.Object).ScanInvoice(Facture(), CancellationToken.None);
        second.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        extraction.Verify(s => s.ExtractAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Un_jeton_sans_societe_connue_ne_scanne_rien()
    {
        // Ce crédit est le seul contrôle d'accès du scan (19/09/2026) : une société
        // introuvable vaut crédit nul, jamais le défaut plateforme — sinon un jeton hors
        // société consommerait 60 000 jetons journalisés sous une société inexistante.
        using var ctx = await SeedAsync(null, null, (CompanyId, Now, 3_000));

        var compteur = Body(await Controller(ctx, companyIdClaim: 4242).GetScanQuota(CancellationToken.None));
        compteur.GetProperty("enabled").GetBoolean().Should().BeFalse();
        compteur.GetProperty("budgetTokens").GetInt32().Should().Be(0);
        compteur.GetProperty("usedTokens").GetInt32().Should().Be(0);

        var result = await Controller(ctx, companyIdClaim: 4242).ScanInvoice(Facture(), CancellationToken.None);
        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    /// <summary>Photo de facture minimale (en-tête JPEG) : le contrôle du crédit passe avant
    /// toute lecture du contenu, et l'extraction est simulée — ces trois octets suffisent.</summary>
    private static FormFile Facture() =>
        new(new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF }), 0, 3, "file", "facture.jpg")
        { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
}
