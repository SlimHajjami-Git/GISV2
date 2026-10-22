using FluentAssertions;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Admin.Users.Commands.UpdateAdminUserRole;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Xunit;

namespace GisAPI.Tests.Middleware;

/// <summary>
/// Escalade de privilèges de l'administrateur restreint (relecture du lot du 22/10/2026) : la
/// liste blanche des routes lui ouvre l'écran Users, où figure la société des administrateurs
/// de la plateforme. Sans garde-fou, il se donnait le rôle « Administrateur » complet (même
/// société, donc accepté), créait un compte qui le porte, ou réinitialisait le mot de passe
/// d'un administrateur complet. Règle : jamais un compte à rôle système, jamais un rôle système.
/// </summary>
public class AdministrateurRestreintEscaladeTests
{
    private static readonly string[] Ecrans = { "clients", "users", "vehicles", "monitoring" };

    // ── La règle elle-même (pure) ──

    [Fact]
    public void Restreint_ne_touche_pas_un_compte_a_role_systeme()
    {
        var act = () => AdminScreenAccess.EnsureMayManage(Ecrans, targetHasSystemRole: true, newRoleIsSystem: false);
        act.Should().Throw<ForbiddenAccessException>().WithMessage(AdminScreenAccess.RefusalMessage);
    }

    [Fact]
    public void Restreint_n_attribue_pas_un_role_systeme()
    {
        var act = () => AdminScreenAccess.EnsureMayManage(Ecrans, targetHasSystemRole: false, newRoleIsSystem: true);
        act.Should().Throw<ForbiddenAccessException>();
    }

    [Fact]
    public void Restreint_gere_librement_les_comptes_clients()
    {
        var act = () => AdminScreenAccess.EnsureMayManage(Ecrans, targetHasSystemRole: false, newRoleIsSystem: false);
        act.Should().NotThrow();
    }

    [Fact]
    public void Sans_restriction_rien_ne_change()
    {
        // Administrateur complet (pas de liste d'écrans) : il garde tout, y compris sur ses pairs.
        var act = () => AdminScreenAccess.EnsureMayManage(null, targetHasSystemRole: true, newRoleIsSystem: true);
        act.Should().NotThrow();
    }

    // ── Branchement dans un handler : l'administrateur complet n'est pas gêné ──
    // (La base de test SQLite ignore les permissions jsonb des rôles : le REFUS d'un
    // administrateur restreint se prouve par la règle pure ci-dessus et en direct sur
    // PostgreSQL ; ici, que la garde laisse passer l'administrateur sans restriction.)

    [Fact]
    public async Task L_administrateur_complet_change_encore_le_role_d_un_compte_client()
    {
        await using var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(TestDataBuilder.CreateSociete(1, null));
        ctx.Societes.Add(TestDataBuilder.CreateSociete(5, null));
        ctx.Roles.Add(new Role { Id = 8, Name = "Administrateur", SocieteId = 1, IsSystemRole = true, IsCompanyAdmin = true });
        ctx.Roles.Add(new Role { Id = 7, Name = "Gestionnaire", SocieteId = 5, IsSystemRole = false, IsCompanyAdmin = true });
        ctx.Roles.Add(new Role { Id = 9, Name = "Chauffeur", SocieteId = 5, IsSystemRole = false });
        var admin = TestDataBuilder.CreateUser(id: 30, companyId: 1, email: "admin@exemple.tn");
        admin.RoleId = 8;
        var client = TestDataBuilder.CreateUser(id: 40, companyId: 5, email: "client@exemple.tn");
        client.RoleId = 9;
        ctx.Users.Add(admin);
        ctx.Users.Add(client);
        await ctx.SaveChangesAsync();

        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: 1, userId: 30);
        var handler = new UpdateAdminUserRoleCommandHandler(ctx, tenant.Object);

        await handler.Handle(new UpdateAdminUserRoleCommand(40, 7), CancellationToken.None);

        (await ctx.Users.FindAsync(40))!.RoleId.Should().Be(7);
    }
}
