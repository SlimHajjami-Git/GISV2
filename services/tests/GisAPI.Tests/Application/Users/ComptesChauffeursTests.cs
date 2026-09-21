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

    // « Créer son compte » depuis l'écran Chauffeurs : l'id de la fiche voyage avec le formulaire.
    private static CreateUserCommand DepuisLaFiche(string email, int driverId, bool chauffeur = true) =>
        new("Ali", "Ben Salah", email, "+21620000000", "Secret123!", RoleEmploye,
            IsDriverAccount: chauffeur, DriverId: driverId);

    [Fact]
    public async Task Creer_son_compte_depuis_une_fiche_sans_email_relie_cette_fiche_et_pas_une_seconde()
    {
        // Driver.Email est facultatif : retrouvée par e-mail, la fiche d'origine était ratée
        // et une seconde « Ali Ben Salah » naissait, sans véhicule, sans permis, sans tournées.
        using var ctx = await ParcAsync();
        ctx.Drivers.Add(new Driver { Id = 30, CompanyId = CompanyId, FirstName = "Ali", LastName = "Ben Salah", AssignedVehicleId = 5, PermitNumber = "P-77" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var dto = await Creation(ctx).Handle(DepuisLaFiche("ali@test.com", 30), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.Drivers.CountAsync()).Should().Be(1, "pas de seconde fiche");
        var fiche = await ctx.Drivers.AsNoTracking().SingleAsync();
        (fiche.Id, fiche.UserId, fiche.AssignedVehicleId, fiche.PermitNumber).Should().Be((30, dto.Id, 5, "P-77"));
        fiche.Email.Should().Be("ali@test.com", "la fiche sans e-mail reçoit celui du compte");
    }

    [Fact]
    public async Task Creer_son_compte_depuis_une_fiche_dont_l_email_a_ete_corrige_relie_quand_meme_cette_fiche()
    {
        using var ctx = await ParcAsync();
        ctx.Drivers.Add(new Driver { Id = 30, CompanyId = CompanyId, FirstName = "Ali", LastName = "Ben Salah", Email = "ancien@test.com" });
        ctx.Drivers.Add(new Driver { Id = 31, CompanyId = CompanyId, FirstName = "Autre", LastName = "Fiche", Email = "ali@test.com" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var dto = await Creation(ctx).Handle(DepuisLaFiche("ali@test.com", 30), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.Drivers.AsNoTracking().SingleAsync(d => d.Id == 30)).UserId.Should().Be(dto.Id, "la fiche d'où l'on vient prime sur l'e-mail");
        (await ctx.Drivers.AsNoTracking().SingleAsync(d => d.Id == 31)).UserId.Should().BeNull();
        (await ctx.Drivers.AsNoTracking().SingleAsync(d => d.Id == 30)).Email.Should().Be("ancien@test.com", "un e-mail déjà renseigné n'est pas écrasé");
    }

    [Fact]
    public async Task Creer_son_compte_depuis_une_fiche_deja_reliee_est_refuse_sans_rien_creer()
    {
        using var ctx = await ParcAsync();
        ctx.Drivers.Add(new Driver { Id = 30, CompanyId = CompanyId, UserId = 99, FirstName = "Ali", LastName = "Ben Salah" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var act = async () => await Creation(ctx).Handle(DepuisLaFiche("ali@test.com", 30), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*déjà reliée à un autre compte*");
        ctx.ChangeTracker.Clear();
        (await ctx.Users.AnyAsync(u => u.Email == "ali@test.com")).Should().BeFalse("aucun compte à moitié créé");
        (await ctx.Drivers.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Creer_son_compte_depuis_la_fiche_d_une_autre_societe_est_refuse()
    {
        using var ctx = await ParcAsync();
        ctx.Drivers.Add(new Driver { Id = 30, CompanyId = 99, FirstName = "Ali", LastName = "Ailleurs" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var act = async () => await Creation(ctx).Handle(DepuisLaFiche("ali@test.com", 30), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        ctx.ChangeTracker.Clear();
        (await ctx.Users.AnyAsync(u => u.Email == "ali@test.com")).Should().BeFalse();
        (await ctx.Drivers.AsNoTracking().SingleAsync()).UserId.Should().BeNull();
    }

    [Fact]
    public async Task La_fiche_d_origine_est_ignoree_si_la_case_chauffeur_a_ete_decochee()
    {
        using var ctx = await ParcAsync();
        ctx.Drivers.Add(new Driver { Id = 30, CompanyId = CompanyId, FirstName = "Ali", LastName = "Ben Salah" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await Creation(ctx).Handle(DepuisLaFiche("ali@test.com", 30, chauffeur: false), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.Drivers.AsNoTracking().SingleAsync()).UserId.Should().BeNull("un compte ordinaire n'a pas de fiche");
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

    [Fact]
    public async Task Passer_en_chauffeur_coupe_rapport_journalier_heures_silencieuses_et_session_du_site()
    {
        using var ctx = await ParcAsync();
        var salarie = TestDataBuilder.CreateUser(id: 50, companyId: CompanyId, email: "s@test.com");
        salarie.DailyReportEmailEnabled = true;   // PDF de TOUTE la flotte chaque matin
        salarie.QuietHoursEnabled = true;         // aurait retenu le push d'une tournée à l'aube
        salarie.QuietHoursStart = TimeSpan.FromHours(22);
        salarie.QuietHoursEnd = TimeSpan.FromHours(7);
        ctx.Users.Add(salarie);
        ctx.RefreshTokens.Add(new RefreshToken { Id = 1, UserId = 50, Token = "rt-site", ExpiresAt = DateTime.UtcNow.AddDays(6), CreatedAt = DateTime.UtcNow });
        ctx.UserDeviceTokens.Add(new UserDeviceToken { Id = 1, UserId = 50, Token = "fcm", IsActive = true });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await Modification(ctx).Handle(Modif(50, "s@test.com", chauffeur: true), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        var compte = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == 50);
        (compte.DailyReportEmailEnabled, compte.QuietHoursEnabled).Should().Be((false, false),
            "le chauffeur ne peut plus les régler lui-même (routes fermées)");
        compte.Status.Should().Be("active", "la conversion n'est pas une désactivation");
        (await ctx.RefreshTokens.AsNoTracking().SingleAsync()).RevokedAt.Should().NotBeNull(
            "sa session du site (jeton sans claim chauffeur) ne doit pas être prolongée");
        (await ctx.UserDeviceTokens.AsNoTracking().SingleAsync()).IsActive.Should().BeTrue(
            "son téléphone doit recevoir ses tournées");
    }

    [Fact]
    public async Task Modifier_un_chauffeur_deja_chauffeur_ne_revoque_pas_sa_session_mobile()
    {
        using var ctx = await ParcAsync();
        var chauffeur = TestDataBuilder.CreateUser(id: 51, companyId: CompanyId, email: "c@test.com");
        chauffeur.AccountType = UserAccountTypes.Driver;
        ctx.Users.Add(chauffeur);
        ctx.Drivers.Add(new Driver { Id = 31, CompanyId = CompanyId, UserId = 51, FirstName = "Ali", LastName = "B" });
        ctx.RefreshTokens.Add(new RefreshToken { Id = 1, UserId = 51, Token = "rt-mobile", ExpiresAt = DateTime.UtcNow.AddDays(80), CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await Modification(ctx).Handle(Modif(51, "c@test.com", chauffeur: true), CancellationToken.None);

        (await ctx.RefreshTokens.AsNoTracking().SingleAsync()).RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task Modifier_un_chauffeur_ne_reactive_pas_sa_fiche_inactive()
    {
        // L'admin passe la fiche « Inactif » puis corrige le téléphone du compte (ou le désactive) :
        // la fiche redevenait « Actif » dans la liste et les sélecteurs de tournée.
        using var ctx = await ParcAsync();
        var chauffeur = TestDataBuilder.CreateUser(id: 51, companyId: CompanyId, email: "c@test.com");
        chauffeur.AccountType = UserAccountTypes.Driver;
        ctx.Users.Add(chauffeur);
        ctx.Drivers.Add(new Driver { Id = 31, CompanyId = CompanyId, UserId = 51, FirstName = "Ali", LastName = "B", Status = "inactive" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await Modification(ctx).Handle(Modif(51, "c@test.com", chauffeur: true), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        var fiche = await ctx.Drivers.AsNoTracking().SingleAsync();
        fiche.Status.Should().Be("inactive");
        fiche.Phone.Should().Be("+21699999999", "le téléphone suit toujours le compte");
    }

    [Fact]
    public async Task Relier_un_compte_inactif_ne_reactive_pas_la_fiche()
    {
        using var ctx = await ParcAsync();
        var salarie = TestDataBuilder.CreateUser(id: 50, companyId: CompanyId, email: "s@test.com");
        ctx.Users.Add(salarie);
        ctx.Drivers.Add(new Driver { Id = 31, CompanyId = CompanyId, FirstName = "Ali", LastName = "B", Email = "s@test.com", Status = "inactive" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await Modification(ctx).Handle(
            new UpdateUserCommand(50, "Ali", "B", "s@test.com", null, RoleEmploye, "inactive", IsDriverAccount: true),
            CancellationToken.None);

        ctx.ChangeTracker.Clear();
        var fiche = await ctx.Drivers.AsNoTracking().SingleAsync();
        fiche.UserId.Should().Be(50);
        fiche.Status.Should().Be("inactive", "un compte inactif ne rend pas la fiche active");
    }

    // ── R5c : « Relier à un compte existant » — la fiche désignée, pas une seconde ──

    private static UpdateUserCommand VersChauffeur(int id, string email, int driverId, string statut = "active") =>
        new(id, "Ali", "Ben Salah", email, "+21699999999", RoleEmploye, statut, IsDriverAccount: true, DriverId: driverId);

    [Fact]
    public async Task Convertir_un_compte_existant_relie_la_fiche_designee_meme_sans_email()
    {
        // La fiche d'Ali n'a pas d'e-mail (véhicule, permis, tournées) ; Ali a déjà un compte
        // salarié. « Créer son compte » est refusé (e-mail pris) ; convertir le compte existant
        // ne retrouvait la fiche que par e-mail et en créait une seconde.
        using var ctx = await ParcAsync();
        ctx.Users.Add(TestDataBuilder.CreateUser(id: 50, companyId: CompanyId, email: "ali@societe.tn"));
        ctx.Drivers.Add(new Driver { Id = 30, CompanyId = CompanyId, FirstName = "Ali", LastName = "Ben Salah", AssignedVehicleId = 5, PermitNumber = "P-77" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await Modification(ctx).Handle(VersChauffeur(50, "ali@societe.tn", driverId: 30), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.Drivers.CountAsync()).Should().Be(1, "pas de seconde fiche");
        var fiche = await ctx.Drivers.AsNoTracking().SingleAsync();
        (fiche.Id, fiche.UserId, fiche.AssignedVehicleId, fiche.PermitNumber).Should().Be((30, 50, 5, "P-77"));
        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == 50)).AccountType.Should().Be(UserAccountTypes.Driver);
    }

    [Fact]
    public async Task Convertir_vers_une_fiche_deja_reliee_a_un_autre_compte_est_refuse_sans_rien_modifier()
    {
        using var ctx = await ParcAsync();
        var salarie = TestDataBuilder.CreateUser(id: 50, companyId: CompanyId, email: "ali@societe.tn");
        salarie.RoleId = RoleEmploye;
        salarie.CanTours = true;
        ctx.Users.Add(salarie);
        ctx.Drivers.Add(new Driver { Id = 30, CompanyId = CompanyId, UserId = 99, FirstName = "Ali", LastName = "Ben Salah" });
        ctx.RefreshTokens.Add(new RefreshToken { Id = 1, UserId = 50, Token = "rt-site", ExpiresAt = DateTime.UtcNow.AddDays(6), CreatedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var act = async () => await Modification(ctx).Handle(VersChauffeur(50, "ali@societe.tn", driverId: 30), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*déjà reliée à un autre compte*");
        ctx.ChangeTracker.Clear();
        var compte = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == 50);
        (compte.AccountType, compte.CanTours).Should().Be((UserAccountTypes.Staff, true), "vérifié AVANT toute modification");
        (await ctx.RefreshTokens.AsNoTracking().SingleAsync()).RevokedAt.Should().BeNull("sa session n'est pas révoquée");
        (await ctx.Drivers.AsNoTracking().SingleAsync()).UserId.Should().Be(99);
        (await ctx.Roles.CountAsync()).Should().Be(2, "aucun rôle créé en chemin");
    }

    [Fact]
    public async Task Convertir_vers_la_fiche_d_une_autre_societe_est_refuse()
    {
        using var ctx = await ParcAsync();
        ctx.Users.Add(TestDataBuilder.CreateUser(id: 50, companyId: CompanyId, email: "ali@societe.tn"));
        ctx.Drivers.Add(new Driver { Id = 30, CompanyId = 99, FirstName = "Ali", LastName = "Ailleurs" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var act = async () => await Modification(ctx).Handle(VersChauffeur(50, "ali@societe.tn", driverId: 30), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        ctx.ChangeTracker.Clear();
        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == 50)).AccountType.Should().Be(UserAccountTypes.Staff);
        (await ctx.Drivers.AsNoTracking().SingleAsync()).UserId.Should().BeNull();
    }

    [Fact]
    public async Task Un_chauffeur_deja_relie_ne_peut_pas_etre_bascule_sur_une_autre_fiche()
    {
        using var ctx = await ParcAsync();
        var chauffeur = TestDataBuilder.CreateUser(id: 51, companyId: CompanyId, email: "c@test.com");
        chauffeur.AccountType = UserAccountTypes.Driver;
        ctx.Users.Add(chauffeur);
        ctx.Drivers.Add(new Driver { Id = 31, CompanyId = CompanyId, UserId = 51, FirstName = "Ali", LastName = "B" });
        ctx.Drivers.Add(new Driver { Id = 32, CompanyId = CompanyId, FirstName = "Autre", LastName = "Fiche" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var act = async () => await Modification(ctx).Handle(VersChauffeur(51, "c@test.com", driverId: 32), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*déjà relié à une autre fiche*");
        ctx.ChangeTracker.Clear();
        (await ctx.Drivers.AsNoTracking().SingleAsync(d => d.Id == 32)).UserId.Should().BeNull();
    }

    // ── R6c : aucune fiche neuve pour un compte chauffeur inactif ───────────────

    [Fact]
    public async Task Enregistrer_un_chauffeur_inactif_sans_fiche_ne_recree_pas_de_fiche_puis_la_reactivation_la_cree()
    {
        // Fiche supprimée (compte passé inactif par DeleteDriver) ; l'admin corrige le nom du
        // compte sans le réactiver : une fiche « Actif » réapparaissait dans les sélecteurs.
        using var ctx = await ParcAsync();
        var chauffeur = TestDataBuilder.CreateUser(id: 51, companyId: CompanyId, email: "c@test.com");
        chauffeur.AccountType = UserAccountTypes.Driver;
        chauffeur.Status = "inactive";
        ctx.Users.Add(chauffeur);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await Modification(ctx).Handle(
            new UpdateUserCommand(51, "Ali", "Corrigé", "c@test.com", null, RoleEmploye, "inactive", IsDriverAccount: true),
            CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.Drivers.CountAsync()).Should().Be(0, "un compte qui ne se connecte plus n'a pas de fiche à reprendre");

        await Modification(ctx).Handle(
            new UpdateUserCommand(51, "Ali", "Corrigé", "c@test.com", null, RoleEmploye, "active", IsDriverAccount: true),
            CancellationToken.None);

        ctx.ChangeTracker.Clear();
        var fiche = await ctx.Drivers.AsNoTracking().SingleAsync();
        (fiche.UserId, fiche.LastName, fiche.Status).Should().Be((51, "Corrigé", "active"), "créée à la réactivation");
    }

    // ── Garde-fous du passage en chauffeur (mêmes règles que DeleteUser) ────────

    private static UpdateUserCommandHandler ModificationPar(TestGisDbContext ctx, int auteurId) =>
        new(ctx, TestDbContextFactory.CreateMockTenantService(CompanyId, auteurId).Object);

    [Fact]
    public async Task Un_administrateur_ne_peut_pas_passer_son_propre_compte_en_chauffeur()
    {
        using var ctx = await ParcAsync();

        var act = async () => await Modification(ctx).Handle(Modif(AdminId, "admin@test.com", chauffeur: true), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*propre compte*");
        ctx.ChangeTracker.Clear();
        var admin = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == AdminId);
        (admin.AccountType, admin.RoleId).Should().Be((UserAccountTypes.Staff, RoleAdmin), "rien n'a changé");
    }

    [Fact]
    public async Task Le_dernier_administrateur_actif_ne_peut_pas_etre_passe_en_chauffeur()
    {
        // Un gestionnaire avec le droit Utilisateurs vise l'unique administrateur.
        using var ctx = await ParcAsync();
        var gestionnaire = TestDataBuilder.CreateUser(id: 60, companyId: CompanyId, email: "g@test.com");
        gestionnaire.CanUsers = true;
        var ancienAdmin = TestDataBuilder.CreateUser(id: 61, companyId: CompanyId, email: "old@test.com");
        ancienAdmin.RoleId = RoleAdmin;
        ancienAdmin.Status = "inactive";   // ne se connecte plus : ne sauve pas la société
        ctx.Users.AddRange(gestionnaire, ancienAdmin);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var act = async () => await ModificationPar(ctx, 60).Handle(Modif(AdminId, "admin@test.com", chauffeur: true), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*dernier administrateur*");
        ctx.ChangeTracker.Clear();
        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == AdminId)).AccountType.Should().Be(UserAccountTypes.Staff);
        (await ctx.Drivers.CountAsync()).Should().Be(0, "aucune fiche créée");
    }

    [Fact]
    public async Task Un_administrateur_peut_etre_passe_en_chauffeur_s_il_en_reste_un_autre()
    {
        using var ctx = await ParcAsync();
        var gestionnaire = TestDataBuilder.CreateUser(id: 60, companyId: CompanyId, email: "g@test.com");
        var autreAdmin = TestDataBuilder.CreateUser(id: 62, companyId: CompanyId, email: "a2@test.com");
        autreAdmin.AccessLevel = "admin";
        ctx.Users.AddRange(gestionnaire, autreAdmin);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await ModificationPar(ctx, 60).Handle(Modif(AdminId, "admin@test.com", chauffeur: true), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == AdminId)).AccountType.Should().Be(UserAccountTypes.Driver);
    }

    [Fact]
    public async Task Un_compte_systeme_ne_peut_pas_etre_passe_en_chauffeur()
    {
        using var ctx = await ParcAsync();
        ctx.Roles.Add(new Role { Id = 3, Name = "Système", SocieteId = CompanyId, IsSystemRole = true });
        var systeme = TestDataBuilder.CreateUser(id: 55, companyId: CompanyId, email: "sys@test.com");
        systeme.RoleId = 3;
        ctx.Users.Add(systeme);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var act = async () => await Modification(ctx).Handle(Modif(55, "sys@test.com", chauffeur: true), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*système*");
        ctx.ChangeTracker.Clear();
        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == 55)).AccountType.Should().Be(UserAccountTypes.Staff);
    }

    [Fact]
    public async Task Repasser_un_chauffeur_en_salarie_rejoue_le_quota_d_utilisateurs()
    {
        // Plan à 2 places, pleines (admin + salarié). Créé « chauffeur » (hors quota) puis
        // décoché : il devenait un 3e compte de gestion, à volonté.
        using var ctx = await ParcAsync(maxUsers: 2);
        ctx.Users.Add(TestDataBuilder.CreateUser(id: 50, companyId: CompanyId, email: "s@test.com"));
        var chauffeur = TestDataBuilder.CreateUser(id: 51, companyId: CompanyId, email: "c@test.com");
        chauffeur.AccountType = UserAccountTypes.Driver;
        ctx.Users.Add(chauffeur);
        ctx.Drivers.Add(new Driver { Id = 31, CompanyId = CompanyId, UserId = 51, FirstName = "Ali", LastName = "B" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var act = async () => await Modification(ctx).Handle(Modif(51, "c@test.com", chauffeur: false), CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Limite d'utilisateurs atteinte*");
        ctx.ChangeTracker.Clear();
        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == 51)).AccountType.Should().Be(UserAccountTypes.Driver);
        (await ctx.Drivers.AsNoTracking().SingleAsync()).UserId.Should().Be(51, "le lien n'est pas coupé");
    }

    // ── Rapport journalier ─────────────────────────────────────────────────────

    [Fact]
    public async Task Le_rapport_journalier_de_la_flotte_ne_part_jamais_a_un_compte_chauffeur()
    {
        using var ctx = await ParcAsync();
        var salarie = TestDataBuilder.CreateUser(id: 50, companyId: CompanyId, email: "s@test.com");
        salarie.DailyReportEmailEnabled = true;
        var chauffeur = TestDataBuilder.CreateUser(id: 51, companyId: CompanyId, email: "c@test.com");
        chauffeur.AccountType = UserAccountTypes.Driver;
        chauffeur.DailyReportEmailEnabled = true;   // posé avant la conversion
        ctx.Users.AddRange(salarie, chauffeur);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var destinataires = await GisAPI.Services.DailyFleetReportService.RecipientsAsync(ctx, CompanyId, CancellationToken.None);

        destinataires.Select(u => u.Id).Should().Equal(50);
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
    public void Le_refus_dit_quelle_version_de_l_application_installer()
    {
        // L'application 1.1.1 ne se déclare pas « mobile » : elle affiche ce même refus. Le début
        // de la phrase est ce que le site reconnaît (auth.service.ts, isDriverRefusal).
        LoginClients.DriverWebLoginRefused.Should()
            .StartWith("Ce compte est réservé à l'application mobile")
            .And.Contain("version 1.2 ou plus récente");
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
