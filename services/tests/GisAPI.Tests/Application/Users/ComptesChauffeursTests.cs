using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Application.Features.Drivers.Commands;
using GisAPI.Application.Features.Users;
using GisAPI.Application.Features.Users.Commands.CreateUser;
using GisAPI.Application.Features.Users.Commands.UpdateUser;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Users;

/// <summary>
/// Comptes chauffeurs (migration 050, décision de Slim du 21/09/2026 : « le chauffeur est
/// un utilisateur »). Un administrateur crée l'utilisateur en cochant « Chauffeur » ; ce
/// compte se connecte à l'application mobile pour recevoir ses tournées, et à rien d'autre.
///
/// Contrat vérifié ici : aucun droit de gestion, jamais administrateur, hors quota,
/// aucune affectation de véhicule (audience des alertes), fiche chauffeur reliée ou créée,
/// connexion refusée hors application mobile, session mobile de 90 jours, accès fermé
/// quand la fiche est supprimée.
/// </summary>
public class ComptesChauffeursTests
{
    private const int CompanyId = 7;
    private const int AdminId = 1;
    private const int RoleEmploye = 1;
    private const int RoleAdmin = 2;

    private static async Task<TestGisDbContext> ParcAsync(int maxUsers = 10)
    {
        var ctx = TestDbContextFactory.Create();
        var plan = TestDataBuilder.CreateSubscriptionType();
        plan.MaxUsers = maxUsers;
        ctx.SubscriptionTypes.Add(plan);
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Roles.Add(new Role { Id = RoleEmploye, Name = "Employé", SocieteId = CompanyId, IsCompanyAdmin = false });
        ctx.Roles.Add(new Role { Id = RoleAdmin, Name = "Admin", SocieteId = CompanyId, IsCompanyAdmin = true });
        var admin = TestDataBuilder.CreateUser(id: AdminId, companyId: CompanyId, email: "admin@test.com");
        admin.RoleId = RoleAdmin;
        ctx.Users.Add(admin);
        ctx.Vehicles.Add(TestDataBuilder.CreateVehicle(id: 5, companyId: CompanyId));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static CreateUserCommandHandler Creation(TestGisDbContext ctx)
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hash");
        return new CreateUserCommandHandler(
            ctx, TestDbContextFactory.CreateMockTenantService(CompanyId, AdminId).Object,
            hasher.Object, Mock.Of<IPublisher>());
    }

    private static CreateUserCommand Commande(string email, bool chauffeur, bool admin = false, int[]? vehicules = null) =>
        new("Ali", "Ben Salah", email, "+21620000000", "Secret123!", RoleEmploye,
            AssignedVehicleIds: vehicules, IsCompanyAdmin: admin, IsDriverAccount: chauffeur,
            CanTours: true, CanMonitoring: true, CanVehicles: true);

    // ── Création ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creer_un_chauffeur_ne_lui_laisse_aucun_droit_et_cree_sa_fiche()
    {
        using var ctx = await ParcAsync();

        var dto = await Creation(ctx).Handle(
            Commande("ali@test.com", chauffeur: true, admin: true, vehicules: new[] { 5 }), CancellationToken.None);

        dto.AccountType.Should().Be(UserAccountTypes.Driver);
        dto.IsCompanyAdmin.Should().BeFalse("un chauffeur n'est jamais administrateur, même si la case est cochée");

        ctx.ChangeTracker.Clear();
        var compte = await ctx.Users.AsNoTracking().SingleAsync(u => u.Email == "ali@test.com");
        compte.AccountType.Should().Be(UserAccountTypes.Driver);
        compte.RoleId.Should().Be(RoleEmploye);
        compte.AccessLevel.Should().Be("user");
        (compte.CanTours, compte.CanMonitoring, compte.CanVehicles, compte.CanReportTrips)
            .Should().Be((false, false, false, false), "aucune case, quoi que dise le formulaire");
        (await ctx.UserVehicles.CountAsync(uv => uv.UserId == compte.Id)).Should().Be(0,
            "une affectation ferait de lui un destinataire des alertes du véhicule");

        var fiche = await ctx.Drivers.AsNoTracking().SingleAsync();
        fiche.UserId.Should().Be(compte.Id);
        (fiche.FirstName, fiche.LastName, fiche.Email, fiche.CompanyId).Should().Be(("Ali", "Ben Salah", "ali@test.com", CompanyId));
    }

