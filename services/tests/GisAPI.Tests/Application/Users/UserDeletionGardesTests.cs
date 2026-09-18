using FluentAssertions;
using GisAPI.Application.Features.Users.Commands.DeleteUser;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Application.Users;

/// <summary>
/// USR-DEL : tant que la suppression échouait en 23503, un compte qui s'était connecté était
/// protégé de fait. Maintenant qu'elle aboutit, l'écran Utilisateurs (droit « Utilisateurs »)
/// ne doit ni ôter un compte système ni laisser la société sans administrateur.
/// </summary>
public class UserDeletionGardesTests
{
    private const int CompanyId = 7;
    private const int CallerId = 1;
    private const int TargetId = 42;
    private const int OtherId = 43;

    private const int EmployeRole = 1;
    private const int AdminRole = 2;
    private const int SystemRole = 3;

    private static async Task<TestGisDbContext> SeedAsync(int targetRole, string targetAccess = "user", int otherRole = EmployeRole, string otherAccess = "user")
    {
        var ctx = TestDbContextFactory.Create();
        ctx.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Roles.AddRange(
            new Role { Id = EmployeRole, Name = "Employé", SocieteId = CompanyId },
            new Role { Id = AdminRole, Name = "Administrateur", SocieteId = CompanyId, IsCompanyAdmin = true },
            new Role { Id = SystemRole, Name = "Système", IsSystemRole = true });

        var caller = TestDataBuilder.CreateUser(id: CallerId, companyId: CompanyId, email: "gestion@test.com");
        var target = TestDataBuilder.CreateUser(id: TargetId, companyId: CompanyId, email: "cible@test.com");
        target.RoleId = targetRole;
        target.AccessLevel = targetAccess;
        var other = TestDataBuilder.CreateUser(id: OtherId, companyId: CompanyId, email: "autre@test.com");
        other.RoleId = otherRole;
        other.AccessLevel = otherAccess;
        ctx.Users.AddRange(caller, target, other);

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static Func<Task> Delete(TestGisDbContext ctx, bool callerIsSystemAdmin = false)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: CallerId);
        tenant.Setup(t => t.IsSystemAdmin).Returns(callerIsSystemAdmin);
        return () => new DeleteUserCommandHandler(ctx, tenant.Object).Handle(new DeleteUserCommand(TargetId), CancellationToken.None);
    }

    [Theory]
    [InlineData(AdminRole, "user")]
    [InlineData(EmployeRole, "admin")]
    public async Task Le_dernier_administrateur_de_la_societe_n_est_pas_supprime(int role, string access)
    {
        await using var ctx = await SeedAsync(targetRole: role, targetAccess: access);

        await Delete(ctx).Should().ThrowAsync<DomainException>()
            .WithMessage("Impossible de supprimer le dernier administrateur de la société");

        (await ctx.Users.AsNoTracking().AnyAsync(u => u.Id == TargetId)).Should().BeTrue();
    }

    [Theory]
    [InlineData(AdminRole, "user")]
    [InlineData(EmployeRole, "admin")]
    public async Task Un_administrateur_est_supprime_quand_un_autre_reste(int otherRole, string otherAccess)
    {
        await using var ctx = await SeedAsync(targetRole: AdminRole, otherRole: otherRole, otherAccess: otherAccess);

        await Delete(ctx).Should().NotThrowAsync();

        ctx.ChangeTracker.Clear();
        (await ctx.Users.AsNoTracking().AnyAsync(u => u.Id == TargetId)).Should().BeFalse();
    }

    /// <summary>
    /// Revue de l'intégration du 18/09/2026 : un administrateur suspendu (ou en attente) ne
    /// peut pas se connecter ; il ne sauve donc pas la société de se retrouver sans
    /// administrateur.
    /// </summary>
    [Theory]
    [InlineData("suspended")]
    [InlineData("pending")]
    public async Task Un_administrateur_qui_ne_peut_pas_se_connecter_ne_compte_pas_comme_restant(string statut)
    {
        await using var ctx = await SeedAsync(targetRole: AdminRole, otherRole: AdminRole);
        var autre = await ctx.Users.SingleAsync(u => u.Id == OtherId);
        autre.Status = statut;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await Delete(ctx).Should().ThrowAsync<DomainException>()
            .WithMessage("Impossible de supprimer le dernier administrateur de la société");

        (await ctx.Users.AsNoTracking().AnyAsync(u => u.Id == TargetId)).Should().BeTrue();
    }

    [Fact]
    public async Task Un_compte_systeme_n_est_pas_supprime_par_un_gestionnaire_de_societe()
    {
        await using var ctx = await SeedAsync(targetRole: SystemRole);

        await Delete(ctx).Should().ThrowAsync<ForbiddenAccessException>()
            .WithMessage("Un compte administrateur système ne peut pas être supprimé depuis cet écran");

        (await ctx.Users.AsNoTracking().AnyAsync(u => u.Id == TargetId)).Should().BeTrue();
    }

    [Fact]
    public async Task Un_compte_systeme_reste_supprimable_par_un_administrateur_systeme()
    {
        await using var ctx = await SeedAsync(targetRole: SystemRole);

        await Delete(ctx, callerIsSystemAdmin: true).Should().NotThrowAsync();

        ctx.ChangeTracker.Clear();
        (await ctx.Users.AsNoTracking().AnyAsync(u => u.Id == TargetId)).Should().BeFalse();
    }

    [Fact]
    public async Task Un_employe_ordinaire_reste_supprimable()
    {
        await using var ctx = await SeedAsync(targetRole: EmployeRole);

        await Delete(ctx).Should().NotThrowAsync();

        ctx.ChangeTracker.Clear();
        (await ctx.Users.AsNoTracking().AnyAsync(u => u.Id == TargetId)).Should().BeFalse();
    }
}
