using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Auth;

/// <summary>
/// Guides des écrans (Karim, 24/09/2026) : ils ne sont présentés qu'à un nouvel
/// utilisateur, et « nouvel utilisateur = première connexion ». La connexion dit donc
/// au site si c'est la toute première du compte : users.last_login_at encore vide.
/// </summary>
public class FirstLoginTests
{
    private static (LoginCommandHandler Handler, TestGisDbContext Ctx) Setup(DateTime? lastLoginAt)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: 7));
        ctx.Roles.Add(new GisAPI.Domain.Entities.Role { Id = 1, Name = "Administrateur", SocieteId = 7, IsCompanyAdmin = true });
        var user = TestDataBuilder.CreateUser(id: 42, companyId: 7, email: "nouveau@belive-gpa.fr");
        user.Status = "active";
        user.RoleId = 1;
        user.LastLoginAt = lastLoginAt;
        ctx.Users.Add(user);
        // Même clé fantôme que dans LoginFailureAuditTests : sans elle, l'Include du
        // handler ne retrouve pas le compte.
        var societeFk = ctx.Model.FindEntityType(typeof(GisAPI.Domain.Entities.User))!
            .FindNavigation(nameof(GisAPI.Domain.Entities.User.Societe))!.ForeignKey.Properties.Single();
        ctx.Entry(user).Property(societeFk.Name).CurrentValue = 7;
        ctx.SaveChanges();

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        // Une connexion réussie enregistre un jeton de rafraîchissement : il lui faut une valeur.
        var jwt = new Mock<IJwtService>();
        jwt.Setup(j => j.GenerateToken(It.IsAny<GisAPI.Domain.Entities.User>(), It.IsAny<string?>())).Returns("jeton");
        jwt.Setup(j => j.GenerateRefreshToken()).Returns(() => Guid.NewGuid().ToString());
        var handler = new LoginCommandHandler(ctx, hasher.Object, jwt.Object,
            NullLogger<LoginCommandHandler>.Instance);
        return (handler, ctx);
    }

    private static LoginCommand Connexion() => new("nouveau@belive-gpa.fr", "Bon@2026");

    [Fact]
    public async Task La_toute_premiere_connexion_est_signalee_puis_plus_jamais()
    {
        var (handler, ctx) = Setup(lastLoginAt: null);

        var premiere = await handler.Handle(Connexion(), CancellationToken.None);
        premiere.FirstLogin.Should().BeTrue("last_login_at était vide : le compte ne s'était jamais connecté");

        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == 42)).LastLoginAt
            .Should().NotBeNull("la date est écrite dès la première connexion");

        var seconde = await handler.Handle(Connexion(), CancellationToken.None);
        seconde.FirstLogin.Should().BeFalse("le compte s'est déjà connecté une fois");
    }

    [Fact]
    public async Task Un_compte_deja_connecte_n_est_pas_un_nouvel_utilisateur()
    {
        // Les clients déjà installés en production : ils ne doivent pas recevoir les guides.
        var (handler, _) = Setup(lastLoginAt: new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));

        var reponse = await handler.Handle(Connexion(), CancellationToken.None);

        reponse.FirstLogin.Should().BeFalse();
    }
}
