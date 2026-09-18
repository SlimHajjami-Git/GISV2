using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Auth;

/// <summary>
/// DEF-030 (recette du 16/09/2026) : dix mots de passe faux d'affilée ne laissaient AUCUNE
/// trace — zéro ligne dans audit_logs, et dans le journal applicatif une erreur générique
/// sans adresse ni IP. Une force brute passait donc inaperçue.
///
/// Ce qui est vérifié : chaque refus écrit une ligne « login_failed » et un avertissement
/// avec le motif et l'IP ; la réponse, elle, ne change pas (adresse inconnue et mot de passe
/// faux gardent le même message) ; l'adresse n'apparaît jamais en clair dans le journal.
/// </summary>
public class LoginFailureAuditTests
{
    private const string Ip = "198.51.100.23";
    private const string UserAgent = "Mozilla/5.0 (recette)";

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static (LoginCommandHandler Handler, TestGisDbContext Ctx, CapturingLogger<LoginCommandHandler> Logger)
        Setup(bool passwordMatches, string status = "active",
            Action<GisAPI.Domain.Entities.Societe>? configureSociete = null)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        var societe = TestDataBuilder.CreateSociete(id: 7);
        configureSociete?.Invoke(societe);
        ctx.Societes.Add(societe);
        ctx.Roles.Add(new GisAPI.Domain.Entities.Role { Id = 1, Name = "Employé", SocieteId = 7 });
        var user = TestDataBuilder.CreateUser(id: 42, companyId: 7, email: "karim.hajjami@gmail.com");
        user.Status = status;
        ctx.Users.Add(user);
        // TestGisDbContext relie User.Societe par une clé fantôme : sans elle, l'Include
        // du handler (jointure interne) ne retrouve pas le compte.
        var societeFk = ctx.Model.FindEntityType(typeof(GisAPI.Domain.Entities.User))!
            .FindNavigation(nameof(GisAPI.Domain.Entities.User.Societe))!.ForeignKey.Properties.Single();
        ctx.Entry(user).Property(societeFk.Name).CurrentValue = 7;
        ctx.SaveChanges();

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(passwordMatches);

