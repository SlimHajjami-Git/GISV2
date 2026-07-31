using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Register;
using GisAPI.Domain.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Auth;

/// <summary>
/// Inscription libre : un visiteur crée son compte et, du même geste, SA SOCIÉTÉ.
///
/// Ces tests couvrent surtout des NON-RÉGRESSIONS. Le handler existait depuis
/// longtemps mais n'était exposé par aucun endpoint : jamais exécuté, il posait
/// une société sans date d'expiration (accès gratuit perpétuel, invisible de la
/// supervision), choisissait un plan au hasard, et ne persistait pas le jeton de
/// rafraîchissement. Chacun de ces points a son test ci-dessous : ce sont eux qui
/// doivent hurler si quelqu'un revient en arrière.
///
/// LIMITE ASSUMÉE — le socle de tests (SQLite en mémoire) n'applique aucun filtre
/// multi-tenant : ces tests ne prouvent donc rien sur l'isolation entre sociétés.
/// C'est acceptable ici, le visiteur n'ayant pas encore de société au moment de
/// l'appel.
/// </summary>
public class RegisterCommandHandlerTests
{
    private const string PlanCode = "plan-basique";

    private static (RegisterCommandHandler handler, TestGisDbContext ctx) Setup(
        bool withPlan = true, bool planActive = true)
    {
        var ctx = TestDbContextFactory.Create();

        if (withPlan)
        {
            var plan = TestDataBuilder.CreateSubscriptionType();
            plan.Code = PlanCode;
            plan.IsActive = planActive;
            plan.YearlyPrice = 1200m;
            ctx.SubscriptionTypes.Add(plan);
            ctx.SaveChanges();
        }

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hashed");

        var jwt = new Mock<IJwtService>();
        // GenerateToken porte un paramètre facultatif : il faut l'expliciter, une
        // arborescence d'expression n'accepte pas les arguments par défaut.
        jwt.Setup(j => j.GenerateToken(It.IsAny<User>(), It.IsAny<string?>())).Returns("jwt-token");
        jwt.Setup(j => j.GenerateRefreshToken()).Returns("refresh-token");

        AppRegistration.DefaultPlanCode = PlanCode;
        AppRegistration.TrialDays = 14;

        var handler = new RegisterCommandHandler(
            ctx, hasher.Object, jwt.Object,
            NullLogger<RegisterCommandHandler>.Instance);

        return (handler, ctx);
    }

    private static RegisterCommand Command(
        string email = "sonia@exemple.tn", string? companyName = "Transports Sonia") =>
        new("Sonia", "Ben Salah", email, "MotDePasse#2026", companyName ?? string.Empty, "+216 20 000 000");

    [Fact]
    public async Task Inscription_cree_la_societe_ses_roles_et_l_utilisateur()
    {
        var (handler, ctx) = Setup();

        var response = await handler.Handle(Command(), CancellationToken.None);

        var societe = await ctx.Societes.SingleAsync();
        societe.Name.Should().Be("Transports Sonia");

        var roles = await ctx.Roles.Where(r => r.SocieteId == societe.Id).ToListAsync();
        roles.Should().HaveCount(2, "il faut un rôle administrateur ET un rôle non-admin, "
            + "sans quoi la création du premier employé n'a aucun rôle à lui donner");
        roles.Should().ContainSingle(r => r.IsCompanyAdmin);
        roles.Should().ContainSingle(r => !r.IsCompanyAdmin);

        var user = await ctx.Users.SingleAsync();
        user.CompanyId.Should().Be(societe.Id);
        user.Status.Should().Be("active");
        response.Token.Should().Be("jwt-token");
    }

    [Fact]
    public async Task Sans_nom_de_societe_la_societe_prend_le_nom_de_la_personne()
    {
        var (handler, ctx) = Setup();

        await handler.Handle(Command(companyName: null), CancellationToken.None);

        var societe = await ctx.Societes.SingleAsync();
        societe.Name.Should().Be("Sonia Ben Salah");
    }

    [Fact]
    public async Task L_abonnement_a_une_echeance_bornee_et_un_montant()
    {
        // NON-RÉGRESSION : sans SubscriptionExpiresAt, la société n'est JAMAIS
        // bloquée, n'apparaît pas dans la supervision, et l'écran de paiement
        // n'a rien à faire respecter — l'inscription offrirait un accès gratuit
        // et perpétuel.
        var (handler, ctx) = Setup();
        var avant = DateTime.UtcNow;

        await handler.Handle(Command(), CancellationToken.None);

        var societe = await ctx.Societes.SingleAsync();
        societe.SubscriptionTypeId.Should().NotBeNull("une société sans plan échappe à tout contrôle de droits");
        societe.SubscriptionExpiresAt.Should().NotBeNull();
        societe.SubscriptionExpiresAt!.Value.Should().BeCloseTo(avant.AddDays(14), TimeSpan.FromMinutes(5));
        societe.SubscriptionStartedAt.Should().BeCloseTo(avant, TimeSpan.FromMinutes(5));
        societe.SubscriptionStatus.Should().Be("active",
            "les écrans d'administration filtrent sur cette valeur exacte ; c'est l'échéance qui porte l'essai");
        societe.NextPaymentAmount.Should().Be(1200m, "l'écran de paiement lit ce montant");
    }

