using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Users.Commands.CreateUser;
using GisAPI.Application.Features.Users.Commands.UpdateProfile;
using GisAPI.Application.Features.Users.Commands.UpdateUser;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Users;

/// <summary>
/// Unicité de l'e-mail de connexion (HERTZ, 03/10/2026).
///
/// L'administrateur de HERTZ créait un compte avec une adresse déjà portée par un compte
/// de PARENIN. Le contrôle d'unicité interrogeait `_context.Users`, filtré par société :
/// il ne voyait pas l'autre société, laissait passer, et c'est l'index UNIQUE
/// `idx_users_email` — qui, lui, couvre toute la table — qui refusait l'insertion.
/// Le 23505 ressortait en « An unexpected error occurred » : le client voyait une panne
/// au lieu de la seule chose à corriger, l'adresse.
///
/// PORTÉE de ce fichier : SQLite ne porte pas le filtre par société (le modèle EF est mis
/// en cache par type de contexte, un filtre ici se figerait selon le premier test qui
/// construit le modèle). Il verrouille donc tout le reste du contrat — casse, espaces de
/// bord, contrôle absent de la modification — et la reproduction fidèle du cas HERTZ,
/// filtre ET index UNIQUE réunis, vit dans UniciteEmailPostgresTests.
/// </summary>
public class UniciteEmailTests
{
    private const int MaSociete = 7;
    private const int AutreSociete = 99;
    private const int AdminId = 1;
    private const int RoleEmploye = 1;

