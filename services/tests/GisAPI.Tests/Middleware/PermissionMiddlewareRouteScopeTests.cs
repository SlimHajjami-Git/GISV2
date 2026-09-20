using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// Les tables du middleware doivent viser les VRAIES routes, et un passe-droit ne doit jamais
/// dépasser ce qu'il ouvre. Deux trous constatés par la recette du 16/09/2026 :
///
///   • le passe-droit « GET libre » des données de référence comparait un simple PRÉFIXE et
///     rendait la main AVANT le contrôle d'abonnement : « /api/vehiclestops » passait pour une
///     sous-route de « /api/vehicles », et Suivi comme Géofences restaient lisibles sur un plan
///     qui ne les vend pas (seule la garde Angular les masquait) ;
///   • la clé « /api/fleetmanagement » ne correspondait à aucun chemin servi — le contrôleur est
///     routé « api/fleet » — donc tout le module Gestion de flotte échappait aux deux contrôles.
///
/// Les chemins Gestion de flotte sont lus sur les attributs du contrôleur : si sa route change
/// sans que la table du middleware suive, ces tests échouent au lieu de valider un chemin mort.
/// </summary>
public class PermissionMiddlewareRouteScopeTests
{
    private const int CompanyId = 7;
    private const int UserId = 51;

    /// <summary>Abonnement « Plan Basique » de la recette : ni Suivi, ni Géofences, ni Gestion de flotte.</summary>
    private static async Task<TestGisDbContext> SeedAsync(
        Action<SubscriptionType>? subscription = null,
        Action<User>? user = null,
        bool companyAdmin = false)
    {
        var ctx = TestDbContextFactory.Create();
        var plan = TestDataBuilder.CreateSubscriptionType();
        plan.Name = "Plan Basique";
        subscription?.Invoke(plan);
        ctx.SubscriptionTypes.Add(plan);
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Roles.Add(new Role { Id = 1, Name = companyAdmin ? "Admin" : "Employé", SocieteId = CompanyId, IsCompanyAdmin = companyAdmin });
        var employee = TestDataBuilder.CreateUser(id: UserId, companyId: CompanyId, email: "employe@test.com");
        user?.Invoke(employee);
        ctx.Users.Add(employee);
        // Même raison que PermissionMiddlewareMaintenanceTests : TestGisDbContext relie
        // User.Societe par une clé fantôme, sans elle l'Include du middleware répond 401.
        var societeFk = ctx.Model.FindEntityType(typeof(User))!
            .FindNavigation(nameof(User.Societe))!.ForeignKey.Properties.Single();
        ctx.Entry(employee).Property(societeFk.Name).CurrentValue = CompanyId;
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
            new[] { new Claim(ClaimTypes.NameIdentifier, (userId ?? UserId).ToString()), new Claim("companyId", CompanyId.ToString()) },
            authenticationType: "Bearer"));
        http.Response.Body = new MemoryStream();

        var reached = false;
        var middleware = new PermissionMiddleware(_ => { reached = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(http, ctx);

        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        return new Outcome(http.Response.StatusCode, reached, body);
    }

    // ── DEF-001 : le passe-droit « GET libre » ─────────────────────────────────

    [Theory]
    [InlineData("/api/vehiclestops")]              // module Suivi, jamais une sous-route de /api/vehicles
    [InlineData("/api/vehicles/with-positions")]   // module Suivi, sous-route de /api/vehicles
    public async Task Sans_le_module_Suivi_la_lecture_est_bloquee_par_l_abonnement(string path)
    {
        await using var ctx = await SeedAsync(s => s.ModuleMonitoring = false);

        var outcome = await SendAsync(ctx, "GET", path);

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain("SUBSCRIPTION_MODULE_BLOCKED");
    }

    [Fact]
    public async Task Sans_le_module_Geofences_la_lecture_des_zones_est_bloquee_par_l_abonnement()
    {
        await using var ctx = await SeedAsync(s => s.ModuleGeofences = false);

        var outcome = await SendAsync(ctx, "GET", "/api/geofences");

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain("SUBSCRIPTION_MODULE_BLOCKED");
    }

    [Fact]
    public async Task Un_admin_de_societe_garde_tout_ce_que_son_abonnement_contient()
    {
        await using var ctx = await SeedAsync(
            s => { s.ModuleMonitoring = true; s.ModuleGeofences = true; s.ModuleFleetManagement = true; },
            companyAdmin: true);

        foreach (var path in new[] { "/api/vehicles", "/api/vehicles/with-positions", "/api/vehiclestops", "/api/geofences", "/api/fleet/departments" })
        {
            var outcome = await SendAsync(ctx, "GET", path);
            outcome.ReachedController.Should().BeTrue($"GET {path} est dans l'abonnement");
            outcome.StatusCode.Should().Be(StatusCodes.Status200OK);
        }
    }

    [Fact]
    public async Task La_lecture_des_donnees_de_reference_reste_ouverte_quand_le_module_est_vendu()
    {
        // Le passe-droit ne portait QUE sur la permission utilisateur : il doit continuer à
        // ouvrir la liste des véhicules, des chauffeurs et des employés à un compte qui n'a
        // aucune de ces cases — beaucoup d'écrans en ont besoin.
        await using var ctx = await SeedAsync(user: u =>
        {
            u.CanVehicles = false;
            u.CanDrivers = false;
        });

        foreach (var path in new[] { "/api/vehicles", "/api/vehicles/1", "/api/drivers", "/api/employees" })
        {
            var outcome = await SendAsync(ctx, "GET", path);
            outcome.ReachedController.Should().BeTrue($"GET {path} reste une donnée de référence partagée");
        }
    }

    [Fact]
    public async Task L_ecriture_sur_une_donnee_de_reference_exige_toujours_la_permission()
    {
        await using var ctx = await SeedAsync(user: u => u.CanVehicles = false);

        var outcome = await SendAsync(ctx, "POST", "/api/vehicles");

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.Body.Should().Contain("USER_PERMISSION_DENIED");
    }

    [Fact]
    public async Task Le_passe_droit_de_lecture_cede_devant_la_regle_qui_vise_la_sous_route()
    {
        // Module Suivi vendu, mais l'utilisateur n'a pas la case Suivi. « /api/vehicles/with-positions »
        // EST une sous-route de « /api/vehicles » : la frontière de segment ne suffisait donc pas, le
        // passe-droit annulait encore la règle CanMonitoring que la table déclare pour ce chemin.
        await using var ctx = await SeedAsync(
            s => s.ModuleMonitoring = true,
            u => { u.CanMonitoring = false; u.CanVehicles = false; });

        var suivi = await SendAsync(ctx, "GET", "/api/vehicles/with-positions");
        var reference = await SendAsync(ctx, "GET", "/api/vehicles");

        suivi.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        suivi.ReachedController.Should().BeFalse("le Suivi temps réel n'est pas une donnée de référence partagée");
        suivi.Body.Should().Contain("USER_PERMISSION_DENIED");
        reference.ReachedController.Should().BeTrue("la liste des véhicules, elle, reste ouverte en lecture");
    }

    [Theory]
    [InlineData("GET", "/api/vehicles", true)]
    [InlineData("GET", "/api/vehicles/1", true)]
    [InlineData("GET", "/api/drivers", true)]
    [InlineData("GET", "/api/employees", true)]
    [InlineData("GET", "/api/geofences", true)]
    [InlineData("GET", "/api/vehicles/with-positions", false)] // règle Suivi, plus précise
    [InlineData("GET", "/api/vehiclestops", false)]            // pas une sous-route
    [InlineData("POST", "/api/vehicles", false)]               // l'écriture n'a jamais été ouverte
    public void Le_passe_droit_n_est_accorde_que_si_aucune_regle_ne_vise_le_chemin_plus_precisement(
        string method, string path, bool attendu)
        => PermissionMiddleware.IsSharedReferenceRead(path, method).Should().Be(attendu);

    [Theory]
    [InlineData("/api/alertemails")] // aucune règle de permission ni d'abonnement ne vise ce chemin
    [InlineData("/api/vehicles")]    // lecture de référence partagée
    public async Task Le_jeton_d_un_utilisateur_supprime_est_refuse_meme_sans_regle_sur_le_chemin(string path)
    {
        // Le jeton reste valide jusqu'à son expiration : c'est le chargement de l'utilisateur par le
        // middleware qui le refuse. Ni l'absence de règle ni le passe-droit de lecture ne l'en dispensent.
        await using var ctx = await SeedAsync();

        var outcome = await SendAsync(ctx, "GET", path, userId: 9999);

        outcome.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain("Utilisateur introuvable");
    }

    [Theory]
    [InlineData("/api/vehicles", "/api/vehicles", true)]
    [InlineData("/api/vehicles/1", "/api/vehicles", true)]
    [InlineData("/api/vehicles/with-positions", "/api/vehicles", true)]
    [InlineData("/api/vehiclestops", "/api/vehicles", false)]
    [InlineData("/api/vehicleassignments", "/api/vehicles", false)]
    [InlineData("/api/geofencesxyz", "/api/geofences", false)]
    public void Une_sous_route_se_reconnait_au_segment_pas_au_prefixe(string path, string route, bool attendu)
        => PermissionMiddleware.IsRouteOrSubRoute(path, route).Should().Be(attendu);

    // ── DEF-027 : la clé du middleware doit être la route réelle ───────────────

    /// <summary>Chemin réel d'une action de <see cref="FleetManagementController"/>.</summary>
    private static string FleetPath(string template) =>
        $"/{typeof(FleetManagementController).GetCustomAttribute<RouteAttribute>()!.Template}/{template}";

    [Fact]
    public async Task Sans_le_module_Gestion_de_flotte_la_vraie_route_du_controleur_est_bloquee()
    {
        await using var ctx = await SeedAsync(s => s.ModuleFleetManagement = false, companyAdmin: true);

        var outcome = await SendAsync(ctx, "GET", FleetPath("departments"));

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse("un module non vendu ne doit être lisible ni en GET ni en écriture");
        outcome.Body.Should().Contain("SUBSCRIPTION_MODULE_BLOCKED");
    }

    [Fact]
    public async Task Sans_le_module_Gestion_de_flotte_l_ecriture_est_bloquee_aussi()
    {
        await using var ctx = await SeedAsync(s => s.ModuleFleetManagement = false, companyAdmin: true);

        var outcome = await SendAsync(ctx, "POST", FleetPath("departments"));

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse();
    }

    [Fact]
    public async Task Avec_le_module_mais_sans_la_case_utilisateur_la_gestion_de_flotte_reste_refusee()
    {
        await using var ctx = await SeedAsync(
            s => s.ModuleFleetManagement = true,
            u => u.CanFleetManagement = false);

        var outcome = await SendAsync(ctx, "GET", FleetPath("departments"));

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.Body.Should().Contain("USER_PERMISSION_DENIED");
    }

    // ── Même panne, module Sinistres (revue du 20/09/2026) ─────────────────────
    //
    // AccidentClaimsController a été remplacé par AccidentReportsController, routé
    // « api/accident-reports » : la clé « /api/accidentclaims » ne visait plus aucun
    // chemin servi, et les seize points d'entrée du module — liste, déclaration
    // manuelle, phases, pièces jointes — passaient les deux contrôles. Seule la
    // suppression portait sa propre garde. Chemin lu sur le contrôleur, comme pour
    // la gestion de flotte : sa route ne peut plus changer en silence.

    /// <summary>Chemin réel d'une action de <see cref="AccidentReportsController"/>.</summary>
    private static string SinistrePath(string template = "") =>
        $"/{typeof(AccidentReportsController).GetCustomAttribute<RouteAttribute>()!.Template}{template}";

    [Fact]
    public async Task Sans_le_module_Sinistres_la_vraie_route_du_controleur_est_bloquee()
    {
        await using var ctx = await SeedAsync(s => s.ModuleAccidents = false, companyAdmin: true);

        var outcome = await SendAsync(ctx, "GET", SinistrePath());

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain("SUBSCRIPTION_MODULE_BLOCKED");
    }

    [Fact]
    public async Task Avec_le_module_mais_sans_la_case_utilisateur_les_sinistres_restent_refuses()
    {
        await using var ctx = await SeedAsync(
            s => s.ModuleAccidents = true,
            u => u.CanAccidents = false);

        // La phase 5 écrit désormais dans Réparations : c'est l'écriture la plus coûteuse
        // que le trou laissait passer.
        var outcome = await SendAsync(ctx, "PATCH", SinistrePath("/42/repair"));

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain("USER_PERMISSION_DENIED");
    }

    [Fact]
    public async Task Avec_le_module_et_la_case_les_sinistres_restent_accessibles()
    {
        await using var ctx = await SeedAsync(
            s => s.ModuleAccidents = true,
            u => u.CanAccidents = true);

        var outcome = await SendAsync(ctx, "GET", SinistrePath());

        outcome.ReachedController.Should().BeTrue();
        outcome.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
