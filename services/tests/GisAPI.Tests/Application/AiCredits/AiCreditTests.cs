using FluentAssertions;
using GisAPI.Application.Features.AiCredits;
using Xunit;

namespace GisAPI.Tests.Application.AiCredits;

/// <summary>
/// Crédit IA mensuel du scan de factures, en jetons (demande de Slim du 22/09/2026) —
/// les fonctions PURES qui décident du budget et de ce qu'affiche la barre. Chaque règle
/// a son test : retirer la règle fait tomber le test.
/// </summary>
public class AiCreditTests
{
    private static readonly DateTime Recharge = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    // ── Budget effectif ────────────────────────────────────────────────────────

    [Fact]
    public void Sans_aucun_reglage_le_budget_est_le_defaut_plateforme_de_60000_jetons()
    {
        AiCredit.EffectiveBudget(null, null).Should().Be(60_000);
        AiCredit.DefaultMonthlyTokens.Should().Be(60_000, "≈ 20 scans à 3 000 jetons, l'ancien défaut");
    }

    [Fact]
    public void Le_credit_en_jetons_fait_foi_meme_quand_un_ancien_quota_existe()
    {
        AiCredit.EffectiveBudget(10_000, 50).Should().Be(10_000);
    }

    [Fact]
    public void Zero_jeton_desactive_la_fonction_meme_avec_un_ancien_quota()
    {
        AiCredit.EffectiveBudget(0, 50).Should().Be(0);
    }

    [Fact]
    public void L_ancien_quota_en_scans_est_converti_a_3000_jetons_par_scan()
    {
        // Relu tel quel comme des jetons, « 50 scans » serait devenu 50 jetons : zéro scan.
        AiCredit.EffectiveBudget(null, 50).Should().Be(150_000);
        AiCredit.EffectiveBudget(null, 1).Should().Be(3_000);
    }

    [Fact]
    public void L_ancien_quota_a_zero_reste_desactive_apres_conversion()
    {
        AiCredit.EffectiveBudget(null, 0).Should().Be(0);
    }

    [Fact]
    public void La_conversion_ne_deborde_pas_et_reste_sous_le_plafond_du_reglage()
    {
        // L'ancien écran acceptait jusqu'à 100 000 scans : converti sans borne, ce budget
        // (300 millions) dépassait le plafond du réglage et ne pouvait plus être ré-enregistré.
        AiCredit.EffectiveBudget(null, 100_000).Should().Be(AiCredit.MaxMonthlyTokens);
        AiCredit.EffectiveBudget(null, int.MaxValue).Should().Be(AiCredit.MaxMonthlyTokens);
    }

    [Fact]
    public void Un_ancien_quota_de_5000_scans_donne_10_millions_de_jetons_pile_au_plafond()
    {
        AiCredit.EffectiveBudget(null, 5_000).Should().Be(10_000_000);
        // Juste sous le plafond : la conversion reste exacte.
        AiCredit.EffectiveBudget(null, 3_333).Should().Be(9_999_000);
    }

    // ── Ombre écrite dans l'ancienne colonne (retour arrière du pod) ─────────

    [Fact]
    public void Ombre_retour_au_defaut_rend_null()
    {
        AiCredit.LegacyScanLimitShadow(null).Should().BeNull();
    }

    [Fact]
    public void Ombre_d_une_societe_desactivee_reste_zero_pour_que_l_ancien_code_la_garde_fermee()
    {
        // NULL vaudrait « 20 scans par défaut » pour l'ancien pod : le scan serait rouvert.
        AiCredit.LegacyScanLimitShadow(0).Should().Be(0);
        AiCredit.LegacyScanLimitShadow(-5).Should().Be(0);
    }

    [Fact]
    public void Ombre_d_un_credit_positif_equivalent_en_scans_au_moins_un()
    {
        AiCredit.LegacyScanLimitShadow(60_000).Should().Be(20);
        AiCredit.LegacyScanLimitShadow(150_000).Should().Be(50);
        AiCredit.LegacyScanLimitShadow(10_000_000).Should().Be(3_333);
        // Moins d'un scan moyen : 1, jamais 0 (qui voudrait « désactivé » pour l'ancien code).
        AiCredit.LegacyScanLimitShadow(1_000).Should().Be(1);
    }

