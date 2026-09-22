using FluentAssertions;
using GisAPI.Application.Features.Societes.Commands.SetSocieteScanQuota;
using GisAPI.Application.Features.Societes.Queries.GetSocieteById;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Application.Admin;

/// <summary>
/// Réglage et lecture du crédit IA mensuel depuis la fiche société de l'admin système
/// (22/09/2026 : le quota en NOMBRE de scans devient un crédit en JETONS).
/// </summary>
public class ScanCreditAdminTests
{
    private const int CompanyId = 14;

    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly DateTime MonthStart = new(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<TestGisDbContext> SeedAsync(int? tokens, int? legacyLimit, params (DateTime At, int Tokens)[] scans)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(new Societe
        {
            Id = CompanyId, Name = "Belive GPA", SubscriptionStatus = "active", IsActive = true,
            InvoiceScanMonthlyTokens = tokens, InvoiceScanMonthlyLimit = legacyLimit
        });
        foreach (var (at, t) in scans)
            ctx.InvoiceScanLogs.Add(new InvoiceScanLog { CompanyId = CompanyId, UserId = 45, TokensUsed = t, CreatedAt = at });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static Task<GisAPI.Application.Features.Costs.InvoiceScanCreditStatus> Regler(TestGisDbContext ctx, int? tokens) =>
        new SetSocieteScanQuotaCommandHandler(ctx).Handle(new SetSocieteScanQuotaCommand(CompanyId, tokens), CancellationToken.None);

    // ── Commande de réglage ───────────────────────────────────────────────────

    [Fact]
    public async Task Le_reglage_ecrit_les_jetons_et_rend_le_credit_du_mois()
    {
        using var ctx = await SeedAsync(null, null, (Now, 30_000));

        var credit = await Regler(ctx, 120_000);

        ctx.Societes.Single(s => s.Id == CompanyId).InvoiceScanMonthlyTokens.Should().Be(120_000);
        credit.BudgetTokens.Should().Be(120_000);
        credit.UsedTokens.Should().Be(30_000);
        credit.PercentUsed.Should().Be(25);
        credit.ResetsAt.Should().Be(MonthStart.AddMonths(1));
    }

    [Fact]
    public async Task Le_reglage_remplace_l_ancien_quota_par_son_equivalent_en_scans_pour_le_retour_arriere()
    {
        // L'ancien pod ne lit que cette colonne : après un rollout undo, la société doit
        // garder ≈ son crédit (90 000 jetons ≈ 30 scans), pas retomber au défaut de 20.
        using var ctx = await SeedAsync(null, 50);

        var credit = await Regler(ctx, 90_000);

        ctx.Societes.Single(s => s.Id == CompanyId).InvoiceScanMonthlyLimit.Should().Be(30);
        credit.BudgetTokens.Should().Be(90_000, "les jetons font foi, l'ombre n'est pas relue");
    }

    [Fact]
    public async Task Desactiver_ecrit_zero_dans_l_ancienne_colonne_pour_qu_un_retour_arriere_ne_rouvre_pas_le_scan()
    {
        // NULL dans l'ancienne colonne = « 20 scans par défaut » pour l'ancien pod : un
        // rollout undo aurait rouvert le scan à une société coupée.
        using var ctx = await SeedAsync(null, 50);

        await Regler(ctx, 0);

        var societe = ctx.Societes.Single(s => s.Id == CompanyId);
        societe.InvoiceScanMonthlyTokens.Should().Be(0);
        societe.InvoiceScanMonthlyLimit.Should().Be(0);
    }

    [Fact]
    public async Task Un_ancien_quota_au_dela_du_plafond_se_reenregistre_tel_que_la_fiche_le_preremplit()
    {
        // Ancien quota de 5 000 scans : la fiche affiche et préremplit le budget effectif.
        // Il doit être au plafond (10 000 000), sinon « Enregistrer » sans retouche = 400.
        using var ctx = await SeedAsync(null, 5_000);
        var dto = await new GetSocieteByIdQueryHandler(ctx).Handle(new GetSocieteByIdQuery(CompanyId), CancellationToken.None);
        dto.InvoiceScanBudgetTokens.Should().Be(10_000_000);

        var credit = await Regler(ctx, dto.InvoiceScanBudgetTokens);

        credit.BudgetTokens.Should().Be(10_000_000);
        // Même chose par la voie de transition (ancienne fiche en cache qui envoie des scans).
        new SetScanQuotaRequest(MonthlyLimit: 5_000).EffectiveMonthlyTokens().Should().Be(10_000_000);
    }

    [Fact]
    public async Task Revenir_au_defaut_donne_60000_jetons_et_pas_l_ancien_quota_converti()
    {
        // Sans l'effacement de l'ancienne colonne, « vide = défaut » aurait rendu les
        // 50 scans × 3 000 = 150 000 jetons de l'ancien réglage.
        using var ctx = await SeedAsync(null, 50);

        var credit = await Regler(ctx, null);

        ctx.Societes.Single(s => s.Id == CompanyId).InvoiceScanMonthlyTokens.Should().BeNull();
        credit.BudgetTokens.Should().Be(60_000);
    }

    [Fact]
    public async Task Zero_jeton_desactive_la_fonction()
    {
        using var ctx = await SeedAsync(null, null);

        var credit = await Regler(ctx, 0);

        ctx.Societes.Single(s => s.Id == CompanyId).InvoiceScanMonthlyTokens.Should().Be(0);
        credit.Enabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_000_001)]
    public async Task Hors_bornes_refus_400_avec_un_message_lisible(int tokens)
    {
        using var ctx = await SeedAsync(null, 50);

        var act = () => Regler(ctx, tokens);

        // DomainException = 400 + message pour la fiche (ArgumentException donnait un 500).
        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage(SetSocieteScanQuotaCommandHandler.InvalidTokensMessage);
        ctx.Societes.Single(s => s.Id == CompanyId).InvoiceScanMonthlyLimit.Should().Be(50, "rien n'est écrit sur un refus");
    }

