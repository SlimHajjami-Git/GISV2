using FluentAssertions;
using GisAPI.Domain.Exceptions;
using GisAPI.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// DEF-051 (recette du 16/09/2026) : les réponses de l'API ne portaient que Content-Type,
/// Date et Server — ni nosniff, ni politique d'encadrement, ni Referrer-Policy.
///
/// Le cas qui compte le plus est la réponse d'ERREUR : c'est elle que le recetteur a lue
/// (400 sur /api/auth/login), et c'est celle qu'un middleware placé trop bas dans le
/// pipeline oublierait.
/// </summary>
public class SecurityHeadersMiddlewareTests
{
    private static void AssertSecurityHeaders(HttpResponse response)
    {
        response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        response.Headers["Content-Security-Policy"].ToString().Should().Be("frame-ancestors 'none'");
        response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
    }

    [Theory]
    [InlineData("GET", "/api/vehicles")]
    [InlineData("GET", "/uploads/invoices/7/facture.pdf")]
    [InlineData("POST", "/api/hubs/gps/negotiate")]
    [InlineData("GET", "/swagger/index.html")]
    public async Task Une_reponse_ordinaire_porte_les_en_tetes_de_securite(string method, string path)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;

        var middleware = new SecurityHeadersMiddleware(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(http);

        AssertSecurityHeaders(http.Response);
    }

    [Fact]
    public async Task Une_erreur_rendue_par_le_middleware_d_exceptions_garde_les_en_tetes()
    {
        // Même ordre que Program.cs : en-têtes de sécurité, PUIS gestion des exceptions.
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/api/auth/login";
        http.Response.Body = new MemoryStream();

        var exceptions = new ExceptionHandlingMiddleware(
            _ => throw new DomainException("Email ou mot de passe incorrect"),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var middleware = new SecurityHeadersMiddleware(exceptions.InvokeAsync);

        await middleware.InvokeAsync(http);

        http.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        AssertSecurityHeaders(http.Response);
    }

    [Fact]
    public async Task Une_reponse_refusee_par_le_limiteur_garde_les_en_tetes()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = "/api/auth/login";

        var middleware = new SecurityHeadersMiddleware(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ctx.Response.Headers["Retry-After"] = "42";
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(http);

        http.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        AssertSecurityHeaders(http.Response);
    }

    [Fact]
    public async Task Un_maillon_qui_fixe_sa_propre_politique_l_emporte()
    {
        // Porte laissée ouverte à un futur aperçu intégré : l'endpoint concerné pourra
        // assouplir l'encadrement sans retirer la protection de toutes les autres routes.
        var http = new DefaultHttpContext();

        var middleware = new SecurityHeadersMiddleware(ctx =>
        {
            ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(http);

        http.Response.Headers["X-Frame-Options"].ToString().Should().Be("SAMEORIGIN");
        http.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }
}
