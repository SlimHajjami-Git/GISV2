using FluentAssertions;
using GisAPI.Application.Features.AiCredits;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Application.AiCredits;

/// <summary>
/// Consommation du crédit IA du mois, lue en base (demande de Slim du 22/09/2026 : « et bien
/// sûr le quota inclut l'utilisation de l'IA »). Consommation = jetons des scans
/// (invoice_scan_logs) + jetons de tous les autres appels (ai_usage_logs), par société,
/// depuis le 1er du mois (UTC) ; un appel muet (0 jeton) compte une estimation de sa fonction.
/// </summary>
public class AiCreditConsumptionTests
{
    private const int CompanyId = 14;
    private const int OtherCompanyId = 10;

    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly DateTime MonthStart = new(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<TestGisDbContext> SeedAsync(int? tokens = null)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(new Societe
        {
            Id = CompanyId, Name = "Belive GPA", SubscriptionStatus = "active", IsActive = true,
            InvoiceScanMonthlyTokens = tokens
        });
        ctx.Societes.Add(new Societe { Id = OtherCompanyId, Name = "SICOAC", SubscriptionStatus = "active", IsActive = true });
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static AiUsageLog Usage(int company, string feature, int tokens, DateTime? at = null, int? user = 45) =>
        new() { CompanyId = company, UserId = user, Feature = feature, TokensUsed = tokens, CreatedAt = at ?? Now };

    private static InvoiceScanLog Scan(int company, int tokens, DateTime? at = null) =>
        new() { CompanyId = company, UserId = 45, TokensUsed = tokens, CreatedAt = at ?? Now };

    // ── Somme scans + autres usages ────────────────────────────────────────────

    [Fact]
    public async Task La_consommation_somme_les_scans_et_tous_les_autres_usages_de_l_IA()
    {
        using var ctx = await SeedAsync();
        ctx.InvoiceScanLogs.AddRange(Scan(CompanyId, 3_100), Scan(CompanyId, 2_900));
        ctx.AiUsageLogs.AddRange(
            Usage(CompanyId, AiFeatures.AssistantChat, 2_400),
            Usage(CompanyId, AiFeatures.AssistantChat, 3_400),
            Usage(CompanyId, AiFeatures.FleetReport, 6_200),
            Usage(CompanyId, AiFeatures.ConsumptionExplain, 700),
            Usage(CompanyId, AiFeatures.AccidentNarrative, 1_900, user: null));
        await ctx.SaveChangesAsync();

        var credit = await AiCredit.LoadAsync(ctx, CompanyId, Now, CancellationToken.None);

        credit.UsedTokens.Should().Be(6_000 + 5_800 + 6_200 + 700 + 1_900);
        credit.ScansThisMonth.Should().Be(2, "seuls les scans sont comptés comme des scans");
        credit.RemainingTokens.Should().Be(60_000 - 20_600);
        credit.PercentUsed.Should().Be(34);
    }

    [Fact]
    public async Task La_ventilation_par_fonction_retombe_sur_le_total()
    {
        using var ctx = await SeedAsync();
        ctx.InvoiceScanLogs.Add(Scan(CompanyId, 3_000));
        ctx.AiUsageLogs.AddRange(
            Usage(CompanyId, AiFeatures.AssistantChat, 2_400),
            Usage(CompanyId, AiFeatures.VehicleCompare, 2_800),
            Usage(CompanyId, AiFeatures.VehicleReport, 3_300),
            Usage(CompanyId, AiFeatures.FleetReportAsk, 2_100));
        await ctx.SaveChangesAsync();

        var credit = await AiCredit.LoadAsync(ctx, CompanyId, Now, CancellationToken.None);

        credit.ByFeature.Keys.Should().Equal(AiFeatures.All, "toutes les fonctions, 0 compris, dans l'ordre de la fiche");
        credit.ByFeature[AiFeatures.InvoiceScan].Should().Be(3_000);
        credit.ByFeature[AiFeatures.AssistantChat].Should().Be(2_400);
        credit.ByFeature[AiFeatures.VehicleCompare].Should().Be(2_800);
        credit.ByFeature[AiFeatures.VehicleReport].Should().Be(3_300);
        credit.ByFeature[AiFeatures.FleetReportAsk].Should().Be(2_100);
        credit.ByFeature[AiFeatures.FleetReport].Should().Be(0);
        credit.ByFeature.Values.Sum().Should().Be(credit.UsedTokens);
    }

    [Fact]
    public async Task Un_appel_muet_compte_l_estimation_de_sa_fonction_jamais_zero()
    {
        // Fournisseur muet (bloc « usage » absent → 0) : sans estimation, l'IA deviendrait
        // gratuite et le crédit illimité.
        using var ctx = await SeedAsync();
        ctx.InvoiceScanLogs.Add(Scan(CompanyId, 0));
        ctx.AiUsageLogs.AddRange(
            Usage(CompanyId, AiFeatures.AssistantChat, 0),
            Usage(CompanyId, AiFeatures.AssistantChat, 2_000),
            Usage(CompanyId, AiFeatures.FleetReport, 0),
            Usage(CompanyId, AiFeatures.ConsumptionExplain, 0),
            Usage(CompanyId, "fonction_future", 0));
        await ctx.SaveChangesAsync();

        var credit = await AiCredit.LoadAsync(ctx, CompanyId, Now, CancellationToken.None);

        credit.ByFeature[AiFeatures.InvoiceScan].Should().Be(3_000);
        credit.ByFeature[AiFeatures.AssistantChat].Should().Be(2_500 + 2_000);
        credit.ByFeature[AiFeatures.FleetReport].Should().Be(5_000);
        credit.ByFeature[AiFeatures.ConsumptionExplain].Should().Be(1_000);
        credit.ByFeature["fonction_future"].Should().Be(2_500, "une fonction inconnue compte aussi, jamais pour zéro");
        credit.UsedTokens.Should().Be(3_000 + 4_500 + 5_000 + 1_000 + 2_500);
    }

    [Fact]
    public async Task Seul_le_mois_civil_en_cours_compte()
    {
        using var ctx = await SeedAsync();
        ctx.AiUsageLogs.AddRange(
            Usage(CompanyId, AiFeatures.AssistantChat, 9_000, MonthStart.AddSeconds(-1)),   // mois précédent
            Usage(CompanyId, AiFeatures.AssistantChat, 2_000, MonthStart),                  // première seconde du mois
            Usage(CompanyId, AiFeatures.FleetReport, 4_000, MonthStart.AddDays(-20)));
        await ctx.SaveChangesAsync();

        var credit = await AiCredit.LoadAsync(ctx, CompanyId, Now, CancellationToken.None);

        credit.UsedTokens.Should().Be(2_000);
    }

    [Fact]
    public async Task Une_societe_ne_consomme_jamais_le_credit_d_une_autre()
    {
        using var ctx = await SeedAsync();
        ctx.InvoiceScanLogs.Add(Scan(OtherCompanyId, 3_000));
        ctx.AiUsageLogs.AddRange(
            Usage(OtherCompanyId, AiFeatures.AssistantChat, 50_000),
            Usage(OtherCompanyId, AiFeatures.FleetReport, 0),
            Usage(CompanyId, AiFeatures.AssistantChat, 1_000));
        await ctx.SaveChangesAsync();

        var ici = await AiCredit.LoadAsync(ctx, CompanyId, Now, CancellationToken.None);
        var voisine = await AiCredit.LoadAsync(ctx, OtherCompanyId, Now, CancellationToken.None);

        ici.UsedTokens.Should().Be(1_000);
        ici.ScansThisMonth.Should().Be(0);
        voisine.UsedTokens.Should().Be(3_000 + 50_000 + 5_000);
    }

    [Fact]
    public async Task Une_societe_introuvable_n_a_aucun_credit()
    {
        using var ctx = await SeedAsync();
        ctx.AiUsageLogs.Add(Usage(4242, AiFeatures.AssistantChat, 1_000));
        await ctx.SaveChangesAsync();

        var credit = await AiCredit.LoadAsync(ctx, 4242, Now, CancellationToken.None);

        credit.Enabled.Should().BeFalse();
        credit.BudgetTokens.Should().Be(0);
        credit.RemainingTokens.Should().Be(0);
    }

    // ── Contrôle avant l'appel ─────────────────────────────────────────────────

    [Fact]
    public async Task Le_controle_rend_le_credit_quand_il_reste_des_jetons()
    {
        using var ctx = await SeedAsync(10_000);
        ctx.AiUsageLogs.Add(Usage(CompanyId, AiFeatures.AssistantChat, 9_999));
        await ctx.SaveChangesAsync();

        var credit = await AiCredit.EnsureAvailableAsync(ctx, CompanyId, CancellationToken.None);

        credit.RemainingTokens.Should().Be(1);
    }

    [Fact]
    public async Task Le_controle_refuse_en_429_un_credit_epuise_par_n_importe_quelle_fonction()
    {
        using var ctx = await SeedAsync(10_000);
        ctx.AiUsageLogs.AddRange(
            Usage(CompanyId, AiFeatures.FleetReport, 6_000),
            Usage(CompanyId, AiFeatures.AccidentNarrative, 4_500, user: null));
        await ctx.SaveChangesAsync();

        var act = () => AiCredit.EnsureAvailableAsync(ctx, CompanyId, CancellationToken.None);

        var refus = (await act.Should().ThrowAsync<AiCreditException>()).Which;
        refus.StatusCode.Should().Be(429);
        refus.Code.Should().Be(AiCreditException.ExhaustedCode);
        refus.Credit.UsedTokens.Should().Be(10_500);
        refus.Credit.PercentUsed.Should().Be(100);
    }

    [Fact]
    public async Task Le_controle_refuse_en_403_une_IA_desactivee()
    {
        using var ctx = await SeedAsync(0);

        var act = () => AiCredit.EnsureAvailableAsync(ctx, CompanyId, CancellationToken.None);

        var refus = (await act.Should().ThrowAsync<AiCreditException>()).Which;
        refus.StatusCode.Should().Be(403);
        refus.Code.Should().Be(AiCreditException.DisabledCode);
    }

    // ── Enregistrement après l'appel ───────────────────────────────────────────

    [Fact]
    public async Task Un_appel_hors_scan_est_journalise_dans_ai_usage_logs_et_le_credit_relu()
    {
        using var ctx = await SeedAsync();

        var credit = await AiCredit.RecordUsageAsync(ctx, CompanyId, 45, AiFeatures.AssistantChat, 2_400, CancellationToken.None);

        var ligne = ctx.AiUsageLogs.Single();
        ligne.CompanyId.Should().Be(CompanyId);
        ligne.UserId.Should().Be(45);
        ligne.Feature.Should().Be("assistant_chat");
        ligne.TokensUsed.Should().Be(2_400);
        ctx.InvoiceScanLogs.Should().BeEmpty();
        credit.UsedTokens.Should().Be(2_400);
        credit.ByFeature[AiFeatures.AssistantChat].Should().Be(2_400);
    }

    [Fact]
    public async Task Un_scan_est_journalise_dans_invoice_scan_logs_jamais_dans_les_deux()
    {
        using var ctx = await SeedAsync();

        var credit = await AiCredit.RecordUsageAsync(ctx, CompanyId, 45, AiFeatures.InvoiceScan, 3_300, CancellationToken.None);

        ctx.InvoiceScanLogs.Single().TokensUsed.Should().Be(3_300);
        ctx.AiUsageLogs.Should().BeEmpty("pas de double comptage");
        credit.UsedTokens.Should().Be(3_300);
        credit.ScansThisMonth.Should().Be(1);
    }

    [Fact]
    public async Task Un_appel_systeme_est_journalise_sans_utilisateur()
    {
        using var ctx = await SeedAsync();

        await AiCredit.LogUsageAsync(ctx, CompanyId, null, AiFeatures.AccidentNarrative, 1_700);
        await AiCredit.LogUsageAsync(ctx, CompanyId, 0, AiFeatures.ConsumptionExplain, 400);

        ctx.AiUsageLogs.Should().OnlyContain(l => l.UserId == null, "0 n'est pas un utilisateur");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Sans_societe_connue_rien_n_est_journalise(int companyId)
    {
        using var ctx = await SeedAsync();

        await AiCredit.LogUsageAsync(ctx, companyId, 45, AiFeatures.AccidentNarrative, 1_700);
        await AiCredit.LogUsageAsync(ctx, companyId, 45, AiFeatures.InvoiceScan, 3_000);

        ctx.AiUsageLogs.Should().BeEmpty();
        ctx.InvoiceScanLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Une_consommation_negative_est_journalisee_a_zero_donc_estimee()
    {
        using var ctx = await SeedAsync();

        var credit = await AiCredit.RecordUsageAsync(ctx, CompanyId, 45, AiFeatures.VehicleReport, -5, CancellationToken.None);

        ctx.AiUsageLogs.Single().TokensUsed.Should().Be(0);
        credit.UsedTokens.Should().Be(3_500, "rapport véhicule muet = estimation 3 500");
    }

    // ── Écriture du journal en échec : le contexte de l'appelant reste sain ─────

    /// <summary>
    /// Relecture du 22/09/2026 : la ligne du journal s'ajoute au contexte de l'APPELANT (le cycle
    /// de détection des accidents en partage un seul entre tous ses candidats). Si son INSERT
    /// échoue — ici la table manque, comme sur un pod déployé avant la migration 052 — l'erreur
    /// remonte à l'appelant, mais la ligne ne doit PAS rester « à insérer » : chaque
    /// sauvegarde suivante de ce contexte la rejouerait et échouerait à son tour.
    /// </summary>
    [Theory]
    [InlineData(AiFeatures.AccidentNarrative)]
    [InlineData(AiFeatures.InvoiceScan)]
    public async Task Une_ecriture_du_journal_en_echec_ne_laisse_rien_en_attente_dans_le_contexte_de_l_appelant(string feature)
    {
        using var ctx = await SeedAsync();
        var journal = feature == AiFeatures.InvoiceScan ? typeof(InvoiceScanLog) : typeof(AiUsageLog);
        var table = ctx.Model.FindEntityType(journal)!.GetTableName();
        await ctx.Database.ExecuteSqlRawAsync($"DROP TABLE \"{table}\"");

        var act = () => AiCredit.LogUsageAsync(ctx, CompanyId, null, feature, 1_700);

        await act.Should().ThrowAsync<DbUpdateException>("l'appelant décide de l'erreur : réponse en erreur, ou simple journal pour le récit d'accident");
        ctx.ChangeTracker.Entries().Should().NotContain(e => e.State == EntityState.Added, "la ligne refusée sort du suivi");

        // Écriture suivante de l'appelant (le récit posé sur l'accident, l'accident suivant du
        // cycle, sa notification) : elle passe.
        ctx.Societes.Single(s => s.Id == OtherCompanyId).Name = "SICOAC (récit enregistré)";
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        ctx.Societes.Single(s => s.Id == OtherCompanyId).Name.Should().Be("SICOAC (récit enregistré)");
    }
}
