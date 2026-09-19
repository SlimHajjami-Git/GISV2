using System.Security.Claims;
using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// Le scan de facture IA est gouverné par le QUOTA DE LA SOCIÉTÉ, pas par le module
/// Dépenses (décision de Karim du 19/09/2026).
///
/// <para>Constat du 19/09/2026 : le scan, jusque-là propre à l'écran Dépenses, est devenu
/// une brique partagée posée sur Carburant, Entretien effectué, Nouvelle réparation et
/// Échéances. Or ses deux routes vivent sous « /api/costs/… » et
/// <see cref="PermissionMiddleware"/> apparie PAR PRÉFIXE : la clé « /api/costs » leur
/// imposait la case utilisateur Dépenses ET l'abonnement Dépenses. Un client abonné à
/// Carburant mais pas à Dépenses, ou un utilisateur sans cette case, voyait le bouton sur
/// ces quatre écrans et se faisait refuser par un message parlant d'un AUTRE module.</para>
///
/// <para>Ce que ces tests fixent : le scan passe sans la case ni l'abonnement Dépenses,
/// l'écran Dépenses lui-même reste gardé exactement comme avant, et l'ouverture ne
/// court-circuite AUCUN des contrôles de société (un jeton d'utilisateur supprimé est
/// toujours refusé — le scan coûte un appel payant à l'IA sur le quota de la société).</para>
/// </summary>
public class ScanFactureAccesTests
{
    private const int CompanyId = 7;
    private const int UserId = 51;

    /// <summary>Les deux routes du scan, telles que le front les appelle.</summary>
    private const string CheminQuota = "/api/costs/scan-quota";
    private const string CheminScan = "/api/costs/scan-invoice";