    /// <summary>
    /// Une base avec DEUX sociétés : la mienne et une autre dont les comptes me sont
    /// invisibles. `prisAilleurs` est l'adresse déjà utilisée par l'autre société.
    /// </summary>
    private static async Task<TestGisDbContext> ParcAsync(string prisAilleurs = "aymen@parenin.tn")
    {
        var ctx = TestDbContextFactory.CreateWithTenant(MaSociete, AdminId);

        ctx.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: MaSociete));
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: AutreSociete));
        ctx.Roles.Add(new Role { Id = RoleEmploye, Name = "Employé", SocieteId = MaSociete, IsCompanyAdmin = false });
        ctx.Roles.Add(new Role { Id = 2, Name = "Admin", SocieteId = MaSociete, IsCompanyAdmin = true });
        ctx.Users.Add(TestDataBuilder.CreateUser(id: AdminId, companyId: MaSociete, email: "admin@hertz.tn"));
        ctx.Users.Add(TestDataBuilder.CreateUser(id: 50, companyId: AutreSociete, email: prisAilleurs));

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static CreateUserCommandHandler Creation(TestGisDbContext ctx)
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hash");
        return new CreateUserCommandHandler(
            ctx, TestDbContextFactory.CreateMockTenantService(MaSociete, AdminId).Object,
            hasher.Object, Mock.Of<IPublisher>());
    }

    private static CreateUserCommand Commande(string email) =>
        new("ste", "parnin", email, "92273151", "Secret123!", RoleEmploye);

    private static UpdateUserCommandHandler Modification(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(MaSociete, AdminId).Object);

    private static UpdateProfileCommandHandler Profil(TestGisDbContext ctx, int userId) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(MaSociete, userId).Object);

    // ── Création ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creer_un_compte_avec_un_email_pris_par_une_AUTRE_societe_est_refuse_proprement()
    {
        using var ctx = await ParcAsync();

        var refus = await Assert.ThrowsAsync<ConflictException>(
            () => Creation(ctx).Handle(Commande("aymen@parenin.tn"), CancellationToken.None));

        refus.Message.Should().Be("Cet email est déjà utilisé");
        refus.Message.Should().NotContain("parenin", "le message ne dit jamais QUI détient l'adresse");
        ctx.ChangeTracker.Clear();
        (await ctx.Users.IgnoreQueryFilters().CountAsync()).Should().Be(2, "rien n'a été écrit");
    }

    [Fact]
    public async Task Creer_un_compte_avec_un_email_pris_dans_SA_societe_est_refuse()
    {
        using var ctx = await ParcAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => Creation(ctx).Handle(Commande("admin@hertz.tn"), CancellationToken.None));
    }

    [Fact]
    public async Task Un_email_qui_ne_differe_que_par_la_casse_est_refuse()
    {
        using var ctx = await ParcAsync();

        // La connexion compare l'e-mail sans la casse ; laisser coexister les deux
        // reviendrait à servir un compte au hasard à l'ouverture de session.
        await Assert.ThrowsAsync<ConflictException>(
            () => Creation(ctx).Handle(Commande("Aymen@Parenin.TN"), CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(
            () => Creation(ctx).Handle(Commande("ADMIN@hertz.tn"), CancellationToken.None));
    }

    [Fact]
    public async Task Une_adresse_libre_passe_et_s_enregistre_sans_les_espaces_de_bord()
    {
        using var ctx = await ParcAsync();

        var dto = await Creation(ctx).Handle(Commande("  nouveau@hertz.tn  "), CancellationToken.None);

        dto.Id.Should().BeGreaterThan(0);
        ctx.ChangeTracker.Clear();
        var compte = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == dto.Id);
        compte.Email.Should().Be("nouveau@hertz.tn", "un e-mail avec un espace de bord ne se reconnecte jamais");
    }

    // ── Modification par l'administrateur ─────────────────────────────────────

    [Fact]
    public async Task Reprendre_l_email_d_une_AUTRE_societe_en_modifiant_un_compte_est_refuse()
    {
        using var ctx = await ParcAsync();
        var dto = await Creation(ctx).Handle(Commande("collegue@hertz.tn"), CancellationToken.None);
        ctx.ChangeTracker.Clear();

        var refus = await Assert.ThrowsAsync<ConflictException>(
            () => Modification(ctx).Handle(
                new UpdateUserCommand(dto.Id, "ste", "parnin", "aymen@parenin.tn", null, null, null),
                CancellationToken.None));

        refus.Message.Should().Be("Cet email est déjà utilisé");
        ctx.ChangeTracker.Clear();
        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == dto.Id)).Email
            .Should().Be("collegue@hertz.tn", "le refus ne laisse rien derrière lui");
    }

    [Fact]
    public async Task Enregistrer_un_compte_sans_toucher_a_son_email_reste_possible()
    {
        using var ctx = await ParcAsync();
        var dto = await Creation(ctx).Handle(Commande("collegue@hertz.tn"), CancellationToken.None);
        ctx.ChangeTracker.Clear();

        // Son propre e-mail ne doit pas se refuser lui-même.
        await Modification(ctx).Handle(
            new UpdateUserCommand(dto.Id, "Stéphane", "Parnin", "collegue@hertz.tn", "99000000", null, null),
            CancellationToken.None);

        ctx.ChangeTracker.Clear();
        var compte = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == dto.Id);
        (compte.FirstName, compte.Email).Should().Be(("Stéphane", "collegue@hertz.tn"));
    }

    // ── Modification de son propre profil ─────────────────────────────────────

    [Fact]
    public async Task Changer_son_profil_pour_l_email_d_une_AUTRE_societe_est_refuse()
    {
        using var ctx = await ParcAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => Profil(ctx, AdminId).Handle(
                new UpdateProfileCommand("Ahmed", "BEN ALI", "aymen@parenin.tn", null),
                CancellationToken.None));
    }

    [Fact]
    public async Task Enregistrer_son_profil_sans_changer_d_email_reste_possible()
    {
        using var ctx = await ParcAsync();

        var dto = await Profil(ctx, AdminId).Handle(
            new UpdateProfileCommand("Ahmed", "BEN ALI", "admin@hertz.tn", "92000000"),
            CancellationToken.None);

        dto.Email.Should().Be("admin@hertz.tn");
    }
}
