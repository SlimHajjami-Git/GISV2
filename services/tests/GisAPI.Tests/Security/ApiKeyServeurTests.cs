using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.ApiKeys.Commands.ExchangeApiKey;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GisAPI.Tests.Security;

/// <summary>
/// Le parcours de la tâche planifiée d'un client (migration 069) sur un VRAI serveur : le VRAI
/// contrôleur <see cref="ApiTokensController"/> (route anonyme d'échange, MediatR), l'aiguillage
/// d'authentification de Program.cs et le verrou des jetons. Prouve que la clé s'échange sans
/// session, que le jeton obtenu lit les arrêts une fois, et que la clé n'ouvre rien elle-même.
/// </summary>
public class ApiKeyServeurTests : IAsyncLifetime
{
    private static readonly SymmetricSecurityKey SessionKey =
        new(Encoding.UTF8.GetBytes("cle-de-test-de-session-assez-longue-pour-hs256-0123456789"));

    private const string Periode = "?startDate=2026-11-01&endDate=2026-11-17";

    /// <summary>Seul ApiTokensController est monté : les autres contrôleurs ont leurs propres dépendances.</summary>
    private sealed class SeulementCesControleurs : ControllerFeatureProvider
    {
        private readonly HashSet<Type> _types;
        public SeulementCesControleurs(params Type[] types) => _types = types.ToHashSet();
        protected override bool IsController(TypeInfo typeInfo) => _types.Contains(typeInfo.AsType());
    }

    private TestGisDbContext _db = null!;
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _db = TestDbContextFactory.Create();
        { var s = TestDataBuilder.CreateSociete(1, null); s.ApiAccessEnabled = true; _db.Societes.Add(s); }
        _db.Roles.Add(new Role { Id = 1, Name = "Gestionnaire", SocieteId = 1, IsCompanyAdmin = true });
        var user = TestDataBuilder.CreateUser(id: 10, companyId: 1, email: "client@exemple.tn");
        user.RoleId = 1;
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IGisDbContext>(_db);
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ExchangeApiKeyCommand).Assembly));
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ApiTokensController).Assembly)
            .ConfigureApplicationPartManager(m =>
            {
                foreach (var p in m.FeatureProviders.OfType<ControllerFeatureProvider>().ToList())
                    m.FeatureProviders.Remove(p);
                m.FeatureProviders.Add(new SeulementCesControleurs(typeof(ApiTokensController)));
            });
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
        _app.MapControllers();
        _app.MapGet("/api/vehiclestops", (HttpContext c) =>
            "stops de " + (c.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "?")).RequireAuthorization();
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

    /// <summary>Une clé valide en base, telle que la création la laisse.</summary>
    private async Task<string> NouvelleCleAsync(Action<ApiKey>? tweak = null)
    {
        var key = ApiKeyAccess.Generate();
        var row = new ApiKey
        {
            CompanyId = 1, UserId = 10, Label = "Tâche planifiée",
            KeyPrefix = ApiKeyAccess.DisplayPrefix(key), KeyHash = ApiKeyAccess.Hash(key),
            ExpiresAt = DateTime.UtcNow.AddDays(ApiKeyAccess.LifetimeDays)
        };
        tweak?.Invoke(row);
        _db.ApiKeys.Add(row);
        await _db.SaveChangesAsync();
        return key;
    }

    private Task<HttpResponseMessage> Appeler(HttpMethod method, string url, Action<HttpRequestMessage>? tweak = null)
    {
        var req = new HttpRequestMessage(method, url);
        tweak?.Invoke(req);
        return _http.SendAsync(req);
    }

    private Task<HttpResponseMessage> Echanger(string? key) =>
        Appeler(HttpMethod.Post, "/api/api-tokens/echange", r => { if (key != null) r.Headers.Add("X-Api-Key", key); });

    private static async Task<string> MessageAsync(HttpResponseMessage rep) =>
        JsonDocument.Parse(await rep.Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;

    private static string JetonDeSession()
    {
        var jwt = new JwtSecurityToken(
            claims: new[] { new Claim("sub", "10"), new Claim("companyId", "1") },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(SessionKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    [Fact]
    public async Task La_tache_echange_sa_cle_sans_session_puis_lit_les_arrets_une_fois()
    {
        var cle = await NouvelleCleAsync();

        var echange = await Echanger(cle);
        echange.StatusCode.Should().Be(HttpStatusCode.OK);
        echange.Headers.CacheControl!.NoStore.Should().BeTrue();
        var jeton = (await echange.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        jeton.Should().StartWith("cly_");

        var premier = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode, r => r.Headers.Add("X-Api-Token", jeton));
        premier.StatusCode.Should().Be(HttpStatusCode.OK);
        (await premier.Content.ReadAsStringAsync()).Should().Be("stops de 10");

        var second = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode, r => r.Headers.Add("X-Api-Token", jeton));
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Le lendemain, la tâche échange de nouveau la même clé : aucune action humaine.
        (await Echanger(cle)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task La_cle_n_ouvre_aucune_donnee_elle_meme()
    {
        var cle = await NouvelleCleAsync();

        var enCle = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode, r => r.Headers.Add("X-Api-Key", cle));
        var enJeton = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode, r => r.Headers.Add("X-Api-Token", cle));
        var enBearer = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode,
            r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cle));

        enCle.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        enJeton.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        enBearer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Ces essais ne l'ont pas abîmée.
        (await Echanger(cle)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Un_refus_d_echange_dit_sa_cause_en_json()
    {
        var revoquee = await NouvelleCleAsync(k => k.RevokedAt = DateTime.UtcNow);

        var sans = await Echanger(null);
        sans.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await MessageAsync(sans)).Should().Be(ApiKeyAccess.InvalidKeyMessage);

        var rep = await Echanger(revoquee);
        rep.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await MessageAsync(rep)).Should().Be(ApiKeyAccess.RevokedMessage);
    }

    [Fact]
    public async Task Une_session_ne_remplace_pas_la_cle()
    {
        var rep = await Appeler(HttpMethod.Post, "/api/api-tokens/echange",
            r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", JetonDeSession()));

        rep.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await MessageAsync(rep)).Should().Be(ApiKeyAccess.InvalidKeyMessage);
        _db.ApiTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_jeton_ne_peut_pas_appeler_l_echange_et_n_est_pas_brule()
    {
        var cle = await NouvelleCleAsync();
        var jeton = (await (await Echanger(cle)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        var rep = await Appeler(HttpMethod.Post, "/api/api-tokens/echange", r =>
        {
            r.Headers.Add("X-Api-Token", jeton);
            r.Headers.Add("X-Api-Key", cle);
        });
        rep.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var permis = await Appeler(HttpMethod.Get, "/api/vehiclestops" + Periode, r => r.Headers.Add("X-Api-Token", jeton));
        permis.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