    [Fact]
    public async Task Le_maximum_de_10_millions_est_accepte()
    {
        using var ctx = await SeedAsync(null, null);

        var credit = await Regler(ctx, 10_000_000);

        credit.BudgetTokens.Should().Be(10_000_000);
    }

    [Fact]
    public async Task Societe_inconnue_404()
    {
        using var ctx = await SeedAsync(null, null);

        var act = () => new SetSocieteScanQuotaCommandHandler(ctx)
            .Handle(new SetSocieteScanQuotaCommand(4242, 1_000), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── Corps de la requête PUT (transition de déploiement) ───────────────────

    [Fact]
    public void Le_champ_en_jetons_fait_foi()
    {
        new SetScanQuotaRequest(MonthlyTokens: 45_000, MonthlyLimit: 50).EffectiveMonthlyTokens().Should().Be(45_000);
        new SetScanQuotaRequest(MonthlyTokens: 0).EffectiveMonthlyTokens().Should().Be(0);
    }

    [Fact]
    public void Une_ancienne_fiche_admin_qui_envoie_des_scans_est_convertie_et_pas_remise_au_defaut()
    {
        new SetScanQuotaRequest(MonthlyLimit: 50).EffectiveMonthlyTokens().Should().Be(150_000);
        new SetScanQuotaRequest(MonthlyLimit: 0).EffectiveMonthlyTokens().Should().Be(0);
    }

    [Fact]
    public void Rien_de_precise_revient_au_defaut_et_un_nombre_de_scans_negatif_reste_refusable()
    {
        new SetScanQuotaRequest().EffectiveMonthlyTokens().Should().BeNull();
        new SetScanQuotaRequest(MonthlyLimit: -3).EffectiveMonthlyTokens().Should().Be(-3);
    }

    // ── Lecture de la fiche ───────────────────────────────────────────────────

    [Fact]
    public async Task La_fiche_rend_budget_consommation_pourcentage_et_date_de_recharge()
    {
        using var ctx = await SeedAsync(null, 50,
            (MonthStart.AddSeconds(-1), 9_000),   // mois précédent : ne compte pas
            (Now, 3_000), (Now, 0));              // 0 = inconnu → 3 000

        var dto = await new GetSocieteByIdQueryHandler(ctx).Handle(new GetSocieteByIdQuery(CompanyId), CancellationToken.None);

        dto.InvoiceScanMonthlyTokens.Should().BeNull();
        dto.InvoiceScanMonthlyLimit.Should().Be(50);
        dto.InvoiceScanBudgetTokens.Should().Be(150_000, "ancien quota de 50 scans converti");
        dto.InvoiceScanUsedTokens.Should().Be(6_000);
        dto.InvoiceScanPercentUsed.Should().Be(4);
        dto.InvoiceScanUsedThisMonth.Should().Be(2);
        dto.InvoiceScanResetsAt.Should().Be(MonthStart.AddMonths(1));
    }

    [Fact]
    public async Task La_fiche_d_une_societe_sans_reglage_montre_le_defaut()
    {
        using var ctx = await SeedAsync(null, null);

        var dto = await new GetSocieteByIdQueryHandler(ctx).Handle(new GetSocieteByIdQuery(CompanyId), CancellationToken.None);

        dto.InvoiceScanBudgetTokens.Should().Be(60_000);
        dto.InvoiceScanUsedTokens.Should().Be(0);
        dto.InvoiceScanPercentUsed.Should().Be(0);
    }
}
