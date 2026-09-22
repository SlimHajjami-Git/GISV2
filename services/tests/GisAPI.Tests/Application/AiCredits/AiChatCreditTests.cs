using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.AiCredits;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.AiCredits;

/// <summary>
/// Le crédit IA du mois couvre l'assistant et les rapports IA (demande de Slim du
/// 22/09/2026 : « le quota inclut l'utilisation de l'IA »), sur le VRAI
/// <see cref="AiChatController"/>, action par action :
/// <list type="bullet">
/// <item>refus AVANT tout appel payant — 403 IA désactivée, 429 crédit épuisé (simulacre
/// STRICT : un appel au modèle ferait échouer le test) ;</item>
/// <item>enregistrement APRÈS un appel réussi (société, utilisateur, fonction, jetons) et
/// crédit relu joint à la réponse ;</item>
/// <item>rien d'enregistré quand l'appel échoue.</item>
/// </list>
/// </summary>
public class AiChatCreditTests
{
    private const int CompanyId = 7;
    private const int UserId = 45;

    /// <summary>Les cinq actions qui appellent le modèle, et la fonction qu'elles décomptent.</summary>
    public static TheoryData<string, string> Actions => new()
    {
        { "send", AiFeatures.AssistantChat },
        { "compare", AiFeatures.VehicleCompare },
        { "report", AiFeatures.VehicleReport },
        { "fleet-report", AiFeatures.FleetReport },
        { "fleet-report/ask", AiFeatures.FleetReportAsk },
    };

