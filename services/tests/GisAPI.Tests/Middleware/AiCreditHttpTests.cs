using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Features.AiCredits;
using GisAPI.Application.Services;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// Crédit IA commun à toute l'IA de la société (22/09/2026), côté HTTP :
/// <list type="bullet">
/// <item>le refus levé par AiCredit est rendu { code, message, credit } en 403 / 429 par
/// ExceptionHandlingMiddleware — le contrat lu par la barre et par les écrans d'IA ;</item>
/// <item>GET /api/ai-credit rend le même objet que GET /api/costs/scan-quota ;</item>
/// <item>/api/ai-credit est ouvert à tout utilisateur authentifié de la société sans case de
/// module, mais passe les contrôles de société ; un compte chauffeur reste refusé.</item>
/// </list>
/// </summary>
public class AiCreditHttpTests
{
    private const int CompanyId = 7;
    private const int UserId = 51;
    private const string Chemin = "/api/ai-credit";

    private static readonly DateTime Recharge = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    // ── Refus rendu par ExceptionHandlingMiddleware ────────────────────────────

    private static async Task<(int Status, JsonElement Body)> RenduAsync(Exception exception)
    {
        var middleware = new ExceptionHandlingMiddleware(_ => throw exception, NullLogger<ExceptionHandlingMiddleware>.Instance);
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(http);

        http.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(http.Response.Body);
        return (http.Response.StatusCode, json.RootElement.Clone());
    }

