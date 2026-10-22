using System.Security.Claims;
using FluentAssertions;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Entities;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// Administrateur restreint (demande du 22/10/2026, compte d'Adel, migration 063) : Sociétés,
/// Users, Véhicules et Monitoring seulement, et jamais l'irréversible (suppressions,
/// réinitialisation des données d'une société, « voir en tant que »). Masquer le menu ne
/// suffisait pas : toute route /api/admin était ouverte à tout administrateur système.
/// </summary>
public class AdministrateurRestreintTests
{
    private static readonly string[] Ecrans = { "clients", "users", "vehicles", "monitoring" };

    [Theory]
    [InlineData("GET", "/api/admin/company")]
    [InlineData("GET", "/api/admin/company/12")]
    [InlineData("POST", "/api/admin/company/12/suspend")]
    [InlineData("GET", "/api/admin/billing/overview")]
    [InlineData("PUT", "/api/admin/users/5")]
    [InlineData("POST", "/api/admin/users/5/reset-password")]
    [InlineData("POST", "/api/admin/vehicles")]
    [InlineData("POST", "/api/admin/vehicles/8/replace-device")]
    [InlineData("POST", "/api/admin/fichiers")]
    [InlineData("GET", "/api/vehicles/with-positions")]
    [InlineData("POST", "/api/hubs/gps/negotiate")]
    [InlineData("POST", "/api/auth/refresh")]
    [InlineData("PUT", "/api/roles/4")]
    [InlineData("GET", "/api/brands/3/models")]
    public void Les_fonctions_de_ses_quatre_ecrans_passent(string methode, string chemin) =>
        AdminScreenAccess.CanCall(Ecrans, methode, chemin).Should().BeTrue();

    [Theory]
    [InlineData("GET", "/api/admin/subscriptions")]               // écran Abonnements
    [InlineData("GET", "/api/admin/gps-devices")]                 // écran Boîtiers GPS
    [InlineData("PUT", "/api/admin/subscription-types/2")]        // modifier une offre
    [InlineData("DELETE", "/api/admin/users/5")]                  // suppression
    [InlineData("DELETE", "/api/admin/vehicles/8")]
    [InlineData("DELETE", "/api/admin/company/12")]
    [InlineData("DELETE", "/api/admin/gps-devices/3")]
    [InlineData("POST", "/api/admin/company/12/reset-data")]      // réinitialisation
    [InlineData("POST", "/api/auth/impersonate")]                 // « voir en tant que »
    [InlineData("DELETE", "/api/roles/4")]
    [InlineData("POST", "/api/vehicles")]                         // écriture côté client
    [InlineData("GET", "/api/costs")]                             // données hors de ses écrans
    public void Le_reste_est_refuse(string methode, string chemin) =>
        AdminScreenAccess.CanCall(Ecrans, methode, chemin).Should().BeFalse();

    [Fact]
    public void Un_role_systeme_sans_liste_d_ecrans_garde_tout_et_un_role_client_n_est_pas_concerne()
    {
        AdminScreenAccess.AllowedScreens(new Role { IsSystemRole = true }).Should().BeNull();
        AdminScreenAccess.AllowedScreens(new Role
        {
            IsSystemRole = false,
            Permissions = new Dictionary<string, object> { [AdminScreenAccess.PermissionKey] = Ecrans }
        }).Should().BeNull();
    }

    [Fact]
    public void La_connexion_renvoie_les_ecrans_autorises()
    {
        var user = TestDataBuilder.CreateUser(id: 30, companyId: 1, email: "adel@exemple.tn");
        user.Role = RoleRestreint();
        var dto = LoginCommandHandler.BuildUserDto(user, null, null, LoginCommandHandler.BuildUserPermissions(user));
        dto.AdminScreens.Should().Equal(Ecrans);
    }

    // ── Garde serveur, de bout en bout ──

    private static Role RoleRestreint() => new()
    {
        Id = 18, Name = "Administrateur restreint", SocieteId = 1, IsSystemRole = true, IsCompanyAdmin = true,
        Permissions = new Dictionary<string, object> { [AdminScreenAccess.PermissionKey] = Ecrans }
    };

    private static async Task<(int Status, bool Atteint)> Appeler(bool restreint, string methode, string chemin)
    {
        await using var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(TestDataBuilder.CreateSociete(1, null));
        ctx.Roles.Add(restreint ? RoleRestreint() : new Role { Id = 8, Name = "Administrateur", SocieteId = 1, IsSystemRole = true, IsCompanyAdmin = true });
        var admin = TestDataBuilder.CreateUser(id: 30, companyId: 1, email: "admin@exemple.tn");
        admin.RoleId = restreint ? 18 : 8;
        ctx.Users.Add(admin);
        var societeFk = ctx.Model.FindEntityType(typeof(User))!.FindNavigation(nameof(User.Societe))!.ForeignKey.Properties.Single();
        ctx.Entry(admin).Property(societeFk.Name).CurrentValue = 1;
        await ctx.SaveChangesAsync();

        var http = new DefaultHttpContext();
        http.Request.Method = methode;
        http.Request.Path = chemin;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "30"),
            new Claim(ClaimTypes.Role, "system_admin"),
        }, authenticationType: "Bearer"));
        http.Response.Body = new MemoryStream();
        var atteint = false;
        await new PermissionMiddleware(_ => { atteint = true; return Task.CompletedTask; }).InvokeAsync(http, ctx);
        return (http.Response.StatusCode, atteint);
    }

    // La base de test (SQLite) ignore les permissions des rôles (jsonb, TestGisDbContext) : le
    // refus de l'administrateur restreint se prouve en direct sur PostgreSQL. Ici, que la garde
    // ne change rien pour un administrateur sans restriction.
    [Fact]
    public async Task L_administrateur_complet_n_est_pas_concerne()
    {
        (await Appeler(restreint: false, "GET", "/api/admin/subscriptions")).Atteint.Should().BeTrue();
        (await Appeler(restreint: false, "DELETE", "/api/admin/users/5")).Atteint.Should().BeTrue();
    }
}
