using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Entities;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace GisAPI.Tests.Security;

/// <summary>
/// Chemin d'une requête portant un jeton d'API : lecture de l'en-tête, relecture en base, identité
/// construite, CONSOMMATION UNIQUE, puis verrou en lecture seule. Le gestionnaire et le middleware
/// réels, sur une vraie requête simulée et la base de test.
/// </summary>
public class ApiTokenAuthenticationTests
{
    private const string Periode = "?startDate=2026-10-01&endDate=2026-10-28";

    private static DefaultHttpContext Requete(string method, string path, string query = Periode, string? header = null, string? bearer = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
        http.Request.QueryString = new QueryString(query);
        if (header != null) http.Request.Headers[ApiTokenAccess.HeaderName] = header;
        if (bearer != null) http.Request.Headers.Authorization = "Bearer " + bearer;
        http.Response.Body = new MemoryStream();
        return http;
    }

    private static async Task<(TestGisDbContext ctx, string token)> SeedAsync(
        Action<User>? userTweak = null, Action<ApiToken>? tokenTweak = null)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(TestDataBuilder.CreateSociete(1, null));
        ctx.Roles.Add(new Role { Id = 1, Name = "Gestionnaire", SocieteId = 1, IsCompanyAdmin = true });
        var user = TestDataBuilder.CreateUser(id: 10, companyId: 1, email: "client@exemple.tn");
        user.RoleId = 1;
        userTweak?.Invoke(user);
        ctx.Users.Add(user);

