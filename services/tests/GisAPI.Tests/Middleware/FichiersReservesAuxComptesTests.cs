using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// Factures et documents lisibles sans connexion par qui avait le lien (campagne de tests GPA
/// du 06/10/2026, erreur n°18 : /uploads/invoices/7/… répondait 200 sans jeton). Désormais :
/// compte connecté (cookie posé à la connexion) de la société propriétaire, ou administrateur
/// système ; 401 sans session, 404 pour le fichier d'une autre société.
/// </summary>
public class FichiersReservesAuxComptesTests
{
    private const int Societe = 7;
    private const int AutreSociete = 5;
    private const int Sinistre = 21;

    private static TestGisDbContext Base()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.AccidentEvents.Add(new AccidentEvent
        {
            Id = Sinistre, CompanyId = Societe, DeviceUid = string.Empty, IncidentAt = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            Confidence = 100, Origin = "manual", Status = "confirmed", ReferenceCode = "MAN-20260910080000"
        });
        ctx.SaveChanges();
        return ctx;
    }

    private static ClaimsPrincipal Compte(int societe, bool systemAdmin = false)
    {
        var claims = new List<Claim> { new("companyId", societe.ToString()), new(ClaimTypes.NameIdentifier, "18") };
        if (systemAdmin) claims.Add(new Claim(ClaimTypes.Role, "system_admin"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static readonly ClaimsPrincipal Anonyme = new(new ClaimsIdentity());

    [Theory]
    [InlineData("/uploads/invoices/7/facture.pdf", Societe)]
    [InlineData("/uploads/acquisition-receipts/7/traite.jpg", Societe)]
    [InlineData("/uploads/accident-reports/21/photo.jpg", Societe)]
    [InlineData("/uploads/accident-reports/999/photo.jpg", -1)]     // sinistre inconnu : personne
    public async Task La_societe_proprietaire_se_lit_dans_le_chemin(string chemin, int attendue)
    {
        using var ctx = Base();
        (await UploadsAccess.ProprietaireAsync(chemin, ctx, CancellationToken.None)).Should().Be(attendue);
    }

    [Fact]
    public async Task Un_autre_dossier_n_a_pas_de_proprietaire_mais_reste_reserve_aux_comptes()
    {
        using var ctx = Base();
        (await UploadsAccess.ProprietaireAsync("/uploads/claims/1/piece.pdf", ctx, CancellationToken.None)).Should().BeNull();
        (await UploadsAccess.PeutLireAsync(Anonyme, "/uploads/claims/1/piece.pdf", ctx, CancellationToken.None)).Should().BeFalse();
        (await UploadsAccess.PeutLireAsync(Compte(AutreSociete), "/uploads/claims/1/piece.pdf", ctx, CancellationToken.None)).Should().BeTrue();
    }

    [Theory]
    [InlineData("/uploads/invoices/7/facture.pdf")]
    [InlineData("/uploads/accident-reports/21/photo.jpg")]
    public async Task Seuls_la_societe_et_l_administrateur_systeme_lisent_le_fichier(string chemin)
    {
        using var ctx = Base();
        (await UploadsAccess.PeutLireAsync(Anonyme, chemin, ctx, CancellationToken.None)).Should().BeFalse();
        (await UploadsAccess.PeutLireAsync(Compte(Societe), chemin, ctx, CancellationToken.None)).Should().BeTrue();
        (await UploadsAccess.PeutLireAsync(Compte(AutreSociete), chemin, ctx, CancellationToken.None)).Should().BeFalse();
        (await UploadsAccess.PeutLireAsync(Compte(AutreSociete, systemAdmin: true), chemin, ctx, CancellationToken.None)).Should().BeTrue();
    }

    private static async Task<int> PasserAsync(ClaimsPrincipal user, string chemin)
    {
        using var ctx = Base();
        var suivant = false;
        var garde = new UploadsAccessMiddleware(_ => { suivant = true; return Task.CompletedTask; });
        var http = new DefaultHttpContext { User = user };
        http.Request.Path = chemin;
        await garde.InvokeAsync(http, ctx);
        return suivant ? 200 : http.Response.StatusCode;
    }

    [Fact]
    public async Task Sans_session_401_autre_societe_404_proprietaire_servi()
    {
        (await PasserAsync(Anonyme, "/uploads/invoices/7/facture.pdf")).Should().Be(401);
        (await PasserAsync(Compte(AutreSociete), "/uploads/invoices/7/facture.pdf")).Should().Be(404);
        (await PasserAsync(Compte(Societe), "/uploads/invoices/7/facture.pdf")).Should().Be(200);
    }

    private static string Jeton(bool systemAdmin)
    {
        var claims = new List<Claim> { new("companyId", "7") };
        if (systemAdmin) claims.Add(new Claim(ClaimTypes.Role, "system_admin"));
        var cle = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("cle-de-test-suffisamment-longue-pour-hmac-256"));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: claims, expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: new SigningCredentials(cle, SecurityAlgorithms.HmacSha256)));
    }

    [Theory]
    [InlineData(false, UploadsAccess.CookieClient)]
    [InlineData(true, UploadsAccess.CookieAdmin)]
    public void La_connexion_pose_un_cookie_reserve_aux_fichiers(bool systemAdmin, string nom)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Forwarded-Proto"] = "https";

        UploadsAccess.Poser(http.Request, http.Response, Jeton(systemAdmin));

        var cookie = http.Response.Headers.SetCookie.ToString();
        cookie.Should().StartWith(nom + "=");
        cookie.Should().Contain("path=/uploads").And.Contain("httponly").And.Contain("samesite=strict").And.Contain("secure");
    }

    [Fact]
    public void Le_cookie_administrateur_passe_avant_le_cookie_client()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = $"{UploadsAccess.CookieClient}=client; {UploadsAccess.CookieAdmin}=admin";
        UploadsAccess.JetonDuCookie(http.Request).Should().Be("admin");

        http.Request.Headers.Cookie = $"{UploadsAccess.CookieClient}=client";
        UploadsAccess.JetonDuCookie(http.Request).Should().Be("client");
    }
}
