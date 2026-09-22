using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.AiCredits;
using GisAPI.Application.Features.FuelExpenses.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.AiCredits;

/// <summary>
/// Explication IA d'une tranche de consommation (clic sur une barre du rapport) : une requête
/// MediatR, donc un refus de crédit par exception (AiCreditException, rendue 403/429 par
/// ExceptionHandlingMiddleware), contrôlé APRÈS le cache (une explication déjà payée se relit
/// gratuitement) et AVANT l'appel payant ; enregistré après un appel réussi seulement.
/// </summary>
public class ExplainSegmentCreditTests
{
    private const int CompanyId = 7;
    private const int UserId = 45;

    private static async Task<TestGisDbContext> ParcAsync(int? tokens)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(new Societe
        {
            Id = CompanyId, Name = "Belive GPA", SubscriptionStatus = "active", IsActive = true,
            InvoiceScanMonthlyTokens = tokens
        });
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Scania 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available", GpsDeviceId = 31 });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    /// <summary>Tranche unique par test : le cache du gestionnaire est partagé par le processus.</summary>
    private static ExplainConsumptionSegmentQuery Tranche()
    {
        var debut = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc).AddMinutes(Random.Shared.Next(1, 500_000));
        return new ExplainConsumptionSegmentQuery(1, debut, debut.AddHours(2), 100m, 38m, 38m, null, true, null, 100, 32m, 28m, 41m);
    }

    private static ExplainConsumptionSegmentQueryHandler Gestionnaire(TestGisDbContext ctx, Mock<ILlmService> llm) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId, UserId).Object, llm.Object);

    private static Mock<ILlmService> LlmQuiRepond(int tokens)
    {
        var llm = new Mock<ILlmService>(MockBehavior.Strict);
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new LlmResponse("Causes probables : vitesse élevée. Recommandation : lever le pied.", tokens));
        return llm;
    }

    [Fact]
    public async Task IA_desactivee_refus_403_avant_l_appel_payant()
    {
        using var ctx = await ParcAsync(0);

        var act = () => Gestionnaire(ctx, new Mock<ILlmService>(MockBehavior.Strict)).Handle(Tranche(), CancellationToken.None);

        (await act.Should().ThrowAsync<AiCreditException>()).Which.Code.Should().Be("AI_CREDIT_DISABLED");
        ctx.AiUsageLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Credit_epuise_refus_429_avant_l_appel_payant()
    {
        using var ctx = await ParcAsync(5_000);
        ctx.AiUsageLogs.Add(new AiUsageLog { CompanyId = CompanyId, UserId = UserId, Feature = AiFeatures.AssistantChat, TokensUsed = 5_000, CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var act = () => Gestionnaire(ctx, new Mock<ILlmService>(MockBehavior.Strict)).Handle(Tranche(), CancellationToken.None);

        var refus = (await act.Should().ThrowAsync<AiCreditException>()).Which;
        refus.StatusCode.Should().Be(429);
        refus.Credit.RemainingTokens.Should().Be(0);
    }

    [Fact]
    public async Task Un_appel_reussi_est_journalise_et_rend_le_credit()
    {
        using var ctx = await ParcAsync(null);

        var dto = await Gestionnaire(ctx, LlmQuiRepond(640)).Handle(Tranche(), CancellationToken.None);

        dto.FromCache.Should().BeFalse();
        var ligne = ctx.AiUsageLogs.Single();
        ligne.Feature.Should().Be("consumption_explain");
        ligne.TokensUsed.Should().Be(640);
        ligne.UserId.Should().Be(UserId);
        ligne.CompanyId.Should().Be(CompanyId);
        dto.Credit.Should().NotBeNull();
        dto.Credit!.UsedTokens.Should().Be(640);
    }

    [Fact]
    public async Task Une_explication_en_cache_se_relit_sans_rien_consommer_meme_credit_epuise()
    {
        using var ctx = await ParcAsync(null);
        var tranche = Tranche();
        await Gestionnaire(ctx, LlmQuiRepond(640)).Handle(tranche, CancellationToken.None);
        ctx.Societes.Single().InvoiceScanMonthlyTokens = 0;   // l'admin coupe l'IA entre-temps
        await ctx.SaveChangesAsync();

        var dto = await Gestionnaire(ctx, new Mock<ILlmService>(MockBehavior.Strict)).Handle(tranche, CancellationToken.None);

        dto.FromCache.Should().BeTrue();
        dto.Credit.Should().BeNull("aucun appel payant");
        ctx.AiUsageLogs.Should().ContainSingle("la relecture en cache n'est pas décomptée");
    }

    [Fact]
    public async Task Un_appel_en_echec_ne_consomme_rien()
    {
        using var ctx = await ParcAsync(null);
        var llm = new Mock<ILlmService>(MockBehavior.Strict);
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new HttpRequestException("Groq indisponible"));

        var dto = await Gestionnaire(ctx, llm).Handle(Tranche(), CancellationToken.None);

        dto.Explanation.Should().Be("Analyse IA momentanément indisponible.");
        ctx.AiUsageLogs.Should().BeEmpty();
    }
}
