using FluentAssertions;
using GisAPI.Application.Common;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Auth;

/// <summary>
/// Préparation du paiement en ligne (19/10/2026, point 1) : une société en libre-service dont
/// l'abonnement ou l'essai avait expiré (au-delà des 7 jours de grâce) ne pouvait plus se
/// connecter, donc jamais atteindre l'écran Abonnement pour payer (cas réel en production : un
/// essai terminé le 13/10). Son administrateur se connecte désormais ; le serveur confine la
/// session à l'abonnement (SubscriptionExpirationMiddleware). Tout autre blocage reste refusé.
/// </summary>
public class ConnexionPourRegulariserTests
{
    private const int Societe = 19;

    private static (LoginCommandHandler Handler, TestGisDbContext Ctx) Connexion(bool administrateur, string codeOffre = "plan-basique", string statut = "active")
    {
        var ctx = TestDbContextFactory.Create();
        var offre = TestDataBuilder.CreateSubscriptionType(3);
        offre.Code = codeOffre;
        ctx.SubscriptionTypes.Add(offre);
        var societe = TestDataBuilder.CreateSociete(Societe, 3);
        societe.SubscriptionStatus = statut;
        societe.AutoSuspendEnabled = true;
        societe.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(-(SubscriptionPolicy.GraceDays + 6));
        ctx.Societes.Add(societe);
        ctx.Roles.Add(new Role { Id = 5, Name = administrateur ? "Administrateur" : "Employé", SocieteId = Societe, IsCompanyAdmin = administrateur });
        var user = TestDataBuilder.CreateUser(id: 70, companyId: Societe, email: "essai@exemple.tn");
        user.RoleId = 5;
        ctx.Users.Add(user);
        // Clé fantôme User → Societe du contexte de test (voir LoginFailureAuditTests).
        var cle = ctx.Model.FindEntityType(typeof(User))!.FindNavigation(nameof(User.Societe))!.ForeignKey.Properties.Single();
        ctx.Entry(user).Property(cle.Name).CurrentValue = Societe;
        ctx.SaveChanges();

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        var jwt = new Mock<IJwtService>();
        jwt.Setup(j => j.GenerateToken(It.IsAny<User>(), It.IsAny<string?>())).Returns("jeton");
        jwt.Setup(j => j.GenerateRefreshToken()).Returns("rafraichissement");
        return (new LoginCommandHandler(ctx, hasher.Object, jwt.Object, NullLogger<LoginCommandHandler>.Instance), ctx);
    }

    private static Task<LoginResponse> SeConnecter(LoginCommandHandler h) =>
        h.Handle(new LoginCommand("essai@exemple.tn", "Bon@2026", "10.0.0.1", "test"), CancellationToken.None);

    [Fact]
    public async Task L_administrateur_d_une_societe_en_libre_service_expiree_se_connecte_pour_renouveler()
    {
        var (h, ctx) = Connexion(administrateur: true);
        using (ctx)
        {
            var reponse = await SeConnecter(h);
            reponse.User.SelfServiceSubscription.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Un_employe_de_cette_societe_est_refuse_et_renvoye_a_son_administrateur()
    {
        var (h, ctx) = Connexion(administrateur: false);
        using (ctx)
            (await ((Func<Task>)(() => SeConnecter(h))).Should().ThrowAsync<DomainException>())
                .WithMessage("L'abonnement de votre société a expiré. Demandez à l'administrateur de votre société de le renouveler.");
    }

    [Fact]
    public async Task Une_societe_installee_par_Belive_reste_refusee_comme_avant()
    {
        var (h, ctx) = Connexion(administrateur: true, codeOffre: "gpa");
        using (ctx)
            (await ((Func<Task>)(() => SeConnecter(h))).Should().ThrowAsync<DomainException>())
                .WithMessage("L'abonnement de votre société a expiré. Contactez votre prestataire pour le renouveler.");
    }

    [Fact]
    public async Task Une_suspension_par_Belive_reste_refusee_meme_en_libre_service()
    {
        var (h, ctx) = Connexion(administrateur: true, statut: "suspended");
        using (ctx)
            (await ((Func<Task>)(() => SeConnecter(h))).Should().ThrowAsync<DomainException>())
                .WithMessage("L'abonnement de votre société est suspendu*");
    }
}