        var token = ApiTokenAccess.Generate();
        var row = new ApiToken
        {
            CompanyId = 1, UserId = 10,
            TokenPrefix = ApiTokenAccess.DisplayPrefix(token), TokenHash = ApiTokenAccess.Hash(token),
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };
        tokenTweak?.Invoke(row);
        ctx.ApiTokens.Add(row);
        await ctx.SaveChangesAsync();
        return (ctx, token);
    }

    private static async Task<AuthenticateResult> AuthentifierAsync(TestGisDbContext ctx, HttpContext http)
    {
        var options = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Setup(o => o.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());
        options.Setup(o => o.CurrentValue).Returns(new AuthenticationSchemeOptions());

        var handler = new ApiTokenAuthenticationHandler(options.Object, NullLoggerFactory.Instance, UrlEncoder.Default, ctx);
        await handler.InitializeAsync(
            new AuthenticationScheme(ApiTokenAuthenticationHandler.SchemeName, null, typeof(ApiTokenAuthenticationHandler)), http);
        return await handler.AuthenticateAsync();
    }

    // ── Lecture du jeton dans la requête ──

    [Fact]
    public void Le_jeton_se_lit_dans_l_en_tete_dedie_ou_en_bearer_prefixe()
    {
        ApiTokenAuthenticationHandler.ExtractToken(Requete("GET", "/x", header: " cly_abc ").Request).Should().Be("cly_abc");
        ApiTokenAuthenticationHandler.ExtractToken(Requete("GET", "/x", bearer: "cly_abc").Request).Should().Be("cly_abc");
    }

    [Fact]
    public void Un_jeton_de_session_n_est_jamais_pris_pour_un_jeton_d_api()
    {
        ApiTokenAuthenticationHandler.ExtractToken(Requete("GET", "/x", bearer: "eyJhbGciOiJIUzI1NiJ9.e30.x").Request).Should().BeNull();
        ApiTokenAuthenticationHandler.ExtractToken(Requete("GET", "/x").Request).Should().BeNull();
    }

    // ── Authentification et usage unique ──

    [Fact]
    public async Task Un_jeton_valide_donne_l_identite_de_son_utilisateur_et_est_consomme()
    {
        var (ctx, token) = await SeedAsync();
        await using var _ = ctx;

        var result = await AuthentifierAsync(ctx, Requete("GET", "/api/vehiclestops", header: token));

        result.Succeeded.Should().BeTrue();
        var p = result.Principal!;
        p.FindFirst(ClaimTypes.NameIdentifier)!.Value.Should().Be("10");
        p.FindFirst("companyId")!.Value.Should().Be("1");
        p.FindFirst(ApiTokenAccess.AuthMethodClaim)!.Value.Should().Be(ApiTokenAccess.AuthMethodValue);
        p.IsInRole("company_admin").Should().BeTrue();
        p.IsInRole("system_admin").Should().BeFalse();

        ctx.ChangeTracker.Clear();
        ctx.ApiTokens.Single().UsedAt.Should().NotBeNull()
            .And.Subject!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Le_second_appel_avec_le_meme_jeton_est_refuse()
    {
        var (ctx, token) = await SeedAsync();
        await using var _ = ctx;

        (await AuthentifierAsync(ctx, Requete("GET", "/api/vehiclestops", header: token))).Succeeded.Should().BeTrue();
        (await AuthentifierAsync(ctx, Requete("GET", "/api/vehiclestops", header: token))).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Le_bearer_prefixe_marche_comme_l_en_tete()
    {
        var (ctx, token) = await SeedAsync();
        await using var _ = ctx;

        (await AuthentifierAsync(ctx, Requete("GET", "/api/vehiclestops", bearer: token))).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Un_appel_hors_perimetre_ne_consomme_pas_le_jeton()
    {
        var (ctx, token) = await SeedAsync();
        await using var _ = ctx;

        // Route interdite, méthode interdite, période manquante : le verrou répondra, le jeton reste intact.
        await AuthentifierAsync(ctx, Requete("GET", "/api/vehicles", header: token));
        await AuthentifierAsync(ctx, Requete("POST", "/api/vehiclestops", header: token));
        await AuthentifierAsync(ctx, Requete("GET", "/api/vehiclestops", query: "", header: token));
        ctx.ChangeTracker.Clear();
        ctx.ApiTokens.Single().UsedAt.Should().BeNull();

        (await AuthentifierAsync(ctx, Requete("GET", "/api/vehiclestops", header: token))).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Un_jeton_inconnu_est_refuse()
    {
        var (ctx, _) = await SeedAsync();
        await using var __ = ctx;

        (await AuthentifierAsync(ctx, Requete("GET", "/api/vehiclestops", header: ApiTokenAccess.Generate()))).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Sans_jeton_le_gestionnaire_ne_dit_rien()
    {
        var (ctx, _) = await SeedAsync();
        await using var __ = ctx;

        var result = await AuthentifierAsync(ctx, Requete("GET", "/api/vehiclestops"));

        result.Succeeded.Should().BeFalse();
        result.None.Should().BeTrue();
    }

    [Fact]
    public async Task Un_jeton_expire_est_refuse()
    {
        var (ctx, token) = await SeedAsync(tokenTweak: t => t.ExpiresAt = DateTime.UtcNow.AddMinutes(-1));
        await using var _ = ctx;

        (await AuthentifierAsync(ctx, Requete("GET", "/api/vehiclestops", header: token))).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Un_compte_desactive_ferme_ses_jetons_en_attente()
    {
        var (ctx, token) = await SeedAsync(userTweak: u => u.Status = "suspended");
        await using var _ = ctx;

        (await AuthentifierAsync(ctx, Requete("GET", "/api/vehiclestops", header: token))).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Un_compte_promu_administrateur_systeme_ferme_ses_jetons()
    {
        var (ctx, token) = await SeedAsync();
        await using var _ = ctx;
        ctx.Roles.Add(new Role { Id = 2, Name = "Administrateur", SocieteId = 1, IsSystemRole = true });
        (await ctx.Users.FindAsync(10))!.RoleId = 2;
        await ctx.SaveChangesAsync();

        (await AuthentifierAsync(ctx, Requete("GET", "/api/vehiclestops", header: token))).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Un_jeton_dont_la_societe_ne_correspond_plus_au_compte_est_refuse()
    {
        var (ctx, token) = await SeedAsync(tokenTweak: t => t.CompanyId = 2);
        await using var _ = ctx;

        (await AuthentifierAsync(ctx, Requete("GET", "/api/vehiclestops", header: token))).Succeeded.Should().BeFalse();
    }

    // ── Verrou en lecture seule (le vrai middleware) ──

    private static async Task<(int status, bool passed)> PasserParLeVerrouAsync(HttpContext http, bool avecJeton)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "10") };
        if (avecJeton) claims.Add(new Claim(ApiTokenAccess.AuthMethodClaim, ApiTokenAccess.AuthMethodValue));
        http.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        var passed = false;
        var middleware = new ApiTokenScopeMiddleware(_ => { passed = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(http);
        return (http.Response.StatusCode, passed);
    }

    [Fact]
    public async Task Le_verrou_laisse_passer_la_lecture_des_arrets_bornee()
    {
        var (status, passed) = await PasserParLeVerrouAsync(Requete("GET", "/api/reports/stops/12"), avecJeton: true);

        passed.Should().BeTrue();
        status.Should().Be(200);
    }

    [Theory]
    [InlineData("GET", "/api/vehicles", Periode, 403)]
    [InlineData("GET", "/api/api-tokens", Periode, 403)]
    [InlineData("POST", "/api/api-tokens", Periode, 403)]
    [InlineData("POST", "/api/vehiclestops", Periode, 403)]
    [InlineData("GET", "/api/vehiclestops", "", 400)]
    [InlineData("GET", "/api/vehiclestops", "?startDate=2026-01-01&endDate=2026-10-28", 400)]
    [InlineData("GET", "/api/vehiclestops", "?startDate=2026-10-01&endDate=2026-10-28&pageSize=5000", 400)]
    public async Task Le_verrou_bloque_tout_le_reste_pour_un_jeton(string method, string path, string query, int attendu)
    {
        var (status, passed) = await PasserParLeVerrouAsync(Requete(method, path, query), avecJeton: true);

        passed.Should().BeFalse();
        status.Should().Be(attendu);
    }

    [Fact]
    public async Task Le_verrou_ne_touche_pas_une_session_ordinaire()
    {
        var (status, passed) = await PasserParLeVerrouAsync(Requete("POST", "/api/vehicles", ""), avecJeton: false);

        passed.Should().BeTrue();
        status.Should().Be(200);
    }
}