    [Fact]
    public async Task IA_desactivee_403_code_message_et_credit()
    {
        var refus = AiCredit.Refusal(AiCredit.Compute(0, 0, 0, Recharge))!;

        var (status, body) = await RenduAsync(refus);

        status.Should().Be(StatusCodes.Status403Forbidden);
        body.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "code", "message", "credit" });
        body.GetProperty("code").GetString().Should().Be("AI_CREDIT_DISABLED");
        body.GetProperty("message").GetString().Should().Be("Les fonctions d'IA ne sont pas activées pour votre société.");
        body.GetProperty("credit").GetProperty("enabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Credit_epuise_429_code_message_avec_date_et_credit_complet()
    {
        var byFeature = new Dictionary<string, int>(AiCredit.EmptyBreakdown()) { [AiFeatures.AssistantChat] = 61_000 };
        var refus = AiCredit.Refusal(AiCredit.Compute(60_000, 61_000, 0, Recharge, byFeature))!;

        var (status, body) = await RenduAsync(refus);

        status.Should().Be(StatusCodes.Status429TooManyRequests);
        body.GetProperty("code").GetString().Should().Be("AI_CREDIT_EXHAUSTED");
        body.GetProperty("message").GetString().Should().Be(
            "Crédit IA du mois épuisé (100 %). Il se recharge le 01/10/2026 ; votre administrateur peut l'augmenter.");
        var credit = body.GetProperty("credit");
        credit.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[]
        {
            "enabled", "budgetTokens", "usedTokens", "remainingTokens", "percentUsed",
            "scansThisMonth", "estimatedScansLeft", "resetsAt", "byFeature"
        });
        credit.GetProperty("percentUsed").GetInt32().Should().Be(100);
        credit.GetProperty("remainingTokens").GetInt32().Should().Be(0);
        credit.GetProperty("resetsAt").GetDateTime().Should().Be(Recharge);
        credit.GetProperty("byFeature").GetProperty("assistant_chat").GetInt32().Should().Be(61_000);
    }

    // ── GET /api/ai-credit ─────────────────────────────────────────────────────

    private static ClaimsPrincipal Employe(int companyId = CompanyId) => new(new ClaimsIdentity(new[]
    {
        new Claim("companyId", companyId.ToString()),
        new Claim(ClaimTypes.NameIdentifier, UserId.ToString())
    }, "test"));

    private static CostsController Couts(TestGisDbContext ctx)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(CompanyId, UserId);
        return new CostsController(ctx, tenant.Object, Mock.Of<IPublisher>(),
            new Mock<IInvoiceExtractionService>(MockBehavior.Strict).Object,
            Mock.Of<IWebHostEnvironment>(), new Mock<ILogger<CostsController>>().Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Employe() } }
        };
    }

    [Fact]
    public async Task GET_ai_credit_rend_le_meme_objet_que_scan_quota_ventilation_comprise()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(new Societe { Id = CompanyId, Name = "Belive GPA", SubscriptionStatus = "active", IsActive = true });
        ctx.InvoiceScanLogs.Add(new InvoiceScanLog { CompanyId = CompanyId, UserId = UserId, TokensUsed = 3_000, CreatedAt = DateTime.UtcNow });
        ctx.AiUsageLogs.Add(new AiUsageLog { CompanyId = CompanyId, UserId = UserId, Feature = AiFeatures.AssistantChat, TokensUsed = 22_200, CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var controleur = new AiCreditController(ctx)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Employe() } }
        };
        var credit = JsonSerializer.SerializeToElement(
            (await controleur.Get(CancellationToken.None)).Result.Should().BeOfType<OkObjectResult>().Subject.Value);
        var quota = JsonSerializer.SerializeToElement(
            (await Couts(ctx).GetScanQuota(CancellationToken.None)).Should().BeOfType<OkObjectResult>().Subject.Value);

        credit.GetRawText().Should().Be(quota.GetRawText());
        credit.GetProperty("usedTokens").GetInt32().Should().Be(25_200);
        credit.GetProperty("percentUsed").GetInt32().Should().Be(42);
        credit.GetProperty("byFeature").GetProperty("invoice_scan").GetInt32().Should().Be(3_000);
        credit.GetProperty("byFeature").GetProperty("assistant_chat").GetInt32().Should().Be(22_200);
    }

    [Fact]
    public async Task GET_ai_credit_d_un_jeton_sans_societe_connue_rend_un_credit_nul()
    {
        using var ctx = TestDbContextFactory.Create();

        var controleur = new AiCreditController(ctx)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Employe(4242) } }
        };
        var credit = (AiCreditStatus)(await controleur.Get(CancellationToken.None)).Result
            .Should().BeOfType<OkObjectResult>().Subject.Value!;

        credit.Enabled.Should().BeFalse();
        credit.BudgetTokens.Should().Be(0);
    }

    // ── PermissionMiddleware : ouvert à la société, fermé au chauffeur ─────────

    private static async Task<TestGisDbContext> SocieteAsync(Action<User>? user = null)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Roles.Add(new Role { Id = 1, Name = "Employé", SocieteId = CompanyId, IsCompanyAdmin = false });
        var employe = TestDataBuilder.CreateUser(id: UserId, companyId: CompanyId, email: "employe@test.com");
        user?.Invoke(employe);
        ctx.Users.Add(employe);
        // Même raison que ScanFactureAccesTests : clé fantôme vers Societe.
        var societeFk = ctx.Model.FindEntityType(typeof(User))!
            .FindNavigation(nameof(User.Societe))!.ForeignKey.Properties.Single();
        ctx.Entry(employe).Property(societeFk.Name).CurrentValue = CompanyId;
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private sealed record Outcome(int StatusCode, bool ReachedController, string Body);

    private static async Task<Outcome> SendAsync(TestGisDbContext ctx, int userId, bool claimChauffeur = false)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.Request.Path = Chemin;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("companyId", CompanyId.ToString()),
        };
        if (claimChauffeur) claims.Add(new Claim(JwtClaims.AccountType, UserAccountTypes.Driver));
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
        http.Response.Body = new MemoryStream();

        var reached = false;
        var middleware = new PermissionMiddleware(_ => { reached = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(http, ctx);

        http.Response.Body.Position = 0;
        return new Outcome(http.Response.StatusCode, reached, await new StreamReader(http.Response.Body).ReadToEndAsync());
    }

    [Fact]
    public async Task Un_employe_sans_aucune_case_de_module_lit_le_credit_de_sa_societe()
    {
        await using var ctx = await SocieteAsync(u =>
        {
            u.CanCosts = false; u.CanReports = false; u.CanVehicles = false; u.CanMonitoring = false; u.CanFuel = false;
        });

        var outcome = await SendAsync(ctx, UserId);

        outcome.ReachedController.Should().BeTrue();
        outcome.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Le_jeton_d_un_utilisateur_supprime_est_refuse()
    {
        await using var ctx = await SocieteAsync();

        var outcome = await SendAsync(ctx, 9999);

        outcome.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        outcome.ReachedController.Should().BeFalse();
    }

    [Fact]
    public async Task Un_compte_chauffeur_reste_refuse_par_la_garde_du_haut()
    {
        await using var ctx = await SocieteAsync(u => u.AccountType = UserAccountTypes.Driver);

        var outcome = await SendAsync(ctx, UserId, claimChauffeur: true);

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain(PermissionMiddleware.DriverAppOnlyCode);
    }

    [Fact]
    public async Task Un_compte_devenu_chauffeur_apres_son_jeton_est_refuse()
    {
        await using var ctx = await SocieteAsync(u => u.AccountType = UserAccountTypes.Driver);

        var outcome = await SendAsync(ctx, UserId, claimChauffeur: false);

        outcome.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain(PermissionMiddleware.AccountTypeChangedCode);
    }

    [Fact]
    public void Le_chemin_passe_les_controles_de_societe_sans_case_de_module_exigee()
    {
        PermissionMiddleware.ClassifyRoute(Chemin, "GET").Should().Be(PermissionMiddleware.RouteGate.TenantChecks,
            "pas une route toujours ouverte : l'utilisateur est chargé, un compte supprimé est refusé");
        PermissionMiddleware.RequiredUserPermission(Chemin).Should().BeNull();
        PermissionMiddleware.IsDriverAppRoute(Chemin).Should().BeFalse();
    }
}