    private static async Task<TestGisDbContext> ParcAsync(int? tokens, params (string Feature, int Tokens)[] deja)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(new Societe
        {
            Id = CompanyId, Name = "Belive GPA", SubscriptionStatus = "active", IsActive = true,
            InvoiceScanMonthlyTokens = tokens
        });
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" },
            new Vehicle { Id = 2, Name = "Service 02", Plate = "GA-215-RK", CompanyId = CompanyId, Status = "available" });
        foreach (var (feature, t) in deja)
            ctx.AiUsageLogs.Add(new AiUsageLog { CompanyId = CompanyId, UserId = UserId, Feature = feature, TokensUsed = t, CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static Mock<ILlmService> LlmQuiRepond(int tokens)
    {
        var llm = new Mock<ILlmService>(MockBehavior.Strict);
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LlmResponse("réponse de l'IA", tokens));
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LlmResponse("réponse de l'IA", tokens));
        return llm;
    }

    private static Mock<ILlmService> LlmEnPanne()
    {
        var llm = new Mock<ILlmService>(MockBehavior.Strict);
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new HttpRequestException("Groq indisponible"));
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new HttpRequestException("Groq indisponible"));
        return llm;
    }

    private static AiChatController Controleur(TestGisDbContext ctx, Mock<ILlmService> llm)
    {
        var sante = new Mock<IVehicleHealthScoreService>();
        sante.Setup(s => s.CalculateAllScoresAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<VehicleHealthResult>());
        sante.Setup(s => s.CalculateScoreAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new VehicleHealthResult { VehicleId = 1, Score = 80, Level = "good" });

        return new AiChatController(ctx, llm.Object, sante.Object, NullLogger<AiChatController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("companyId", CompanyId.ToString()),
                        new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
                        new Claim(ClaimTypes.Role, "company_admin")
                    }, "test"))
                }
            }
        };
    }

    private static Task<IActionResult> Appeler(AiChatController c, string action) => action switch
    {
        "send" => c.SendMessage(new AiChatRequest(1, "Fais un diagnostic complet")),
        "compare" => c.CompareVehicles(new CompareVehiclesRequest(new List<int> { 1, 2 }, null)),
        "report" => c.GenerateReport(1),
        "fleet-report" => c.GenerateFleetReport(new FleetReportRequest("month", null)),
        "fleet-report/ask" => c.AskFleetReport(new FleetReportAskRequest("Quel véhicule remplacer ?", null)),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };

    // ── Refus AVANT l'appel payant ─────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task IA_desactivee_pour_la_societe_refus_403_sans_appel_au_modele(string action, string _)
    {
        using var ctx = await ParcAsync(tokens: 0);
        var llm = new Mock<ILlmService>(MockBehavior.Strict);   // tout appel ferait échouer le test

        var act = () => Appeler(Controleur(ctx, llm), action);

        var refus = (await act.Should().ThrowAsync<AiCreditException>()).Which;
        refus.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        refus.Code.Should().Be("AI_CREDIT_DISABLED");
        refus.Credit.Enabled.Should().BeFalse();
        ctx.AiUsageLogs.Should().BeEmpty();
        ctx.AiChatMessages.Should().BeEmpty("un message refusé ne reste pas dans l'historique");
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Credit_du_mois_epuise_refus_429_sans_appel_au_modele(string action, string _)
    {
        // Épuisé par D'AUTRES fonctions : le crédit est commun à toute l'IA de la société.
        using var ctx = await ParcAsync(tokens: 8_000, (AiFeatures.ConsumptionExplain, 0), (AiFeatures.FleetReport, 8_000));
        var llm = new Mock<ILlmService>(MockBehavior.Strict);

        var act = () => Appeler(Controleur(ctx, llm), action);

        var refus = (await act.Should().ThrowAsync<AiCreditException>()).Which;
        refus.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        refus.Code.Should().Be("AI_CREDIT_EXHAUSTED");
        refus.Message.Should().StartWith("Crédit IA du mois épuisé (100 %). Il se recharge le ");
        refus.Credit.PercentUsed.Should().Be(100);
        ctx.AiUsageLogs.Should().HaveCount(2, "rien de plus n'est journalisé sur un refus");
        ctx.AiChatMessages.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Un_jeton_sans_societe_connue_est_refuse(string action, string _)
    {
        using var ctx = await ParcAsync(tokens: null);
        ctx.Societes.Remove(ctx.Societes.Single());
        await ctx.SaveChangesAsync();

        var act = () => Appeler(Controleur(ctx, new Mock<ILlmService>(MockBehavior.Strict)), action);

        (await act.Should().ThrowAsync<AiCreditException>()).Which.StatusCode.Should().Be(403);
    }

    // ── Enregistrement APRÈS un appel réussi ───────────────────────────────────

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Un_appel_reussi_est_journalise_et_la_reponse_porte_le_credit_relu(string action, string fonction)
    {
        using var ctx = await ParcAsync(tokens: null, (AiFeatures.AssistantChat, 1_000));
        var llm = LlmQuiRepond(2_345);

        var result = await Appeler(Controleur(ctx, llm), action);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var ligne = ctx.AiUsageLogs.OrderByDescending(l => l.Id).First();
        ligne.Feature.Should().Be(fonction);
        ligne.TokensUsed.Should().Be(2_345);
        ligne.CompanyId.Should().Be(CompanyId);
        ligne.UserId.Should().Be(UserId);
        ctx.AiUsageLogs.Should().HaveCount(2, "une seule ligne par appel");
        ctx.InvoiceScanLogs.Should().BeEmpty();

        var credit = JsonSerializer.SerializeToElement(ok.Value).GetProperty("credit");
        credit.GetProperty("usedTokens").GetInt32().Should().Be(3_345, "crédit RELU après l'écriture");
        credit.GetProperty("remainingTokens").GetInt32().Should().Be(60_000 - 3_345);
        credit.GetProperty("byFeature").GetProperty(fonction).GetInt32()
            .Should().Be(fonction == AiFeatures.AssistantChat ? 3_345 : 2_345);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Un_appel_en_echec_ne_consomme_rien(string action, string _)
    {
        using var ctx = await ParcAsync(tokens: null);

        var result = await Appeler(Controleur(ctx, LlmEnPanne()), action);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(503);
        ctx.AiUsageLogs.Should().BeEmpty("seul un appel réussi consomme le crédit");
    }

    [Fact]
    public async Task Le_dernier_appel_peut_depasser_le_budget_puis_le_suivant_est_refuse()
    {
        // 500 jetons restants : l'appel part (son coût n'est connu qu'après), la barre plafonne.
        using var ctx = await ParcAsync(tokens: 10_000, (AiFeatures.AssistantChat, 9_500));
        var llm = LlmQuiRepond(2_400);

        var premier = await Appeler(Controleur(ctx, llm), "send");
        var credit = JsonSerializer.SerializeToElement(premier.Should().BeOfType<OkObjectResult>().Subject.Value).GetProperty("credit");
        credit.GetProperty("percentUsed").GetInt32().Should().Be(100);
        credit.GetProperty("remainingTokens").GetInt32().Should().Be(0);

        var second = () => Appeler(Controleur(ctx, llm), "send");
        (await second.Should().ThrowAsync<AiCreditException>()).Which.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task Les_lectures_sans_appel_au_modele_ne_sont_ni_controlees_ni_decomptees()
    {
        // Historique, liste des véhicules, scores de santé : crédit à 0, tout reste lisible.
        using var ctx = await ParcAsync(tokens: 0);
        var c = Controleur(ctx, new Mock<ILlmService>(MockBehavior.Strict));

        (await c.GetHistory(1)).Should().BeOfType<OkObjectResult>();
        (await c.GetVehicles()).Should().BeOfType<OkObjectResult>();
        (await c.GetAllHealthScores()).Should().BeOfType<OkObjectResult>();
        ctx.AiUsageLogs.Should().BeEmpty();
    }
}