    [Fact]
    public void Un_reglage_negatif_ne_donne_jamais_un_budget_negatif()
    {
        AiCredit.EffectiveBudget(-5, null).Should().Be(0);
        AiCredit.EffectiveBudget(null, -5).Should().Be(0);
    }

    // ── Jetons décomptés par scan ─────────────────────────────────────────────

    [Fact]
    public void Un_scan_compte_ses_jetons_reels()
    {
        AiCredit.ChargedTokens(AiFeatures.InvoiceScan, 2_741).Should().Be(2_741);
    }

    [Fact]
    public void Un_scan_sans_consommation_connue_compte_pour_le_cout_moyen_et_jamais_gratuit()
    {
        AiCredit.ChargedTokens(AiFeatures.InvoiceScan, 0).Should().Be(3_000);
    }

    // ── État affiché ──────────────────────────────────────────────────────────

    [Fact]
    public void Etat_courant_pourcentage_reste_et_scans_estimes()
    {
        var s = AiCredit.Compute(60_000, 25_200, 9, Recharge);

        s.Enabled.Should().BeTrue();
        s.BudgetTokens.Should().Be(60_000);
        s.UsedTokens.Should().Be(25_200);
        s.RemainingTokens.Should().Be(34_800);
        s.PercentUsed.Should().Be(42);
        s.ScansThisMonth.Should().Be(9);
        s.EstimatedScansLeft.Should().Be(11, "34 800 / 3 000 = 11,6 arrondi vers le bas");
        s.ResetsAt.Should().Be(Recharge);
    }

    [Fact]
    public void Le_pourcentage_est_arrondi_vers_le_bas_pour_ne_pas_griser_le_bouton_trop_tot()
    {
        // Arrondi au plus proche : 99,998 % → « 100 % » et bouton grisé alors qu'il reste
        // un jeton et que le serveur accepte encore un scan.
        var s = AiCredit.Compute(60_000, 59_999, 20, Recharge);

        s.PercentUsed.Should().Be(99);
        s.RemainingTokens.Should().Be(1);
    }

    [Fact]
    public void Credit_epuise_100_pourcent_et_plus_rien_a_consommer()
    {
        var s = AiCredit.Compute(60_000, 60_000, 20, Recharge);

        s.PercentUsed.Should().Be(100);
        s.RemainingTokens.Should().Be(0);
        s.EstimatedScansLeft.Should().Be(0);
        s.Enabled.Should().BeTrue("épuisé n'est pas désactivé");
    }

    [Fact]
    public void Le_dernier_scan_qui_depasse_le_budget_plafonne_la_barre_a_100()
    {
        var s = AiCredit.Compute(60_000, 63_500, 21, Recharge);

        s.PercentUsed.Should().Be(100);
        s.RemainingTokens.Should().Be(0);
    }

    [Fact]
    public void Budget_nul_fonction_fermee_rien_de_disponible()
    {
        var s = AiCredit.Compute(0, 0, 0, Recharge);

        s.Enabled.Should().BeFalse();
        s.RemainingTokens.Should().Be(0);
        s.PercentUsed.Should().Be(100, "invariant : 100 % si et seulement s'il ne reste rien");
        s.EstimatedScansLeft.Should().Be(0);
    }

    [Fact]
    public void Le_pourcentage_ne_deborde_pas_sur_un_gros_budget()
    {
        // used * 100 dépasse int.MaxValue au-delà de 21,4 millions de jetons consommés.
        var s = AiCredit.Compute(int.MaxValue, 2_000_000_000, 1, Recharge);

        s.PercentUsed.Should().Be(93);
    }

    // ── Mois civil ────────────────────────────────────────────────────────────