    [Fact]
    public async Task Creer_un_chauffeur_relie_la_fiche_existante_qui_porte_son_email()
    {
        using var ctx = await ParcAsync();
        ctx.Drivers.Add(new Driver { Id = 30, CompanyId = CompanyId, FirstName = "A.", LastName = "BEN SALAH", Email = "ALI@test.com", AssignedVehicleId = 5, PermitNumber = "P-77" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var dto = await Creation(ctx).Handle(Commande("ali@test.com", chauffeur: true), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.Drivers.CountAsync()).Should().Be(1, "pas de seconde fiche");
        var fiche = await ctx.Drivers.AsNoTracking().SingleAsync();
        fiche.UserId.Should().Be(dto.Id);
        fiche.AssignedVehicleId.Should().Be(5, "la fiche garde son véhicule et son permis");
        fiche.PermitNumber.Should().Be("P-77");
    }

    [Fact]
    public async Task Un_chauffeur_ne_compte_pas_dans_le_quota_et_n_est_pas_compte()
    {
        // Plan à 2 places : l'admin + un salarié = plein. Un chauffeur passe quand même,
        // et un chauffeur déjà là ne bloque pas le salarié suivant.
        using var ctx = await ParcAsync(maxUsers: 2);
        var chauffeurExistant = TestDataBuilder.CreateUser(id: 40, companyId: CompanyId, email: "c1@test.com");
        chauffeurExistant.AccountType = UserAccountTypes.Driver;
        ctx.Users.Add(chauffeurExistant);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // 1 salarié (admin) + 1 chauffeur : il reste une place de salarié.
        await Creation(ctx).Handle(Commande("s1@test.com", chauffeur: false), CancellationToken.None);
        // Plein côté salariés : un chauffeur passe encore…
        await Creation(ctx).Handle(Commande("c2@test.com", chauffeur: true), CancellationToken.None);
        // … pas un salarié de plus.
        var refus = async () => await Creation(ctx).Handle(Commande("s2@test.com", chauffeur: false), CancellationToken.None);
        await refus.Should().ThrowAsync<DomainException>().WithMessage("*Limite d'utilisateurs atteinte*");
    }

    // ── Modification ───────────────────────────────────────────────────────────

    private static UpdateUserCommandHandler Modification(TestGisDbContext ctx) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId, AdminId).Object);

    private static UpdateUserCommand Modif(int id, string email, bool? chauffeur, bool? admin = null, int[]? vehicules = null) =>
        new(id, "Ali", "Ben Salah", email, "+21699999999", RoleEmploye, "active",
            AssignedVehicleIds: vehicules, IsCompanyAdmin: admin, IsDriverAccount: chauffeur, CanTours: true);

    [Fact]
    public async Task Passer_un_salarie_en_chauffeur_lui_retire_droits_et_affectations()
    {
        using var ctx = await ParcAsync();
        var salarie = TestDataBuilder.CreateUser(id: 50, companyId: CompanyId, email: "s@test.com");
        salarie.CanTours = true;
        ctx.Users.Add(salarie);
        ctx.UserVehicles.Add(new UserVehicle { UserId = 50, VehicleId = 5 });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await Modification(ctx).Handle(Modif(50, "s@test.com", chauffeur: true, admin: true, vehicules: new[] { 5 }), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        var compte = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == 50);
        (compte.AccountType, compte.CanTours, compte.RoleId).Should().Be((UserAccountTypes.Driver, false, RoleEmploye));
        (await ctx.UserVehicles.CountAsync(uv => uv.UserId == 50)).Should().Be(0);
        (await ctx.Drivers.SingleAsync()).UserId.Should().Be(50);
    }

