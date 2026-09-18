using System.Text.Json;
using FluentAssertions;
using GisAPI.Controllers;
using GisAPI.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// DEF-030 (recette du 16/09/2026) : la connexion n'avait aucune partition dans le
/// GlobalLimiter de Program.cs. Relecture : les premiers tests recomposaient leur propre
/// chaîne, ils seraient restés verts si Program.cs avait oublié de brancher la connexion,
/// et le 429 rendu (message, Retry-After) n'était testé nulle part.
///
/// Ici, la politique est celle que Program.cs enregistre (RateLimitPolicies.Configure),
/// traversée par le VRAI middleware ASP.NET Core de limitation, derrière les en-têtes de
/// sécurité et dans le même ordre que l'API.
/// </summary>
public class RateLimitPipelineTests
{
    private sealed class Pipeline : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly RequestDelegate _app;

        /// <summary>Requêtes arrivées jusqu'au contrôleur (donc non refusées).</summary>
        public int ReachedController { get; private set; }

        public Pipeline(Dictionary<string, string?>? settings = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRateLimiter(options => RateLimitPolicies.Configure(options, configuration));
            _services = services.BuildServiceProvider();

            var app = new ApplicationBuilder(_services);
            app.UseSecurityHeaders();
            app.UseRateLimiter();
            // Le contrôleur répond comme à un mot de passe faux.
            app.Run(ctx =>
            {
                ReachedController++;
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return Task.CompletedTask;
            });
            _app = app.Build();
        }

        public async Task<HttpContext> SendAsync(string method, string path, string clientIp = "203.0.113.10")
        {
            var http = new DefaultHttpContext { RequestServices = _services };
            http.Request.Method = method;
            http.Request.Path = path;
            // Derrière Traefik, la socket vient du proxy pour tout le monde.
            http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.42.0.1");
            http.Request.Headers["X-Forwarded-For"] = clientIp;
            http.Response.Body = new MemoryStream();
            await _app(http);
            return http;
        }

        public void Dispose() => _services.Dispose();
    }

    private static string MessageOf(HttpContext http)
    {
        http.Response.Body.Position = 0;
        using var json = JsonDocument.Parse(http.Response.Body);
        return json.RootElement.GetProperty("message").GetString()!;
    }

    [Fact]
    public async Task Quinze_connexions_immediates_donnent_cinq_refus_429_en_francais()
    {
        using var pipeline = new Pipeline();

        var statuses = new List<int>();
        HttpContext last = null!;
        for (var i = 0; i < 15; i++)
        {
            last = await pipeline.SendAsync("POST", "/api/auth/login");
            statuses.Add(last.Response.StatusCode);
        }

        // Plafond par défaut de 10 par minute (CGNAT, bureaux partageant une adresse).
        statuses.Should().Equal(Enumerable.Repeat(400, 10).Concat(Enumerable.Repeat(429, 5)));
        pipeline.ReachedController.Should().Be(AuthController.DefaultLoginAttemptsPerMinutePerIp,
            "un refus ne doit pas atteindre le contrôle du mot de passe");

        MessageOf(last).Should().Be(AuthController.TooManyLoginAttemptsMessage);
        // Fenêtre fixe : la longueur de la fenêtre, borne haute de reprise.
        last.Response.Headers["Retry-After"].ToString().Should().Be("60");
        last.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        last.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
    }

    [Fact]
    public async Task Le_plafond_de_connexion_se_regle_par_configuration()
    {
        using var pipeline = new Pipeline(new Dictionary<string, string?>
        {
            ["Auth:LoginAttemptsPerMinutePerIp"] = "2"
        });

        (await pipeline.SendAsync("POST", "/api/auth/login")).Response.StatusCode.Should().Be(400);
        (await pipeline.SendAsync("POST", "/api/auth/login")).Response.StatusCode.Should().Be(400);
        (await pipeline.SendAsync("POST", "/api/auth/login")).Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task Un_client_bloque_ne_bloque_pas_les_autres_clients_du_meme_proxy()
    {
        using var pipeline = new Pipeline();

        for (var i = 0; i <= AuthController.DefaultLoginAttemptsPerMinutePerIp; i++)
            await pipeline.SendAsync("POST", "/api/auth/login", clientIp: "198.51.100.7");

        (await pipeline.SendAsync("POST", "/api/auth/login", clientIp: "198.51.100.7"))
            .Response.StatusCode.Should().Be(429);
        (await pipeline.SendAsync("POST", "/api/auth/login", clientIp: "198.51.100.8"))
            .Response.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData("POST", "/api/auth/refresh")]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("POST", "/api/hubs/gps/negotiate")]
    [InlineData("GET", "/api/hubs/gps")]
    [InlineData("GET", "/api/vehicles/with-positions")]
    [InlineData("OPTIONS", "/api/auth/login")]
    public async Task Une_adresse_bloquee_a_la_connexion_garde_le_reste_de_l_api(string method, string path)
    {
        using var pipeline = new Pipeline();
        for (var i = 0; i <= AuthController.DefaultLoginAttemptsPerMinutePerIp; i++)
            await pipeline.SendAsync("POST", "/api/auth/login");

        for (var i = 0; i < 30; i++)
        {
            var http = await pipeline.SendAsync(method, path);
            http.Response.StatusCode.Should().NotBe(429, $"{method} {path} ne doit jamais être plafonné");
        }
    }

    [Theory]
    [InlineData("/api/auth/forgot-password", 5, AuthController.TooManyPasswordResetRequestsMessage, "3600")]
    [InlineData("/api/auth/register", 3, RateLimitPolicies.TooManyRegistrationsMessage, "3600")]
    [InlineData("/api/auth/resend-confirmation", 3, RateLimitPolicies.TooManyRegistrationsMessage, "3600")]
    [InlineData("/api/assistant/ask", 10, RateLimitPolicies.TooManyQuestionsMessage, "30")]
    [InlineData("/api/costs/scan-invoice", 12, RateLimitPolicies.TooManyRequestsMessage, "60")]
    public async Task Chaque_route_plafonnee_rend_son_propre_message_et_son_delai(
        string path, int allowed, string message, string retryAfter)
    {
        using var pipeline = new Pipeline();

        for (var i = 0; i < allowed; i++)
            (await pipeline.SendAsync("POST", path)).Response.StatusCode.Should().Be(400);

        var rejected = await pipeline.SendAsync("POST", path);
        rejected.Response.StatusCode.Should().Be(429);
        MessageOf(rejected).Should().Be(message);
        rejected.Response.Headers["Retry-After"].ToString().Should().Be(retryAfter);
    }
}
