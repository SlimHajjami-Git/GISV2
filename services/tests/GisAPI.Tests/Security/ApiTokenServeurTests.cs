using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Entities;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GisAPI.Tests.Security;

/// <summary>
/// Un VRAI serveur (Kestrel, port libre) monté avec le même aiguillage que Program.cs
/// (<c>AddApiTokenRouting</c>) : prouve de bout en bout qu'un jeton d'API est reconnu UNE fois, qu'un
/// jeton de session marche toujours comme avant, et que le jeton est borné aux arrêts en lecture.
/// </summary>
public class ApiTokenServeurTests : IAsyncLifetime
{
    private static readonly SymmetricSecurityKey SessionKey =
        new(Encoding.UTF8.GetBytes("cle-de-test-de-session-assez-longue-pour-hs256-0123456789"));

    private const string Periode = "?startDate=2026-10-01&endDate=2026-10-28";

    private TestGisDbContext _db = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _db = TestDbContextFactory.Create();
        _db.Societes.Add(TestDataBuilder.CreateSociete(1, null));
        _db.Roles.Add(new Role { Id = 1, Name = "Gestionnaire", SocieteId = 1, IsCompanyAdmin = true });
        var user = TestDataBuilder.CreateUser(id: 10, companyId: 1, email: "client@exemple.tn");
        user.RoleId = 1;
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IGisDbContext>(_db);
        builder.Services
            .AddAuthentication(ApiTokenAuthenticationExtensions.SmartScheme)
            .AddApiTokenRouting()
            .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false, ValidateAudience = false, IssuerSigningKey = SessionKey
            });
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseMiddleware<ApiTokenScopeMiddleware>();
        _app.UseAuthorization();
        static string Qui(HttpContext c) => c.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "?";
        _app.MapGet("/api/vehiclestops", (HttpContext c) => "stops de " + Qui(c)).RequireAuthorization();
        _app.MapPost("/api/vehiclestops", (HttpContext c) => "ecrit").RequireAuthorization();
        _app.MapGet("/api/vehicles", (HttpContext c) => "vehicules de " + Qui(c)).RequireAuthorization();
        _app.MapPost("/api/api-tokens", (HttpContext c) => "jeton").RequireAuthorization();
        await _app.StartAsync();

        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.First()) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _db.DisposeAsync();
    }

    /// <summary>Un jeton valide en base, tel que la génération le laisse.</summary>
    private async Task<string> NouveauJetonAsync(Action<ApiToken>? tweak = null)
    {
        var token = ApiTokenAccess.Generate();
        var row = new ApiToken
        {
            CompanyId = 1, UserId = 10,
            TokenPrefix = ApiTokenAccess.DisplayPrefix(token), TokenHash = ApiTokenAccess.Hash(token),
            ExpiresAt = DateTime.UtcNow.AddMinutes(15)
        };
        tweak?.Invoke(row);
        _db.ApiTokens.Add(row);
        await _db.SaveChangesAsync();
        return token;
    }

    private static string JetonDeSession()
    {
        var jwt = new JwtSecurityToken(
            claims: new[] { new Claim("sub", "10"), new Claim("companyId", "1") },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(SessionKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private Task<HttpResponseMessage> Appeler(HttpMethod method, string url, Action<HttpRequestMessage>? tweak = null)
    {
        var req = new HttpRequestMessage(method, url);
        tweak?.Invoke(req);
        return _http.SendAsync(req);
    }

    [Fact]
    public async Task Le_jeton_en_en_tete_lit_les_arrets_une_seule_fois()
    {
        var token = await NouveauJetonAsync();

        var premier = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode, r => r.Headers.Add("X-Api-Token", token));
        premier.StatusCode.Should().Be(HttpStatusCode.OK);
        (await premier.Content.ReadAsStringAsync()).Should().Be("stops de 10");

        var second = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode, r => r.Headers.Add("X-Api-Token", token));
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Le_jeton_en_bearer_marche_aussi_une_seule_fois()
    {
        var token = await NouveauJetonAsync();

        var premier = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode,
            r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token));
        premier.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode,
            r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token));
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sans_aucun_identifiant_c_est_401()
    {
        (await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Un_jeton_faux_ou_expire_c_est_401()
    {
        var faux = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode,
            r => r.Headers.Add("X-Api-Token", ApiTokenAccess.Generate()));
        faux.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var expire = await NouveauJetonAsync(t => t.ExpiresAt = DateTime.UtcNow.AddMinutes(-1));
        var rep = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode, r => r.Headers.Add("X-Api-Token", expire));
        rep.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Le_jeton_de_session_marche_toujours_partout_comme_avant()
    {
        foreach (var url in new[] { "/api/vehiclestops" + Periode, "/api/vehicles", "/api/vehiclestops" })
        {
            var rep = await Appeler(HttpMethod.Get, url,
                r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JetonDeSession()));
            rep.StatusCode.Should().Be(HttpStatusCode.OK, url);
        }

        // Y compris l'écriture et la génération : le verrou ne concerne QUE les jetons d'API.
        foreach (var url in new[] { "/api/vehiclestops", "/api/api-tokens" })
        {
            var post = await Appeler(HttpMethod.Post, url,
                r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JetonDeSession()));
            post.StatusCode.Should().Be(HttpStatusCode.OK, url);
        }
    }

    [Theory]
    [InlineData("GET", "/api/vehicles")]
    [InlineData("POST", "/api/api-tokens")]
    [InlineData("POST", "/api/vehiclestops")]
    public async Task Le_jeton_ne_sort_pas_des_arrets_en_lecture_et_n_est_pas_brule(string method, string path)
    {
        var token = await NouveauJetonAsync();

        var refuse = await Appeler(new HttpMethod(method), path + Periode, r => r.Headers.Add("X-Api-Token", token));
        refuse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Le refus n'a pas consommé le jeton : il sert encore, une fois, sur un appel permis.
        var permis = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode, r => r.Headers.Add("X-Api-Token", token));
        permis.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Le_jeton_exige_une_periode_bornee_sans_etre_brule()
    {
        var token = await NouveauJetonAsync();

        var sans = await Appeler(HttpMethod.Get, "/api/vehiclestops", r => r.Headers.Add("X-Api-Token", token));
        sans.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var trop = await Appeler(HttpMethod.Get, "/api/vehiclestops?startDate=2026-01-01&endDate=2026-10-28",
            r => r.Headers.Add("X-Api-Token", token));
        trop.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await trop.Content.ReadAsStringAsync()).Should().Contain("31 jours");

        var bon = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode, r => r.Headers.Add("X-Api-Token", token));
        bon.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
