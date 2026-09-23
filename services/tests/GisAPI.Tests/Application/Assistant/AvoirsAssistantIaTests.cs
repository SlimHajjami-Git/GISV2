using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Assistant;

/// <summary>
/// Règle des avoirs et remboursements du 18/09/2026, appliquée à l'ASSISTANT IA :
/// postes BRUTS, ligne « Avoirs et remboursements » à part en négatif, total net —
/// la convention des rapports détaillés, puisque c'est de rapports que le client
/// parle quand il interroge l'assistant.
///
/// <para>Constat : l'assistant avait sa PROPRE convention, la troisième de
/// l'application. <c>totalOtherCost</c> valait la catégorie « Autres » SIGNÉE, donc
/// nette des crédits, mais la phrase envoyée au modèle n'étiquetait que le total
/// (« Coûts totaux (avoirs et remboursements déduits) … Autres: X »). Sur la société
/// de test, exercice 2026 — 745 de frais divers et 1 200 d'avoirs —, l'assistant
/// lisait « Autres: -455 » pendant que le rapport affichait « Autres 745 » et, en
/// dessous, « Avoirs et remboursements −1 200 ». Rien ne signalait l'écart : le
/// modèle énonçait au client un poste qu'aucun écran ne montre.</para>
///
/// <para>Les dépenses de ces tests vivent TOUTES dans <c>vehicle_costs</c> : c'est la
/// seule source que l'assistant lise, et la comparaison porte ici sur la convention
/// des avoirs. Un plein saisi dans <c>fuel_entries</c> ou une réparation de la table
/// <c>repairs</c> compte au rapport et pas à l'assistant — écart de SOURCES, distinct
/// de celui-ci, relevé à part.</para>
/// </summary>
public class AvoirsAssistantIaTests
{
    private const int CompanyId = 7;
    private const int AdminUserId = 1;

    // Chiffres réels de la société de test sur l'exercice 2026, tous portés en dépenses
    // pour que rapport et assistant lisent exactement les mêmes lignes.
    private const decimal Carburant = 29_481.78m;
    private const decimal Entretiens = 6_054.00m;
    private const decimal Reparations = 11_242.00m;
    private const decimal AutresBruts = 745.00m;   // 625 d'assurance + 120 d'amende
    private const decimal Avoirs = -1_200.00m;     // remboursement d'assurance, saisi en positif
    private const decimal TotalNet = 46_322.78m;

