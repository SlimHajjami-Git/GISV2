using System.Security.Claims;
using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// Compte chauffeur (migration 050, décision du 21/09/2026 : « le chauffeur est un
/// utilisateur ») : il se connecte à l'application mobile et n'y voit que ses tournées.
/// Le serveur est ce qui l'y tient — pas l'écran : liste blanche stricte dans le
/// middleware, AVANT tout aiguillage, pour couvrir aussi ce que ClassifyRoute laisse
/// passer sans regarder (hubs SignalR de la flotte, /health, _skipRoutes comme le
/// tableau de bord, lectures partagées comme la liste des chauffeurs avec CIN).
/// </summary>
public class PermissionMiddlewareDriverTests
{
    private const int CompanyId = 7;
    private const int ChauffeurId = 61;
    private const int SalarieId = 62;

    private static async Task<TestGisDbContext> SeedAsync(string statutChauffeur = "active")
    {
        var ctx = TestDbContextFactory.Create();
        ctx.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Roles.Add(new Role { Id = 1, Name = "Employé", SocieteId = CompanyId, IsCompanyAdmin = false });

        var chauffeur = TestDataBuilder.CreateUser(id: ChauffeurId, companyId: CompanyId, email: "chauffeur@test.com");
        chauffeur.AccountType = UserAccountTypes.Driver;
        chauffeur.Status = statutChauffeur;
        var salarie = TestDataBuilder.CreateUser(id: SalarieId, companyId: CompanyId, email: "salarie@test.com");
        salarie.CanTours = true;
        ctx.Users.AddRange(chauffeur, salarie);
        // Même raison que PermissionMiddlewareMaintenanceTests : clé fantôme vers Societe.
        var societeFk = ctx.Model.FindEntityType(typeof(User))!
            .FindNavigation(nameof(User.Societe))!.ForeignKey.Properties.Single();
        ctx.Entry(chauffeur).Property(societeFk.Name).CurrentValue = CompanyId;
        ctx.Entry(salarie).Property(societeFk.Name).CurrentValue = CompanyId;
        await ctx.SaveChangesAsync();
        return ctx;
    }

    private sealed record Outcome(int StatusCode, bool ReachedController, string Body);