    [Fact]
    public async Task Repasser_un_chauffeur_en_salarie_garde_sa_fiche_mais_coupe_le_lien()
    {
        using var ctx = await ParcAsync();
        var chauffeur = TestDataBuilder.CreateUser(id: 51, companyId: CompanyId, email: "c@test.com");
        chauffeur.AccountType = UserAccountTypes.Driver;
        ctx.Users.Add(chauffeur);
        ctx.Drivers.Add(new Driver { Id = 31, CompanyId = CompanyId, UserId = 51, FirstName = "Ali", LastName = "Ben Salah", AssignedVehicleId = 5 });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await Modification(ctx).Handle(Modif(51, "c@test.com", chauffeur: false), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == 51)).AccountType.Should().Be(UserAccountTypes.Staff);
        var fiche = await ctx.Drivers.AsNoTracking().SingleAsync(d => d.Id == 31);
        fiche.UserId.Should().BeNull();
        fiche.AssignedVehicleId.Should().Be(5, "la fiche et son historique restent");
    }

    // ── Suppression de la fiche ────────────────────────────────────────────────

    [Fact]
    public async Task Supprimer_la_fiche_ferme_l_acces_du_compte_sans_le_supprimer()
    {
        using var ctx = await ParcAsync();
        var chauffeur = TestDataBuilder.CreateUser(id: 52, companyId: CompanyId, email: "c@test.com");
        chauffeur.AccountType = UserAccountTypes.Driver;
        ctx.Users.Add(chauffeur);
        ctx.Drivers.Add(new Driver { Id = 32, CompanyId = CompanyId, UserId = 52, FirstName = "Ali", LastName = "B" });
        ctx.RefreshTokens.Add(new RefreshToken { Id = 1, UserId = 52, Token = "rt", ExpiresAt = DateTime.UtcNow.AddDays(80), CreatedAt = DateTime.UtcNow });
        ctx.UserDeviceTokens.Add(new UserDeviceToken { Id = 1, UserId = 52, Token = "fcm", IsActive = true });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await new DeleteDriverCommandHandler(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId, AdminId).Object)
            .Handle(new DeleteDriverCommand(32), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.Drivers.CountAsync()).Should().Be(0);
        var compte = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == 52);
        compte.Status.Should().Be("inactive", "le compte reste pour l'audit, mais ne se connecte plus");
        (await ctx.RefreshTokens.SingleAsync()).RevokedAt.Should().NotBeNull("sa session de 90 jours est révoquée");
        (await ctx.UserDeviceTokens.SingleAsync()).IsActive.Should().BeFalse("plus aucune notification");
    }

    // ── Audience des alertes ───────────────────────────────────────────────────

    [Fact]
    public async Task Un_chauffeur_n_est_jamais_dans_l_audience_des_alertes()
    {
        using var ctx = await ParcAsync();
        var chauffeur = TestDataBuilder.CreateUser(id: 53, companyId: CompanyId, email: "c@test.com");
        chauffeur.AccountType = UserAccountTypes.Driver;
        chauffeur.RoleId = RoleAdmin;   // même avec un rôle admin posé par erreur
        ctx.Users.Add(chauffeur);
        ctx.UserVehicles.Add(new UserVehicle { UserId = 53, VehicleId = 5 });   // même affecté par erreur
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        (await NotificationAudience.ForVehicleAsync(ctx, CompanyId, 5)).Should().Equal(AdminId);
        (await NotificationAudience.CompanyAdminsAsync(ctx, CompanyId)).Should().Equal(AdminId);
    }

    // ── Connexion ──────────────────────────────────────────────────────────────

    private static LoginCommandHandler Connexion(TestGisDbContext ctx)
    {
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        var jwt = new Mock<IJwtService>();
        jwt.Setup(j => j.GenerateToken(It.IsAny<User>(), It.IsAny<string?>())).Returns("jwt");
        jwt.Setup(j => j.GenerateRefreshToken()).Returns("refresh");
        return new LoginCommandHandler(ctx, hasher.Object, jwt.Object, NullLogger<LoginCommandHandler>.Instance);
    }

    private static async Task<TestGisDbContext> ParcAvecChauffeurAsync()
    {
        var ctx = await ParcAsync();
        var chauffeur = TestDataBuilder.CreateUser(id: 54, companyId: CompanyId, email: "c@test.com");
        chauffeur.AccountType = UserAccountTypes.Driver;
        ctx.Users.Add(chauffeur);
        var societeFk = ctx.Model.FindEntityType(typeof(User))!
            .FindNavigation(nameof(User.Societe))!.ForeignKey.Properties.Single();
        ctx.Entry(chauffeur).Property(societeFk.Name).CurrentValue = CompanyId;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    [Fact]
    public async Task Un_chauffeur_ne_se_connecte_pas_depuis_le_site()
    {
        using var ctx = await ParcAvecChauffeurAsync();

        var web = async () => await Connexion(ctx).Handle(new LoginCommand("c@test.com", "x"), CancellationToken.None);

        await web.Should().ThrowAsync<DomainException>().WithMessage(LoginClients.DriverWebLoginRefused);
        (await ctx.AuditLogs.AsNoTracking().SingleAsync()).Action.Should().Be(LoginCommandHandler.FailedLoginAction);
    }

    [Fact]
    public async Task Un_chauffeur_se_connecte_depuis_l_application_et_garde_sa_session_90_jours()
    {
        using var ctx = await ParcAvecChauffeurAsync();

        var reponse = await Connexion(ctx).Handle(
            new LoginCommand("c@test.com", "x", ClientType: LoginClients.Mobile), CancellationToken.None);

        reponse.User.AccountType.Should().Be(UserAccountTypes.Driver, "l'application aiguille dessus");
        var session = await ctx.RefreshTokens.AsNoTracking().SingleAsync(t => t.UserId == 54);
        session.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(RefreshTokenLifetime.DriverDays), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Un_salarie_garde_sa_session_de_7_jours_et_se_connecte_partout()
    {
        using var ctx = await ParcAvecChauffeurAsync();
        var societeFk = ctx.Model.FindEntityType(typeof(User))!
            .FindNavigation(nameof(User.Societe))!.ForeignKey.Properties.Single();
        var admin = await ctx.Users.SingleAsync(u => u.Id == AdminId);
        ctx.Entry(admin).Property(societeFk.Name).CurrentValue = CompanyId;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var reponse = await Connexion(ctx).Handle(new LoginCommand("admin@test.com", "x"), CancellationToken.None);

        reponse.User.AccountType.Should().Be(UserAccountTypes.Staff);
        (await ctx.RefreshTokens.AsNoTracking().SingleAsync(t => t.UserId == AdminId)).ExpiresAt
            .Should().BeCloseTo(DateTime.UtcNow.AddDays(RefreshTokenLifetime.StaffDays), TimeSpan.FromMinutes(1));
    }
}