    [Fact]
    public void Le_credit_se_recharge_le_1er_du_mois_suivant_a_minuit_UTC()
    {
        var now = new DateTime(2026, 9, 22, 14, 30, 0, DateTimeKind.Utc);

        AiCredit.MonthStart(now).Should().Be(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        AiCredit.NextReset(now).Should().Be(Recharge);
    }

    [Fact]
    public void En_decembre_la_recharge_tombe_le_1er_janvier_de_l_annee_suivante()
    {
        AiCredit.NextReset(new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc))
            .Should().Be(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    // ── Toute l'IA : estimation d'un appel muet, par fonction ─────────────────

    [Theory]
    [InlineData(AiFeatures.InvoiceScan, 3_000)]
    [InlineData(AiFeatures.AssistantChat, 2_500)]
    [InlineData(AiFeatures.VehicleCompare, 3_000)]
    [InlineData(AiFeatures.VehicleReport, 3_500)]
    [InlineData(AiFeatures.FleetReport, 5_000)]
    [InlineData(AiFeatures.FleetReportAsk, 3_500)]
    [InlineData(AiFeatures.ConsumptionExplain, 1_000)]
    [InlineData(AiFeatures.AccidentNarrative, 2_500)]
    [InlineData("fonction_future", 2_500)]
    public void Un_appel_sans_consommation_connue_compte_une_estimation_de_sa_fonction_jamais_zero(string fonction, int attendu)
    {
        AiCredit.ChargedTokens(fonction, 0).Should().Be(attendu);
        AiCredit.ChargedTokens(fonction, -1).Should().Be(attendu, "une valeur négative n'est pas une consommation");
        AiCredit.ChargedTokens(fonction, 1_234).Should().Be(1_234, "une consommation rendue fait foi");
    }

    [Fact]
    public void La_ventilation_vide_liste_toutes_les_fonctions_a_zero()
    {
        var vide = AiCredit.EmptyBreakdown();

        vide.Keys.Should().Equal(AiFeatures.All);
        vide.Values.Should().OnlyContain(v => v == 0);
        AiFeatures.All.Should().BeEquivalentTo(new[]
        {
            "invoice_scan", "assistant_chat", "vehicle_compare", "vehicle_report",
            "fleet_report", "fleet_report_ask", "consumption_explain", "accident_narrative"
        }, "ces clés sont écrites en base (ai_usage_logs.feature) et lues par l'écran");
        AiFeatures.All.Should().OnlyContain(f => f.Length <= 32, "colonne feature varchar(32)");
    }

    // ── Refus avant un appel payant ───────────────────────────────────────────

    [Fact]
    public void IA_desactivee_refus_403_AI_CREDIT_DISABLED_avec_le_credit()
    {
        var credit = AiCredit.Compute(0, 0, 0, Recharge);

        var refus = AiCredit.Refusal(credit);

        refus.Should().NotBeNull();
        refus!.StatusCode.Should().Be(403);
        refus.Code.Should().Be("AI_CREDIT_DISABLED");
        refus.Message.Should().Be("Les fonctions d'IA ne sont pas activées pour votre société.");
        refus.Credit.Should().BeSameAs(credit);
    }

    [Fact]
    public void Credit_epuise_refus_429_AI_CREDIT_EXHAUSTED_avec_la_date_de_recharge()
    {
        var credit = AiCredit.Compute(60_000, 61_000, 0, Recharge);

        var refus = AiCredit.Refusal(credit);

        refus.Should().NotBeNull();
        refus!.StatusCode.Should().Be(429);
        refus.Code.Should().Be("AI_CREDIT_EXHAUSTED");
        refus.Message.Should().Be(
            "Crédit IA du mois épuisé (100 %). Il se recharge le 01/10/2026 ; votre administrateur peut l'augmenter.");
        refus.Credit.PercentUsed.Should().Be(100);
    }

    [Fact]
    public void Tant_qu_il_reste_un_jeton_aucun_refus()
    {
        AiCredit.Refusal(AiCredit.Compute(60_000, 59_999, 0, Recharge)).Should().BeNull();
    }

    [Fact]
    public void Un_refus_de_credit_n_est_jamais_une_erreur_500()
    {
        // Un chemin qui ne connaîtrait pas l'exception la rendrait au pire en 400 (DomainException).
        AiCredit.Refusal(AiCredit.Compute(0, 0, 0, Recharge))
            .Should().BeAssignableTo<GisAPI.Domain.Exceptions.DomainException>();
    }
}