    /// <summary>Dépenses du mois écoulé : la fenêtre « month » de l'assistant remonte à 30 jours.</summary>
    private static async Task<TestGisDbContext> ParcAsync(bool avecAvoir = true)
    {
        var jour = DateTime.UtcNow.AddDays(-3);
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId, Status = "available" });
        ctx.VehicleCosts.AddRange(
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "fuel", Amount = Carburant, Liters = 1_200m, Date = jour },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "maintenance", Amount = Entretiens, Date = jour },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "repair", Amount = Reparations, Date = jour },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance", Amount = 625m, Date = jour },
            new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "amende", Amount = 120m, Date = jour });
        if (avecAvoir)
            ctx.VehicleCosts.Add(new VehicleCost { VehicleId = 1, CompanyId = CompanyId, Type = "insurance_refund", Amount = -Avoirs, Date = jour });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    /// <summary>Le rapport R1 « Coût d'exploitation réel » sur la même fenêtre que l'assistant.</summary>
    private static Task<OperatingCostReportDto> RapportAsync(TestGisDbContext ctx) =>
        new GetOperatingCostReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
            .Handle(new GetOperatingCostReportQuery(DateTime.UtcNow.AddDays(-30).Date, DateTime.UtcNow.Date), CancellationToken.None);

    private static async Task<(JsonElement Rapport, string Prompt)> AssistantAsync(TestGisDbContext ctx)
    {
        string? prompt = null;
        var llm = new Mock<ILlmService>();
        llm.Setup(l => l.ChatAsync(It.IsAny<string>(), It.IsAny<List<LlmMessage>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
           .Callback<string, List<LlmMessage>, int, CancellationToken>((system, _, _, _) => prompt = system)
           .ReturnsAsync(new LlmResponse("analyse", 1200));
        var sante = new Mock<IVehicleHealthScoreService>();
        sante.Setup(s => s.CalculateAllScoresAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new List<VehicleHealthResult>());

        // Crédit IA de la société (22/09/2026) : sans société connue, le rapport serait refusé.
        AiCreditTestData.EnsureSocieteAvecCredit(ctx, CompanyId);
        var controleur = new AiChatController(ctx, llm.Object, sante.Object, NullLogger<AiChatController>.Instance,
            TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = Admin() } }
        };
        var ok = (await controleur.GenerateFleetReport(new FleetReportRequest("month", null)))
            .Should().BeOfType<OkObjectResult>().Subject;

        prompt.Should().NotBeNull("le prompt doit partir au modèle");
        return (JsonSerializer.SerializeToElement(ok.Value), prompt!);
    }

    private static ClaimsPrincipal Admin() => new(new ClaimsIdentity(new[]
    {
        new Claim("companyId", CompanyId.ToString()),
        new Claim(ClaimTypes.NameIdentifier, AdminUserId.ToString()),
        new Claim(ClaimTypes.Role, "company_admin")
    }, "test"));

    // ───────────── 1. Les chiffres de l'assistant sont ceux du rapport ─────────────

    [Fact]
    public async Task Un_mois_avec_un_avoir_donne_a_l_assistant_les_memes_postes_que_le_rapport()
    {
        using var ctx = await ParcAsync();

        var rapport = await RapportAsync(ctx);
        var (reponse, _) = await AssistantAsync(ctx);

        // Le rapport, référence : postes bruts, ligne d'avoirs, total net.
        (rapport.TotalFuelCost, rapport.TotalMaintenanceCost, rapport.TotalRepairCost,
         rapport.TotalOtherCost, rapport.TotalCreditAmount, rapport.TotalCost)
            .Should().Be((Carburant, Entretiens, Reparations, AutresBruts, Avoirs, TotalNet));

        var repartition = reponse.GetProperty("charts").GetProperty("costBreakdown");
        decimal Part(string nom) => repartition.GetProperty(nom).GetDecimal();

        // L'assistant arrondit au dinar près ; à cela près, les mêmes chiffres que le rapport.
        Part("fuel").Should().Be(Math.Round(rapport.TotalFuelCost, 0));
        Part("maintenance").Should().Be(Math.Round(rapport.TotalMaintenanceCost, 0));
        Part("repairs").Should().Be(Math.Round(rapport.TotalRepairCost, 0));
        Part("other").Should().Be(Math.Round(rapport.TotalOtherCost, 0), "les frais divers restent bruts");
        Part("credits").Should().Be(-Math.Round(rapport.TotalCreditAmount, 0), "les avoirs ont leur propre part, en positif");
        reponse.GetProperty("fleetSummary").GetProperty("totalCosts").GetDecimal()
            .Should().Be(Math.Round(rapport.TotalCost, 0), "seul le total est net");

        // Aucun crédit compté deux fois : la légende retombe sur le total affiché.
        (Part("fuel") + Part("maintenance") + Part("repairs") + Part("other") - Part("credits"))
            .Should().Be(reponse.GetProperty("fleetSummary").GetProperty("totalCosts").GetDecimal());
    }

    // ───────────── 2. La phrase envoyée au modèle nomme chaque poste ─────────────

    [Fact]
    public async Task La_phrase_envoyee_au_modele_nomme_correctement_chaque_poste()
    {
        using var ctx = await ParcAsync();

        var (_, prompt) = await AssistantAsync(ctx);

        prompt.Should()
            .Contain($"Coûts totaux nets: {TotalNet:N0}")
            .And.Contain("postes bruts")
            .And.Contain($"Carburant: {Carburant:N0}")
            .And.Contain($"Entretiens: {Entretiens:N0}")
            .And.Contain($"Réparations: {Reparations:N0}")
            .And.Contain($"Autres: {AutresBruts:N0}", "« Autres » est le poste du rapport, pas un solde")
            .And.Contain($"Avoirs et remboursements: {Avoirs:N0} déjà déduits du total",
                "sans la mention, le modèle resoustrait l'avoir d'un total qui l'avait déjà déduit");
    }

    [Fact]
    public async Task Le_solde_net_d_avant_n_est_plus_annonce_comme_un_poste()
    {
        using var ctx = await ParcAsync();

        var (_, prompt) = await AssistantAsync(ctx);

        // 745 − 1 200 : le chiffre que l'assistant énonçait, absent de tous les écrans.
        prompt.Should().NotContain($"Autres: {AutresBruts + Avoirs:N0}")
            .And.NotContain("Coûts totaux (avoirs et remboursements déduits)");
    }

    /// <summary>
    /// Plafond Groq (8 000 jetons/minute) : la ligne d'avoirs n'est écrite que s'il y
    /// en a un. Une période sans crédit garde la phrase d'avant, aux intitulés près.
    /// </summary>
    [Fact]
    public async Task Sans_avoir_la_phrase_ne_porte_aucune_ligne_de_credit()
    {
        using var ctx = await ParcAsync(avecAvoir: false);

        var (reponse, prompt) = await AssistantAsync(ctx);

        prompt.Should().Contain($"Autres: {AutresBruts:N0}").And.NotContain("Avoirs et remboursements");
        reponse.GetProperty("charts").GetProperty("costBreakdown").GetProperty("credits").GetDecimal().Should().Be(0m);
    }
}
