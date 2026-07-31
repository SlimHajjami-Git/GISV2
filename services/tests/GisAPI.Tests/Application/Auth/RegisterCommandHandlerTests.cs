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

    private static (RegisterCommandHandler handler, TestGisDbContext ctx, Mock<IEmailService> mailer) Setup(
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

        var mailer = new Mock<IEmailService>();
        mailer.Setup(e => e.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        AppRegistration.DefaultPlanCode = PlanCode;
        AppRegistration.TrialDays = 14;
        AppRegistration.EmailConfirmationHours = 48;

        var handler = new RegisterCommandHandler(
            ctx, hasher.Object, mailer.Object,
            NullLogger<RegisterCommandHandler>.Instance);

        return (handler, ctx, mailer);
    }

    private static RegisterCommand Command(
        string email = "sonia@exemple.tn",
        string? companyName = "Transports Sonia",
        string accountType = AccountTypes.Company) =>
        new("Sonia", "Ben Salah", email, "MotDePasse#2026", companyName, "+216 20 000 000", accountType);

    [Fact]
    public async Task Inscription_cree_la_societe_ses_roles_et_l_utilisateur()
    {
        var (handler, ctx, _) = Setup();

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
        user.Status.Should().Be("pending", "le compte n'est utilisable qu'après confirmation de l'adresse");
        response.Email.Should().Be("sonia@exemple.tn");
    }

    [Fact]
    public async Task Un_particulier_obtient_un_espace_a_son_nom_sans_rien_declarer()
    {
        // Le point central : la société est une pièce de NOTRE modèle, pas une
        // question posée au visiteur. Un particulier n'a aucune entreprise à
        // nommer, et son espace prend simplement son nom.
        var (handler, ctx, _) = Setup();

        await handler.Handle(
            Command(companyName: null, accountType: AccountTypes.Individual),
            CancellationToken.None);

        var societe = await ctx.Societes.SingleAsync();
        societe.Name.Should().Be("Sonia Ben Salah");
        societe.Type.Should().Be("autre");
    }

    [Fact]
    public async Task Un_particulier_qui_saisirait_un_nom_de_societe_le_voit_ignore()
    {
        // Le champ n'existe pas dans le formulaire pour un particulier : s'il
        // arrive quand même, c'est du bruit et il ne doit pas renommer l'espace.
        var (handler, ctx, _) = Setup();

        await handler.Handle(
            Command(companyName: "Société Fantôme", accountType: AccountTypes.Individual),
            CancellationToken.None);

        (await ctx.Societes.SingleAsync()).Name.Should().Be("Sonia Ben Salah");
    }

    [Fact]
    public async Task Un_professionnel_obtient_un_espace_au_nom_de_son_entreprise()
    {
        var (handler, ctx, _) = Setup();

        await handler.Handle(
            Command(companyName: "Transports Sonia", accountType: AccountTypes.Company),
            CancellationToken.None);

        var societe = await ctx.Societes.SingleAsync();
        societe.Name.Should().Be("Transports Sonia");
        societe.Type.Should().Be("transport");
    }

    [Fact]
    public async Task L_abonnement_a_une_echeance_bornee_et_un_montant()
    {
        // NON-RÉGRESSION : sans SubscriptionExpiresAt, la société n'est JAMAIS
        // bloquée, n'apparaît pas dans la supervision, et l'écran de paiement
        // n'a rien à faire respecter — l'inscription offrirait un accès gratuit
        // et perpétuel.
        var (handler, ctx, _) = Setup();
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
        var (handler, ctx, _) = Setup();
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
    public async Task Aucune_session_n_est_ouverte_a_l_inscription()
    {
        // Renvoyer un jeton ici viderait la confirmation d'adresse de son sens :
        // l'inscrit entrerait sans jamais avoir prouvé qu'il possède l'adresse.
        var (handler, ctx, _) = Setup();

        await handler.Handle(Command(), CancellationToken.None);

        (await ctx.RefreshTokens.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Un_jeton_de_confirmation_borne_est_pose_et_l_email_part()
    {
        var (handler, ctx, mailer) = Setup();
        var avant = DateTime.UtcNow;

        var response = await handler.Handle(Command(), CancellationToken.None);

        var user = await ctx.Users.SingleAsync();
        user.EmailVerificationToken.Should().NotBeNullOrWhiteSpace();
        user.EmailVerificationExpiresAt.Should().NotBeNull();
        user.EmailVerificationExpiresAt!.Value.Should().BeCloseTo(avant.AddHours(48), TimeSpan.FromMinutes(5));

        response.EmailSent.Should().BeTrue();
        mailer.Verify(e => e.SendEmailAsync(
            "sonia@exemple.tn", It.IsAny<string>(), It.IsAny<string>(),
            It.Is<string>(html => html.Contains(user.EmailVerificationToken!)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Une_panne_de_messagerie_ne_perd_pas_l_inscription_mais_le_dit()
    {
        var (handler, ctx, mailer) = Setup();
        mailer.Setup(e => e.SendEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP injoignable"));

        var response = await handler.Handle(Command(), CancellationToken.None);

        response.EmailSent.Should().BeFalse("l'écran doit proposer un renvoi plutôt que « vérifiez votre boîte »");
        (await ctx.Users.CountAsync()).Should().Be(1, "le compte reste créé et confirmable");
    }

    [Fact]
    public async Task Le_fondateur_a_les_droits_sur_sa_propre_societe()
    {
        // Les valeurs par défaut de l'entité laissent plusieurs droits à faux :
        // le créateur se serait retrouvé bridé sur ses propres données.
        var (handler, ctx, _) = Setup();

        await handler.Handle(Command(), CancellationToken.None);

        var user = await ctx.Users.SingleAsync();
        user.AccessLevel.Should().Be("admin");
        user.CanReports.Should().BeTrue();
        user.CanVehicles.Should().BeTrue();
        user.CanSettings.Should().BeTrue();
    }

    [Fact]
    public async Task Les_permissions_du_role_admin_sont_a_plat()
    {
        // L'ancien code produisait { "modules": { ... } }, une forme imbriquée que
        // personne dans l'application ne sait lire.
        var (handler, ctx, _) = Setup();

        await handler.Handle(Command(), CancellationToken.None);

        var adminRole = await ctx.Roles.SingleAsync(r => r.IsCompanyAdmin);
        adminRole.Permissions.Should().NotBeNull();
        adminRole.Permissions!.Should().ContainKey("vehicles");
        adminRole.Permissions.Should().NotContainKey("modules");
    }

    [Fact]
    public async Task Un_email_deja_utilise_est_refuse()
    {
        var (handler, ctx, _) = Setup();
        ctx.Users.Add(TestDataBuilder.CreateUser(email: "sonia@exemple.tn"));
        await ctx.SaveChangesAsync();

        var act = () => handler.Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        (await ctx.Societes.CountAsync()).Should().Be(0, "aucune société ne doit rester derrière un refus");
    }

    [Fact]
    public async Task Un_email_qui_ne_differe_que_par_la_casse_est_refuse()
    {
        var (handler, ctx, _) = Setup();
        ctx.Users.Add(TestDataBuilder.CreateUser(email: "sonia@exemple.tn"));
        await ctx.SaveChangesAsync();

        var act = () => handler.Handle(Command(email: "Sonia@Exemple.TN"), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task L_email_est_enregistre_en_minuscules()
    {
        var (handler, ctx, _) = Setup();

        await handler.Handle(Command(email: "Sonia@Exemple.TN"), CancellationToken.None);

        (await ctx.Users.SingleAsync()).Email.Should().Be("sonia@exemple.tn");
    }

    [Fact]
    public async Task Sans_plan_configure_l_inscription_est_refusee_sans_rien_creer()
    {
        // Le garde-fou qui empêche de recréer une société sans abonnement — le trou
        // fermé côté création administrateur n'a aucune raison de rouvrir ici.
        var (handler, ctx, _) = Setup(withPlan: false);

        var act = () => handler.Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await ctx.Societes.CountAsync()).Should().Be(0);
        (await ctx.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Un_plan_desactive_n_est_pas_utilise()
    {
        var (handler, ctx, _) = Setup(planActive: false);

        var act = () => handler.Handle(Command(), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        (await ctx.Societes.CountAsync()).Should().Be(0);
    }
}
