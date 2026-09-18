using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using FluentAssertions;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// PermissionMiddleware associait le droit Maintenance à « /api/vehiclemaintenance » (sans
/// tiret) alors que <see cref="VehicleMaintenanceController"/> est routé
/// « api/vehicle-maintenance ». Le chemin n'étant que mis en minuscules avant StartsWith,
/// aucune requête ne correspondait : tout le contrôleur échappait au droit utilisateur
/// CanMaintenance ET au module d'abonnement Entretien, dont POST mark-done qui crée une
/// dépense et relève le kilométrage. Seuls [Authorize] et le filtre société s'appliquaient.
///
/// Ces tests passent par le VRAI middleware, avec des chemins lus sur les attributs du
/// contrôleur (aucun chemin recopié) : si sa route change sans que la table suive, ils
/// échouent au lieu de valider un chemin mort.
/// </summary>
public class PermissionMiddlewareMaintenanceTests
{
    private const int CompanyId = 14;
    private const int UserId = 51;

    /// <summary>Chemin réel d'une action : [Route] du contrôleur + gabarit de l'action, paramètres à 1.</summary>
    private static (string Method, string Path) Endpoint(MethodInfo action)
    {
        var prefix = typeof(VehicleMaintenanceController).GetCustomAttribute<RouteAttribute>()!.Template;
        var verb = action.GetCustomAttribute<HttpMethodAttribute>()!;
        var path = string.IsNullOrEmpty(verb.Template) ? $"/{prefix}" : $"/{prefix}/{verb.Template}";
        return (verb.HttpMethods.Single(), Regex.Replace(path, @"\{[^}]+\}", "1"));
    }

    private static (string Method, string Path) Endpoint(string actionName) =>
        Endpoint(typeof(VehicleMaintenanceController).GetMethod(actionName)!);

    public static IEnumerable<object[]> EveryAction() =>
        typeof(VehicleMaintenanceController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<HttpMethodAttribute>() != null)
            .Select(m => Endpoint(m))
            .Select(e => new object[] { e.Method, e.Path });

    private static async Task<TestGisDbContext> SeedAsync(
        Action<User>? user = null, bool companyAdmin = false, bool moduleMaintenance = true)
    {
        var ctx = TestDbContextFactory.Create();
        var subscription = TestDataBuilder.CreateSubscriptionType();
        subscription.ModuleMaintenance = moduleMaintenance;
        ctx.SubscriptionTypes.Add(subscription);
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Roles.Add(new Role { Id = 1, Name = companyAdmin ? "Admin" : "Employé", SocieteId = CompanyId, IsCompanyAdmin = companyAdmin });
        var employee = TestDataBuilder.CreateUser(id: UserId, companyId: CompanyId, email: "employe@test.com");
        user?.Invoke(employee);
        ctx.Users.Add(employee);
        // TestGisDbContext relie User.Societe par une clé fantôme (SocieteId) et non par CompanyId
        // comme UserConfiguration : sans elle, l'Include du middleware (jointure interne) perd
        // l'utilisateur et répond 401. On renseigne la clé que le modèle de test utilise réellement.
        var societeFk = ctx.Model.FindEntityType(typeof(User))!
            .FindNavigation(nameof(User.Societe))!.ForeignKey.Properties.Single();
        ctx.Entry(employee).Property(societeFk.Name).CurrentValue = CompanyId;
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private sealed record Outcome(int StatusCode, bool ReachedController, string Body);

    private static async Task<Outcome> SendAsync(TestGisDbContext ctx, (string Method, string Path) endpoint)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = endpoint.Method;
        http.Request.Path = endpoint.Path;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, UserId.ToString()), new Claim("companyId", CompanyId.ToString()) },
            authenticationType: "Bearer"));
        http.Response.Body = new MemoryStream();

        var reached = false;
        var middleware = new PermissionMiddleware(_ => { reached = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(http, ctx);

        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        return new Outcome(http.Response.StatusCode, reached, body);
    }

    [Fact]
    public async Task Sans_CanMaintenance_POST_mark_done_est_refuse_en_403()
    {
        await using var ctx = await SeedAsync(u => u.CanMaintenance = false);
        var markDone = Endpoint(nameof(VehicleMaintenanceController.MarkDone));
        markDone.Should().Be(("POST", "/api/vehicle-maintenance/mark-done"));

        var outcome = await SendAsync(ctx, markDone);

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse("aucune dépense ni aucun kilométrage ne doit être écrit");
        outcome.Body.Should().Contain("USER_PERMISSION_DENIED");
    }

    [Fact]
    public async Task Avec_CanMaintenance_POST_mark_done_atteint_le_controleur()
    {
        await using var ctx = await SeedAsync(u => u.CanMaintenance = true);

        var outcome = await SendAsync(ctx, Endpoint(nameof(VehicleMaintenanceController.MarkDone)));

        outcome.ReachedController.Should().BeTrue();
        outcome.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [MemberData(nameof(EveryAction))]
    public async Task Sans_CanMaintenance_ni_Rapports_aucune_action_du_controleur_ne_passe(string method, string path)
    {
        await using var ctx = await SeedAsync(u =>
        {
            u.CanMaintenance = false;
            u.CanReports = false;
        });

        var outcome = await SendAsync(ctx, (method, path));

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden, $"{method} {path}");
        outcome.ReachedController.Should().BeFalse();
    }

    [Fact]
    public async Task Sans_le_module_Entretien_l_abonnement_bloque_mark_done_meme_pour_un_admin_societe()
    {
        await using var ctx = await SeedAsync(companyAdmin: true, moduleMaintenance: false);

        var outcome = await SendAsync(ctx, Endpoint(nameof(VehicleMaintenanceController.MarkDone)));

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain("SUBSCRIPTION_MODULE_BLOCKED");
    }

    [Fact]
    public async Task Le_journal_de_flotte_suit_le_droit_du_rapport_Maintenance_et_rien_d_autre()
    {
        // Le rapport « Maintenance » (écran Rapports) lit GET /logs ; le front l'ouvre sur
        // canReportMaintenance, pas sur canMaintenance. Ce droit ne doit rien ouvrir d'autre.
        await using var ctx = await SeedAsync(u =>
        {
            u.CanMaintenance = false;
            u.CanReports = true;
            u.CanReportMaintenance = true;
        });

        var logs = await SendAsync(ctx, Endpoint(nameof(VehicleMaintenanceController.GetAllMaintenanceLogs)));
        var markDone = await SendAsync(ctx, Endpoint(nameof(VehicleMaintenanceController.MarkDone)));

        logs.ReachedController.Should().BeTrue("le rapport Maintenance reste lisible avec son seul droit");
        markDone.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        markDone.ReachedController.Should().BeFalse();
    }
}