        var logger = new CapturingLogger<LoginCommandHandler>();
        var handler = new LoginCommandHandler(ctx, hasher.Object, new Mock<IJwtService>().Object, logger);
        return (handler, ctx, logger);
    }

    [Fact]
    public async Task Un_mot_de_passe_faux_ecrit_une_ligne_d_audit_et_un_avertissement()
    {
        var (handler, ctx, logger) = Setup(passwordMatches: false);

        var act = () => handler.Handle(
            new LoginCommand("Karim.Hajjami@gmail.com", "faux-1", Ip, UserAgent), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("Email ou mot de passe incorrect");

        var audit = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        audit.Action.Should().Be(LoginCommandHandler.FailedLoginAction);
        audit.UserId.Should().BeNull("une clé vers users rendrait le compte insupprimable");
        audit.CompanyId.Should().Be(7, "l'administrateur de la société doit voir les échecs sur ses comptes");
        audit.EntityType.Should().Be("User");
        audit.EntityId.Should().Be(42);
        audit.IpAddress.Should().Be(Ip);
        audit.UserAgent.Should().Be(UserAgent);
        audit.Description.Should().Be("Échec de connexion : mot de passe incorrect");

        var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain("ka***@gmail.com").And.Contain(Ip).And.Contain("mot de passe incorrect");
        logger.Entries.Should().NotContain(e => e.Message.Contains("karim.hajjami", StringComparison.OrdinalIgnoreCase),
            "l'adresse complète ne doit pas partir dans les journaux du pod");
    }

    [Fact]
    public async Task Une_adresse_inconnue_est_auditee_masquee_sans_compte_ni_societe()
    {
        var (handler, ctx, logger) = Setup(passwordMatches: false);

        var act = () => handler.Handle(
            new LoginCommand("inconnu@example.invalid", "Faux@2026", Ip, UserAgent), CancellationToken.None);

        // Même message que le mot de passe faux : la connexion ne dit pas qui a un compte.
        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("Email ou mot de passe incorrect");

        var audit = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        audit.Action.Should().Be(LoginCommandHandler.FailedLoginAction);
        audit.UserId.Should().BeNull();
        audit.CompanyId.Should().BeNull("seul l'administrateur système voit les tentatives sur des adresses inconnues");
        audit.EntityName.Should().Be("in***@example.invalid");
        audit.Description.Should().Be("Échec de connexion : adresse inconnue");

        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning
                                                   && e.Message.Contains("adresse inconnue")
                                                   && e.Message.Contains(Ip));
    }

    [Theory]
    [InlineData("inactive", "compte désactivé", "Compte désactivé")]
    [InlineData("pending", "adresse non confirmée", "Votre adresse email n'est pas encore confirmée*")]
    public async Task Un_compte_bloque_est_audite_avec_son_motif(string status, string motif, string message)
    {
        var (handler, ctx, _) = Setup(passwordMatches: true, status: status);

        var act = () => handler.Handle(
            new LoginCommand("karim.hajjami@gmail.com", "Bon@2026", Ip, UserAgent), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>()).WithMessage(message);

        var audit = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        audit.Action.Should().Be(LoginCommandHandler.FailedLoginAction);
        audit.Description.Should().Be($"Échec de connexion : {motif}");
    }

    [Fact]
    public async Task Une_societe_suspendue_est_auditee_avec_son_motif()
    {
        var (handler, ctx, logger) = Setup(passwordMatches: true,
            configureSociete: s => s.SubscriptionStatus = "suspended");

        var act = () => handler.Handle(
            new LoginCommand("karim.hajjami@gmail.com", "Bon@2026", Ip, UserAgent), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("L'abonnement de votre société est suspendu*");

        var audit = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        audit.Action.Should().Be(LoginCommandHandler.FailedLoginAction);
        audit.CompanyId.Should().Be(7);
        audit.Description.Should().Be("Échec de connexion : abonnement suspendu");
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("abonnement suspendu"));
    }

    [Fact]
    public async Task Une_societe_expiree_au_dela_de_la_grace_est_auditee_avec_son_motif()
    {
        var (handler, ctx, _) = Setup(passwordMatches: true, configureSociete: s =>
        {
            s.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(-(GisAPI.Application.Common.SubscriptionPolicy.GraceDays + 5));
            s.AutoSuspendEnabled = true;
        });

        var act = () => handler.Handle(
            new LoginCommand("karim.hajjami@gmail.com", "Bon@2026", Ip, UserAgent), CancellationToken.None);

        (await act.Should().ThrowAsync<DomainException>())
            .WithMessage("L'abonnement de votre société a expiré*");

        var audit = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        audit.Description.Should().Be("Échec de connexion : abonnement expiré");
    }

    [Fact]
    public async Task Un_mot_de_passe_tape_dans_le_champ_e_mail_n_apparait_ni_dans_le_journal_ni_dans_l_audit()
    {
        // Constat de relecture : « Motdepasse@2026 » passe le validateur (un seul @) et
        // l'ancien masque en gardait « mo***@2026 » dans le journal du pod et dans audit_logs.
        var (handler, ctx, logger) = Setup(passwordMatches: false);

        var act = () => handler.Handle(
            new LoginCommand("Motdepasse@2026", "", Ip, UserAgent), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>();

        var audit = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        audit.EntityName.Should().Be("***");
        logger.Entries.Should().NotContain(e => e.Message.Contains("2026") || e.Message.Contains("mo***"));
    }

    [Fact]
    public async Task Dix_echecs_immediats_laissent_dix_lignes_d_audit()
    {
        // La reproduction de la fiche : dix mots de passe faux d'affilée.
        var (handler, ctx, _) = Setup(passwordMatches: false);

        for (var i = 1; i <= 10; i++)
        {
            var n = i;
            var act = () => handler.Handle(
                new LoginCommand("karim.hajjami@gmail.com", $"faux-{n}", Ip, UserAgent), CancellationToken.None);
            await act.Should().ThrowAsync<DomainException>();
        }

        (await ctx.AuditLogs.CountAsync(a => a.Action == LoginCommandHandler.FailedLoginAction))
            .Should().Be(10);
    }

    /// <summary>
    /// Relecture de DEF-030 : la ligne login_failed portait UserId = id du compte, or
    /// FK_audit_logs_users_UserId est en NO ACTION et aucune suppression d'utilisateur ne
    /// purge audit_logs. Un seul mot de passe mal tapé rendait le compte insupprimable
    /// (23503, donc 500). Les clés étrangères sont activées pour la suppression, comme en
    /// production, et le contexte est vidé : rien ne délie la ligne d'audit côté EF.
    /// </summary>
    [Theory]
    [InlineData("utilisateurs de la société")]
    [InlineData("employés")]
    [InlineData("administration système")]
    public async Task Un_compte_ayant_un_echec_de_connexion_reste_supprimable(string ecran)
    {
        var (handler, ctx, _) = Setup(passwordMatches: false);
        var act = () => handler.Handle(
            new LoginCommand("karim.hajjami@gmail.com", "faux-1", Ip, UserAgent), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>();

        // L'écran Employés ne supprime que les fiches employé (EmployeeRole renseigné).
        if (ecran == "employés")
        {
            ctx.ChangeTracker.Clear();
            (await ctx.Users.SingleAsync(u => u.Id == 42)).EmployeeRole = "driver";
            await ctx.SaveChangesAsync();
        }

        ctx.ChangeTracker.Clear();
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 7, userId: 1).Object;
        Func<Task> delete = ecran switch
        {
            "utilisateurs de la société" => () => new GisAPI.Application.Features.Users.Commands.DeleteUser
                .DeleteUserCommandHandler(ctx, tenant)
                .Handle(new GisAPI.Application.Features.Users.Commands.DeleteUser.DeleteUserCommand(42), CancellationToken.None),
            "employés" => () => new GisAPI.Application.Features.Employees.Commands.DeleteEmployee
                .DeleteEmployeeCommandHandler(ctx, tenant)
                .Handle(new GisAPI.Application.Features.Employees.Commands.DeleteEmployee.DeleteEmployeeCommand(42), CancellationToken.None),
            _ => () => new GisAPI.Application.Features.Admin.Users.Commands.DeleteAdminUser
                .DeleteAdminUserCommandHandler(ctx, tenant)
                .Handle(new GisAPI.Application.Features.Admin.Users.Commands.DeleteAdminUser.DeleteAdminUserCommand(42), CancellationToken.None)
        };

        await delete.Should().NotThrowAsync();

        ctx.ChangeTracker.Clear();
        (await ctx.Users.AnyAsync(u => u.Id == 42)).Should().BeFalse();
        // La trace survit au compte, toujours rattachée à la société et à l'id supprimé.
        var audit = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        audit.Action.Should().Be(LoginCommandHandler.FailedLoginAction);
        audit.CompanyId.Should().Be(7);
        audit.EntityId.Should().Be(42);
        audit.EntityName.Should().Be("karim.hajjami@gmail.com");
    }

    [Fact]
    public async Task Un_user_agent_demesure_est_borne_dans_l_audit()
    {
        var (handler, ctx, _) = Setup(passwordMatches: false);
        var huge = new string('x', 5000);

        var act = () => handler.Handle(
            new LoginCommand("karim.hajjami@gmail.com", "faux", Ip, huge), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>();

        var audit = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        audit.UserAgent!.Length.Should().Be(250);
    }

    [Theory]
    [InlineData("karim.hajjami@gmail.com", "ka***@gmail.com")]
    [InlineData("  Sonia@Exemple.tn ", "so***@exemple.tn")]
    [InlineData("ali@x.tn", "a***@x.tn")]
    [InlineData("", "(vide)")]
    [InlineData(null, "(vide)")]
    // Hors forme d'adresse, rien de visible : c'est souvent un mot de passe mal placé.
    [InlineData("sans-arobase", "***")]
    [InlineData("Admin@2026", "***")]
    [InlineData("Motdepasse@2026", "***")]
    [InlineData("motdepasse@x", "***")]
    [InlineData("@gmail.com", "***")]
    [InlineData("secret@", "***")]
    public void Le_masque_ne_laisse_voir_que_le_debut_et_le_domaine(string? email, string expected)
    {
        LoginCommandHandler.MaskEmail(email).Should().Be(expected);
    }

    [Fact]
    public void Le_masque_borne_une_saisie_demesuree()
    {
        // Adresse longue mais plausible : tronquée.
        LoginCommandHandler.MaskEmail("abcdef@" + new string('d', 200) + ".tn").Length.Should().Be(80);
        // Au-delà de la longueur maximale d'une adresse : rien de visible.
        LoginCommandHandler.MaskEmail("abcdef@" + new string('d', 500) + ".tn").Should().Be("***");
    }
}