    private static async Task<Outcome> SendAsync(TestGisDbContext ctx, string method, string path, int userId, bool claimChauffeur)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
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
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        return new Outcome(http.Response.StatusCode, reached, body);
    }

    [Theory]
    [InlineData("GET", "/api/vehicles")]                 // lecture partagée
    [InlineData("GET", "/api/drivers")]                  // CIN et téléphone de tous les chauffeurs
    [InlineData("GET", "/api/tours")]                    // les tournées de toute la société
    [InlineData("GET", "/api/dashboard/kpis")]           // _skipRoutes
    [InlineData("GET", "/api/statistics/fleet")]         // _skipRoutes
    [InlineData("GET", "/api/notifications")]            // _skipRoutes
    [InlineData("GET", "/api/users/me")]                 // libre-service
    [InlineData("GET", "/api/chat/users")]               // annuaire, sans clé de permission
    [InlineData("POST", "/hubs/gps/negotiate")]          // hub mobile, hors /api
    [InlineData("POST", "/api/hubs/gps/negotiate")]      // hub web
    [InlineData("GET", "/health")]
    [InlineData("GET", "/api/driver-apple")]             // frontière de segment
    public async Task Un_chauffeur_est_refuse_partout_sauf_sur_ses_routes(string method, string path)
    {
        await using var ctx = await SeedAsync();

        var outcome = await SendAsync(ctx, method, path, ChauffeurId, claimChauffeur: true);

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden, $"{method} {path}");
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain(PermissionMiddleware.DriverAppOnlyCode);
    }

    [Theory]
    [InlineData("GET", "/api/driver-app/me")]
    [InlineData("GET", "/api/driver-app/tours")]
    [InlineData("POST", "/api/driver-app/tours/12/waypoints/3/arrive")]
    [InlineData("POST", "/api/auth/refresh")]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("POST", "/api/devicetokens")]
    [InlineData("DELETE", "/api/devicetokens")]
    public async Task Un_chauffeur_actif_atteint_ses_routes(string method, string path)
    {
        await using var ctx = await SeedAsync();

        var outcome = await SendAsync(ctx, method, path, ChauffeurId, claimChauffeur: true);

        outcome.ReachedController.Should().BeTrue($"{method} {path}");
        outcome.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Un_chauffeur_desactive_est_refuse_meme_sur_ses_routes_sans_attendre_l_expiration_du_jeton()
    {
        await using var ctx = await SeedAsync(statutChauffeur: "inactive");

        var outcome = await SendAsync(ctx, "GET", "/api/driver-app/tours", ChauffeurId, claimChauffeur: true);

        outcome.StatusCode.Should().Be(StatusCodes.Status401Unauthorized, "la décision est relue en base à chaque appel");
        outcome.ReachedController.Should().BeFalse();
    }

    [Fact]
    public async Task Un_jeton_emis_avant_le_passage_en_chauffeur_est_refuse_sur_les_routes_controlees()
    {
        // Pas de claim « acct » (jeton antérieur), mais la ligne users dit « driver ».
        await using var ctx = await SeedAsync();

        var outcome = await SendAsync(ctx, "GET", "/api/tours", ChauffeurId, claimChauffeur: false);

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.Body.Should().Contain(PermissionMiddleware.DriverAppOnlyCode);
    }

    [Theory]
    [InlineData("GET", "/api/driver-app/me")]
    [InlineData("GET", "/api/driver-app/tours/12")]
    [InlineData("POST", "/api/devicetokens")]
    public async Task Un_jeton_emis_avant_le_passage_en_chauffeur_atteint_quand_meme_ses_routes(string method, string path)
    {
        // Salarié déjà connecté à l'application, converti puis destinataire d'une tournée : la
        // base fait foi dans les deux sens — ses routes s'ouvrent sans attendre un nouveau jeton.
        await using var ctx = await SeedAsync();

        var outcome = await SendAsync(ctx, method, path, ChauffeurId, claimChauffeur: false);

        outcome.ReachedController.Should().BeTrue($"{method} {path}");
        outcome.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task Un_jeton_emis_avant_le_passage_en_chauffeur_d_un_compte_desactive_reste_refuse()
    {
        await using var ctx = await SeedAsync(statutChauffeur: "inactive");

        var outcome = await SendAsync(ctx, "GET", "/api/driver-app/tours", ChauffeurId, claimChauffeur: false);

        outcome.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        outcome.ReachedController.Should().BeFalse();
        outcome.Body.Should().Contain(PermissionMiddleware.DriverAppOnlyCode);
    }

    [Fact]
    public async Task Un_salarie_n_est_pas_touche()
    {
        await using var ctx = await SeedAsync();

        foreach (var path in new[] { "/api/tours", "/api/vehicles", "/api/dashboard/kpis", "/api/driver-app/tours" })
        {
            var outcome = await SendAsync(ctx, "GET", path, SalarieId, claimChauffeur: false);
            outcome.ReachedController.Should().BeTrue($"GET {path} pour un salarié ordinaire");
        }
    }

    [Theory]
    [InlineData("/api/driver-app", true)]
    [InlineData("/api/driver-app/tours/1", true)]
    [InlineData("/api/driver-apple", false)]
    [InlineData("/api/devicetokens/test-push", true)]
    [InlineData("/api/auth/login", false)]
    [InlineData("/api/auth/refresh", true)]
    [InlineData("/hubs/gps", false)]
    public void La_liste_blanche_respecte_la_frontiere_de_segment(string path, bool attendu) =>
        PermissionMiddleware.IsDriverAppRoute(path).Should().Be(attendu);
}
