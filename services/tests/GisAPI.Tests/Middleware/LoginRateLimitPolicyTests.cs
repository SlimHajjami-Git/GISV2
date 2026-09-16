using System.Threading.RateLimiting;
using FluentAssertions;
using GisAPI.Controllers;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// DEF-030 (recette du 16/09/2026) : dix POST /api/auth/login avec un mot de passe faux
/// recevaient dix 400 en moins d'une seconde. Le GlobalLimiter de Program.cs existait,
/// mais la connexion n'y avait AUCUNE partition : seules l'inscription, le scan de
/// facture et l'assistant IA étaient plafonnés, tout le reste tombait sur « open ».
///
/// Ces tests portent sur les deux partitions seules (casse, IP réelle, plafond mal
/// configuré). Leur branchement dans la chaîne réelle, le 429 rendu et son message sont
/// couverts par RateLimitPipelineTests. Ils vérifient aussi ce que le plafond ne doit PAS
/// toucher : le rafraîchissement de jeton (appelé en boucle par le web et l'app mobile),
/// le hub SignalR, les lectures.
/// </summary>
public class LoginRateLimitPolicyTests
{
    private static PartitionedRateLimiter<HttpContext> BuildLimiter(
        int loginPerMinute = AuthController.DefaultLoginAttemptsPerMinutePerIp) =>
        PartitionedRateLimiter.CreateChained(
            PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                AuthController.LoginAttemptPartition(ctx, loginPerMinute)),
            PartitionedRateLimiter.Create<HttpContext, string>(
                AuthController.PasswordResetRequestPartition));

    private static HttpContext Request(string method, string path, string ip = "203.0.113.10", string? forwardedFor = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        if (forwardedFor != null)
            http.Request.Headers["X-Forwarded-For"] = forwardedFor;
        return http;
    }

    [Fact]
    public void Le_plafond_par_defaut_laisse_passer_une_equipe_derriere_une_meme_adresse_publique()
    {
        // Relecture : 5 par minute, succès compris, bloquait une prise de poste ordinaire
        // derrière un NAT d'opérateur (CGNAT) ou le réseau d'un bureau GPA.
        AuthController.DefaultLoginAttemptsPerMinutePerIp.Should().Be(10);
    }

    [Fact]
    public void La_tentative_au_dela_du_plafond_dans_la_minute_est_refusee_avec_une_date_de_reprise()
    {
        using var limiter = BuildLimiter();

        for (var i = 1; i <= AuthController.DefaultLoginAttemptsPerMinutePerIp; i++)
        {
            using var accepted = limiter.AttemptAcquire(Request("POST", "/api/auth/login"));
            accepted.IsAcquired.Should().BeTrue($"la tentative {i} reste sous le plafond");
        }

        using var rejected = limiter.AttemptAcquire(Request("POST", "/api/auth/login"));
        rejected.IsAcquired.Should().BeFalse();
        rejected.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter).Should().BeTrue(
            "OnRejected en tire l'en-tête Retry-After");
        retryAfter.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Les_dix_tentatives_de_la_recette_puis_cinq_de_plus_donnent_cinq_refus()
    {
        using var limiter = BuildLimiter();

        // La reproduction de la fiche (dix essais) épuise exactement le plafond par
        // défaut : chaque essai supplémentaire dans la minute est refusé.
        var refused = Enumerable.Range(0, AuthController.DefaultLoginAttemptsPerMinutePerIp + 5)
            .Count(_ =>
            {
                using var lease = limiter.AttemptAcquire(Request("POST", "/api/auth/login"));
                return !lease.IsAcquired;
            });

        refused.Should().Be(5);
    }

    [Fact]
    public void Le_plafond_est_compte_par_adresse_IP_reelle_derriere_le_proxy()
    {
        using var limiter = BuildLimiter(loginPerMinute: 2);

        // Derrière Traefik / nginx, la connexion TCP vient du proxy pour tout le monde :
        // compter sur elle bloquerait tous les clients à la fois.
        for (var i = 0; i < 2; i++)
        {
            using var lease = limiter.AttemptAcquire(
                Request("POST", "/api/auth/login", ip: "10.42.0.1", forwardedFor: "198.51.100.7"));
            lease.IsAcquired.Should().BeTrue();
        }
        using (var blocked = limiter.AttemptAcquire(
                   Request("POST", "/api/auth/login", ip: "10.42.0.1", forwardedFor: "198.51.100.7")))
        {
            blocked.IsAcquired.Should().BeFalse();
        }

        using var otherClient = limiter.AttemptAcquire(
            Request("POST", "/api/auth/login", ip: "10.42.0.1", forwardedFor: "198.51.100.8, 10.42.0.1"));
        otherClient.IsAcquired.Should().BeTrue("un autre client derrière le même proxy garde son quota");
    }

    [Theory]
    [InlineData("POST", "/api/auth/refresh")]
    [InlineData("POST", "/api/auth/logout")]
    [InlineData("POST", "/api/hubs/gps/negotiate")]
    [InlineData("GET", "/api/hubs/gps")]
    [InlineData("GET", "/hubs/gps")]
    [InlineData("GET", "/api/vehicles/with-positions")]
    [InlineData("OPTIONS", "/api/auth/login")]
    [InlineData("POST", "/api/auth/login-history")]
    public void Les_autres_routes_ne_sont_jamais_plafonnees(string method, string path)
    {
        using var limiter = BuildLimiter(loginPerMinute: 1);

        // D'abord épuiser le quota de connexion de cette adresse…
        using (limiter.AttemptAcquire(Request("POST", "/api/auth/login"))) { }

        // …puis vérifier que le reste passe sans compter.
        for (var i = 0; i < 50; i++)
        {
            using var lease = limiter.AttemptAcquire(Request(method, path));
            lease.IsAcquired.Should().BeTrue($"{method} {path} ne doit jamais recevoir de 429");
        }
    }

    [Theory]
    [InlineData("/API/Auth/Login")]
    [InlineData("/api/auth/login/")]
    public void Une_variante_de_casse_ou_de_barre_finale_ne_contourne_pas_le_plafond(string path)
    {
        using var limiter = BuildLimiter(loginPerMinute: 1);

        using (var first = limiter.AttemptAcquire(Request("POST", "/api/auth/login")))
            first.IsAcquired.Should().BeTrue();

        using var variant = limiter.AttemptAcquire(Request("POST", path));
        variant.IsAcquired.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Un_plafond_mal_configure_ne_fait_pas_tomber_la_connexion(int configured)
    {
        using var limiter = BuildLimiter(loginPerMinute: configured);

        var act = () =>
        {
            using var lease = limiter.AttemptAcquire(Request("POST", "/api/auth/login"));
            return lease.IsAcquired;
        };

        act.Should().NotThrow().Which.Should().BeTrue();
    }

    [Fact]
    public void Mot_de_passe_oublie_est_plafonne_a_cinq_demandes_par_heure()
    {
        using var limiter = BuildLimiter();

        for (var i = 0; i < 5; i++)
        {
            using var lease = limiter.AttemptAcquire(Request("POST", "/api/auth/forgot-password"));
            lease.IsAcquired.Should().BeTrue();
        }

        using var rejected = limiter.AttemptAcquire(Request("POST", "/api/auth/forgot-password"));
        rejected.IsAcquired.Should().BeFalse();

        // Le quota de connexion est distinct : l'un n'entame pas l'autre.
        using var login = limiter.AttemptAcquire(Request("POST", "/api/auth/login"));
        login.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void Les_messages_de_refus_sont_en_francais_et_propres_a_la_route()
    {
        AuthController.IsLoginAttempt(Request("POST", "/api/auth/login")).Should().BeTrue();
        AuthController.IsLoginAttempt(Request("POST", "/api/auth/refresh")).Should().BeFalse();
        AuthController.IsPasswordResetRequest(Request("POST", "/api/auth/forgot-password")).Should().BeTrue();

        AuthController.TooManyLoginAttemptsMessage.Should().Contain("tentatives de connexion");
        AuthController.TooManyPasswordResetRequestsMessage.Should().Contain("réinitialisation");
        // « Depuis cette adresse » se lisait comme l'adresse e-mail : le plafond vise le réseau.
        AuthController.TooManyLoginAttemptsMessage.Should().Contain("depuis ce réseau").And.NotContain("adresse");
        AuthController.TooManyPasswordResetRequestsMessage.Should().Contain("depuis ce réseau").And.NotContain("adresse");
    }
}