    [Fact]
    public async Task Le_plan_est_resolu_par_code_et_non_pris_au_hasard()
    {
        var (handler, ctx) = Setup();
        // Un second plan, plus complet, créé APRÈS : l'ancien code prenait
        // « le premier plan actif venu », sans ORDER BY.
        ctx.SubscriptionTypes.Add(new SubscriptionType
        {
            Id = 99, Name = "Premium", Code = "plan-premium",
            YearlyPrice = 9999, MaxVehicles = 200, IsActive = true
        });
        await ctx.SaveChangesAsync();

        await handler.Handle(Command(), CancellationToken.None);

        var societe = await ctx.Societes.SingleAsync();
        var plan = await ctx.SubscriptionTypes.SingleAsync(p => p.Id == societe.SubscriptionTypeId);
        plan.Code.Should().Be(PlanCode, "le plan doit venir de la configuration, jamais du hasard ni du visiteur");
    }

    [Fact]
    public async Task Le_jeton_de_rafraichissement_est_persiste()
    {
        // NON-RÉGRESSION : il était renvoyé au client sans jamais être enregistré,
        // donc le nouvel inscrit était déconnecté au premier renouvellement.
        var (handler, ctx) = Setup();

        var response = await handler.Handle(Command(), CancellationToken.None);

        var stored = await ctx.RefreshTokens.SingleAsync();
        stored.Token.Should().Be(response.RefreshToken);
        stored.RevokedAt.Should().BeNull();
        stored.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Le_fondateur_a_les_droits_sur_sa_propre_societe()
    {
        // Les valeurs par défaut de l'entité laissent plusieurs droits à faux :
        // le créateur se serait retrouvé bridé sur ses propres données.
        var (handler, ctx) = Setup();

        var response = await handler.Handle(Command(), CancellationToken.None);

        var user = await ctx.Users.SingleAsync();
        user.AccessLevel.Should().Be("admin");
        user.CanReports.Should().BeTrue();
        user.CanVehicles.Should().BeTrue();
        user.CanSettings.Should().BeTrue();
        response.User.IsCompanyAdmin.Should().BeTrue();
        response.User.CompanyName.Should().Be("Transports Sonia",
            "les navigations doivent être renseignées avant de construire la réponse");
    }

    [Fact]
    public async Task Les_permissions_du_role_admin_sont_a_plat()
    {
        // L'ancien code produisait { "modules": { ... } }, une forme imbriquée que
        // personne dans l'application ne sait lire.
        var (handler, _) = Setup();

        var response = await handler.Handle(Command(), CancellationToken.None);

        response.User.Permissions.Should().NotBeNull();
        response.User.Permissions!.Should().ContainKey("vehicles");
        response.User.Permissions.Should().NotContainKey("modules");
    }

    [Fact]
    public async Task Un_email_deja_utilise_est_refuse()
    {
        var (handler, ctx) = Setup();
        ctx.Users.Add(TestDataBuilder.CreateUser(email: "sonia@exemple.tn"));
        await ctx.SaveChangesAsync();

        var act = () => handler.Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await ctx.Societes.CountAsync()).Should().Be(0, "aucune société ne doit rester derrière un refus");
    }

    [Fact]
    public async Task Un_email_qui_ne_differe_que_par_la_casse_est_refuse()
    {
        var (handler, ctx) = Setup();
        ctx.Users.Add(TestDataBuilder.CreateUser(email: "sonia@exemple.tn"));
        await ctx.SaveChangesAsync();

        var act = () => handler.Handle(Command(email: "Sonia@Exemple.TN"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task L_email_est_enregistre_en_minuscules()
    {
        var (handler, ctx) = Setup();

        await handler.Handle(Command(email: "Sonia@Exemple.TN"), CancellationToken.None);

        (await ctx.Users.SingleAsync()).Email.Should().Be("sonia@exemple.tn");
    }

    [Fact]
    public async Task Sans_plan_configure_l_inscription_est_refusee_sans_rien_creer()
    {
        // Le garde-fou qui empêche de recréer une société sans abonnement — le trou
        // fermé côté création administrateur n'a aucune raison de rouvrir ici.
        var (handler, ctx) = Setup(withPlan: false);

        var act = () => handler.Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await ctx.Societes.CountAsync()).Should().Be(0);
        (await ctx.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Un_plan_desactive_n_est_pas_utilise()
    {
        var (handler, ctx) = Setup(planActive: false);

        var act = () => handler.Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await ctx.Societes.CountAsync()).Should().Be(0);
    }
}