    private static async Task<TestGisDbContext> SeedAsync(
        Action<SubscriptionType>? subscription = null,
        Action<User>? user = null)
    {
        var ctx = TestDbContextFactory.Create();
        var plan = TestDataBuilder.CreateSubscriptionType();
        plan.Name = "Plan Carburant";
        subscription?.Invoke(plan);
        ctx.SubscriptionTypes.Add(plan);
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Roles.Add(new Role { Id = 1, Name = "Employé", SocieteId = CompanyId, IsCompanyAdmin = false });
        var employe = TestDataBuilder.CreateUser(id: UserId, companyId: CompanyId, email: "employe@test.com");
        user?.Invoke(employe);
        ctx.Users.Add(employe);
        // Même raison que PermissionMiddlewareRouteScopeTests : TestGisDbContext relie
        // User.Societe par une clé fantôme, sans elle l'Include du middleware répond 401.
        var societeFk = ctx.Model.FindEntityType(typeof(User))!
            .FindNavigation(nameof(User.Societe))!.ForeignKey.Properties.Single();
        ctx.Entry(employe).Property(societeFk.Name).CurrentValue = CompanyId;
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private sealed record Outcome(int StatusCode, bool ReachedController, string Body);

    private static async Task<Outcome> SendAsync(TestGisDbContext ctx, string method, string path, int? userId = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, (userId ?? UserId).ToString()),
                new Claim("companyId", CompanyId.ToString())
            },
            authenticationType: "Bearer"));
        http.Response.Body = new MemoryStream();

        var reached = false;
        var middleware = new PermissionMiddleware(_ => { reached = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(http, ctx);

        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        return new Outcome(http.Response.StatusCode, reached, body);
    }

    // ── Ce que le scan ne doit plus exiger ─────────────────────────────────────

    [Theory]
    [InlineData("GET", CheminQuota)]
    [InlineData("POST", CheminScan)]
    public async Task Un_utilisateur_sans_le_droit_Depenses_peut_scanner(string methode, string chemin)
    {
        // CanCosts vaut false par défaut sur un compte neuf : c'est très exactement le
        // magasinier qui saisit des pleins sans avoir accès aux dépenses.
        await using var ctx = await SeedAsync(user: u => u.CanCosts = false);

        var outcome = await SendAsync(ctx, methode, chemin);

        outcome.ReachedController.Should().BeTrue("le scan ne dépend plus de la case Dépenses");
        outcome.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("GET", CheminQuota)]
    [InlineData("POST", CheminScan)]
    public async Task Une_societe_sans_l_abonnement_Depenses_peut_scanner(string methode, string chemin)
    {
        // Abonnement « Carburant » : le module Dépenses n'est pas vendu, mais la société a
        // bien un quota de scans IA — c'est lui, et lui seul, qui ouvre la fonction.
        await using var ctx = await SeedAsync(
            s => s.ModuleCosts = false,
            u => u.CanCosts = true);

        var outcome = await SendAsync(ctx, methode, chemin);

        outcome.ReachedController.Should().BeTrue("le scan ne dépend plus de l'abonnement Dépenses");
        outcome.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("GET", CheminQuota)]
    [InlineData("POST", CheminScan)]
    public async Task Ni_la_case_ni_l_abonnement_Depenses_ne_sont_demandes(string methode, string chemin)
    {
        await using var ctx = await SeedAsync(s => s.ModuleCosts = false, u => u.CanCosts = false);

        var outcome = await SendAsync(ctx, methode, chemin);

        outcome.ReachedController.Should().BeTrue();
        outcome.Body.Should().BeEmpty("aucun refus n'est écrit avant le contrôleur");
    }

    // ── Ce qui ne doit surtout pas sauter au passage ───────────────────────────

    [Fact]
    public async Task L_ecran_Depenses_lui_reste_garde_par_la_case_utilisateur()
    {
        await using var ctx = await SeedAsync(user: u => u.CanCosts = false);

        var outcome = await SendAsync(ctx, "GET", "/api/costs");

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain("USER_PERMISSION_DENIED");
    }

    [Fact]
    public async Task L_ecran_Depenses_lui_reste_garde_par_l_abonnement()
    {
        await using var ctx = await SeedAsync(s => s.ModuleCosts = false, u => u.CanCosts = true);

        var outcome = await SendAsync(ctx, "POST", "/api/costs");

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain("SUBSCRIPTION_MODULE_BLOCKED");
    }

    [Theory]
    [InlineData("GET", CheminQuota)]
    [InlineData("POST", CheminScan)]
    public async Task Le_jeton_d_un_utilisateur_supprime_ne_peut_pas_scanner(string methode, string chemin)
    {
        // L'ouverture passe par une règle de la table, PAS par _skipRoutes : le middleware
        // charge toujours l'utilisateur, donc un jeton encore valide d'un compte supprimé
        // ne peut pas consommer le quota d'IA — ni l'argent — de la société.
        await using var ctx = await SeedAsync();

        var outcome = await SendAsync(ctx, methode, chemin, userId: 9999);

        outcome.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain("Utilisateur introuvable");
    }

    [Theory]
    [InlineData("GET", CheminQuota)]
    [InlineData("POST", CheminScan)]
    public void Le_scan_passe_par_les_controles_de_societe_et_non_par_une_route_toujours_ouverte(
        string methode, string chemin)
        => PermissionMiddleware.ClassifyRoute(chemin, methode)
            .Should().Be(PermissionMiddleware.RouteGate.TenantChecks);

    // ── Les tables elles-mêmes ─────────────────────────────────────────────────

    [Theory]
    [InlineData(CheminQuota, PermissionMiddleware.PermissionNonRequise)]
    [InlineData(CheminScan, PermissionMiddleware.PermissionNonRequise)]
    [InlineData("/api/costs", "CanCosts")]
    [InlineData("/api/costs/812", "CanCosts")]
    [InlineData("/api/costs/summary", "CanCosts")]
    public void La_cle_la_plus_longue_decide_du_droit_exige(string chemin, string attendu)
        => PermissionMiddleware.RequiredUserPermission(chemin).Should().Be(attendu);

    // ── L'ouverture s'arrête à la frontière de segment (relecture du 19/09/2026) ──
    //
    // Les deux clés du scan OUVRENT un accès, et ce fichier même porte la leçon de la
    // recette du 16/09/2026 : « une règle qui OUVRE un accès doit s'arrêter à la frontière
    // de segment ». Appariées par simple préfixe, elles auraient offert leur ouverture à
    // toute route future commençant par leur nom.

    /// <summary>Route qui n'existe pas encore et commence par une clé ouvrante.</summary>
    private const string CheminFutur = "/api/costs/scan-quota-detaillee";

    [Theory]
    [InlineData("/api/costs/scan-quota-detaillee")]
    [InlineData("/api/costs/scan-invoice-par-lot")]
    public void Une_route_future_n_herite_pas_de_l_ouverture_du_scan(string chemin)
        => PermissionMiddleware.RequiredUserPermission(chemin)
            .Should().Be("CanCosts", "elle retombe sur « /api/costs », qui la garde");

    [Fact]
    public async Task Une_route_future_reste_gardee_par_l_abonnement_Depenses()
    {
        // Le contrôle d'abonnement passe par la même table : sans frontière de segment,
        // « /api/costs/scan-quota-detaillee » aurait hérité du « _ => true » du scan.
        await using var ctx = await SeedAsync(s => s.ModuleCosts = false, u => u.CanCosts = true);

        var outcome = await SendAsync(ctx, "GET", CheminFutur);

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain("SUBSCRIPTION_MODULE_BLOCKED");
    }

    [Fact]
    public async Task Une_route_future_reste_gardee_par_la_case_Depenses()
    {
        await using var ctx = await SeedAsync(user: u => u.CanCosts = false);

        var outcome = await SendAsync(ctx, "GET", CheminFutur);

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.Body.Should().Contain("USER_PERMISSION_DENIED");
    }

    [Theory]
    [InlineData("/api/costs/scan-quota")]
    [InlineData("/api/costs/scan-invoice")]
    public async Task Le_scan_lui_meme_passe_toujours(string chemin)
    {
        // Le cas limite dans l'autre sens : resserrer l'appariement ne doit pas refermer
        // les deux routes du scan, qui sont la clé EXACTE.
        await using var ctx = await SeedAsync(s => s.ModuleCosts = false, u => u.CanCosts = false);

        var outcome = await SendAsync(ctx, "GET", chemin);

        outcome.ReachedController.Should().BeTrue();
    }

    [Theory]
    // Une clé qui RESTREINT garde le préfixe : elle ne peut qu'ajouter du contrôle, et
    // l'arrêter à la frontière de segment ouvrirait des sous-routes aujourd'hui gardées.
    [InlineData("/api/costs/812", "CanCosts")]
    [InlineData("/api/costs-archives", "CanCosts")]
    [InlineData("/api/reports/costs/ranking-par-vehicule", "CanReportCostRanking")]
    [InlineData("/api/vehicles/with-positions/38", "CanMonitoring")]
    public void Les_cles_qui_restreignent_gardent_le_prefixe(string chemin, string attendu)
        => PermissionMiddleware.RequiredUserPermission(chemin).Should().Be(attendu);

    [Fact]
    public void Toute_cle_qui_n_exige_aucune_case_est_declaree_ouvrante()
    {
        // Le filet qui tient les deux listes ensemble : une clé ajoutée à la table sans
        // être déclarée ouvrante retomberait au simple préfixe, en silence.
        PermissionMiddleware.ClesSansPermissionDeModule.Should().NotBeEmpty();
        PermissionMiddleware.ClesSansPermissionDeModule
            .Should().OnlyContain(cle => PermissionMiddleware.EstCleOuvrante(cle));
    }

    [Fact]
    public void La_permission_non_requise_est_accordee_a_un_compte_sans_aucune_case()
    {
        var sansRien = TestDataBuilder.CreateUser(id: UserId, companyId: CompanyId);

        PermissionMiddleware.IsGranted(sansRien, PermissionMiddleware.PermissionNonRequise).Should().BeTrue();
        PermissionMiddleware.IsGranted(sansRien, "CanCosts").Should().BeFalse("le module Dépenses, lui, s'accorde toujours case par case");
    }
}
