using FluentAssertions;
using GisAPI.Application.Features.Admin.Users.Commands.DeleteAdminUser;
using GisAPI.Application.Features.Employees.Commands.DeleteEmployee;
using GisAPI.Application.Features.Users.Commands.DeleteUser;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GisAPI.Tests.Application.Users;

/// <summary>
/// USR-DEL : les trois suppressions d'utilisateur (société, employés, administration système)
/// rejouées avec les clés étrangères ACTIVES, comme en production. Sans le correctif, le
/// Users.Remove du compte connecté échoue sur FOREIGN KEY constraint failed (23503 en prod).
///
/// Compte 42 : une connexion au journal, une dépense saisie, une fiche chauffeur liée
/// (drivers.user_id, colonne héritée en CASCADE hors modèle EF), deux messages et une
/// conversation avec l'assistant. Collègues 1 (appelant) et 43, qui ne doivent rien perdre.
/// </summary>
public class UserDeletionHandlersTests
{
    private const int CompanyId = 7;
    private const int CallerId = 1;
    private const int DeletedId = 42;
    private const int ColleagueId = 43;
    private const int VehicleId = 40;
    private const int CostId = 900;
    private const int DriverId = 5;

    private static async Task<TestGisDbContext> SeedAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Roles.Add(new Role { Id = 1, Name = "Employé", SocieteId = CompanyId });
        ctx.Users.AddRange(
            TestDataBuilder.CreateUser(id: CallerId, companyId: CompanyId, email: "admin@test.com"),
            TestDataBuilder.CreateUser(id: DeletedId, companyId: CompanyId, email: "parti@test.com"),
            TestDataBuilder.CreateUser(id: ColleagueId, companyId: CompanyId, email: "collegue@test.com"));
        ctx.Vehicles.Add(TestDataBuilder.CreateVehicle(id: VehicleId, companyId: CompanyId));
        ctx.AuditLogs.Add(new AuditLog { UserId = DeletedId, CompanyId = CompanyId, Action = "login", EntityType = "User", EntityId = DeletedId });
        ctx.VehicleCosts.Add(new VehicleCost { Id = CostId, VehicleId = VehicleId, CompanyId = CompanyId, Type = "fuel", Amount = 120m, Date = new DateTime(2026, 9, 10), CreatedByUserId = DeletedId });
        ctx.Drivers.Add(new Driver { Id = DriverId, CompanyId = CompanyId, FirstName = "Sami", LastName = "Chauffeur" });
        ctx.ChatMessages.AddRange(
            new ChatMessage { CompanyId = CompanyId, SenderId = DeletedId, ReceiverId = CallerId, Content = "envoyé" },
            new ChatMessage { CompanyId = CompanyId, SenderId = CallerId, ReceiverId = DeletedId, Content = "reçu" },
            new ChatMessage { CompanyId = CompanyId, SenderId = CallerId, ReceiverId = ColleagueId, Content = "entre collègues" });
        ctx.AiChatMessages.AddRange(
            new AiChatMessage { CompanyId = CompanyId, UserId = DeletedId, VehicleId = VehicleId, Content = "question du compte supprimé" },
            new AiChatMessage { CompanyId = CompanyId, UserId = ColleagueId, VehicleId = VehicleId, Content = "question d'un collègue" });
        ctx.Notifications.Add(new Notification { CompanyId = CompanyId, UserId = DeletedId, Type = "info", Title = "t", Message = "m" });
        ctx.RefreshTokens.Add(new RefreshToken { UserId = DeletedId, Token = "jeton", ExpiresAt = DateTime.UtcNow.AddDays(7) });
        await ctx.SaveChangesAsync();

        // Colonne héritée de la base de production (NULLABLE, ON DELETE CASCADE), absente du modèle EF.
        await ctx.Database.ExecuteSqlRawAsync("ALTER TABLE drivers ADD COLUMN user_id INTEGER NULL REFERENCES users(id) ON DELETE CASCADE");
        await ctx.Database.ExecuteSqlRawAsync($"UPDATE drivers SET user_id = {DeletedId} WHERE id = {DriverId}");

        ctx.ChangeTracker.Clear();
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
        return ctx;
    }

    private static Func<Task> Delete(string ecran, TestGisDbContext ctx, int id, int callerId = CallerId)
    {
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: callerId).Object;
        return ecran switch
        {
            "société" => () => new DeleteUserCommandHandler(ctx, tenant).Handle(new DeleteUserCommand(id), CancellationToken.None),
            "employés" => () => new DeleteEmployeeCommandHandler(ctx, tenant).Handle(new DeleteEmployeeCommand(id), CancellationToken.None),
            _ => () => new DeleteAdminUserCommandHandler(ctx, tenant).Handle(new DeleteAdminUserCommand(id), CancellationToken.None),
        };
    }

    private static async Task<long?> DriverUserIdAsync(TestGisDbContext ctx)
    {
        var conn = ctx.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT user_id FROM drivers WHERE id = {DriverId}";
        var value = await cmd.ExecuteScalarAsync();
        value.Should().NotBeNull("la fiche chauffeur doit exister");
        return value is DBNull ? null : Convert.ToInt64(value);
    }

    [Theory]
    [InlineData("société")]
    [InlineData("employés")]
    [InlineData("administration")]
    public async Task Un_compte_avec_historique_est_supprime_et_son_historique_conserve(string ecran)
    {
        await using var ctx = await SeedAsync();

        await Delete(ecran, ctx, DeletedId).Should().NotThrowAsync();

        ctx.ChangeTracker.Clear();
        (await ctx.Users.Select(u => u.Id).ToListAsync()).Should().BeEquivalentTo(new[] { CallerId, ColleagueId });

        // Conservé, détaché du compte.
        var audit = await ctx.AuditLogs.AsNoTracking().SingleAsync();
        audit.Action.Should().Be("login");
        audit.UserId.Should().BeNull();
        audit.EntityId.Should().Be(DeletedId);
        var cost = await ctx.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id == CostId);
        cost.CreatedByUserId.Should().BeNull();
        cost.Amount.Should().Be(120m);
        (await ctx.Drivers.AsNoTracking().CountAsync(d => d.Id == DriverId)).Should().Be(1);
        (await DriverUserIdAsync(ctx)).Should().BeNull();

        // Supprimé : ses messages et sa conversation avec l'assistant, rien de ceux des collègues.
        (await ctx.ChatMessages.AsNoTracking().Select(m => m.Content).ToListAsync()).Should().Equal("entre collègues");
        (await ctx.AiChatMessages.AsNoTracking().Select(m => m.UserId).ToListAsync()).Should().Equal(ColleagueId);
        (await ctx.Notifications.AsNoTracking().CountAsync(n => n.UserId == DeletedId)).Should().Be(0);
        (await ctx.RefreshTokens.AsNoTracking().CountAsync(t => t.UserId == DeletedId)).Should().Be(0);
    }

    [Theory]
    [InlineData("société")]
    [InlineData("employés")]
    [InlineData("administration")]
    public async Task Un_refus_de_la_base_annule_tout_et_repond_en_clair(string ecran)
    {
        await using var ctx = await SeedAsync();
        // La mise à NULL de la dépense échoue APRÈS le détachement du journal d'audit et de la
        // fiche chauffeur (tables relevées par ordre de nom) : tout doit être annulé. Le modèle
        // EF de test met messages et conversations en CASCADE : SQLite n'émet ici aucun DELETE
        // de lignes ; l'annulation de ces DELETE est prouvée par UserDeletionPostgresTests.
        await ctx.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER refus_test BEFORE UPDATE ON VehicleCosts BEGIN SELECT RAISE(ABORT, 'refus de test'); END;");

        var act = Delete(ecran, ctx, DeletedId);

        // Message en français seulement : le texte technique de la base reste dans l'exception interne.
        var refus = (await act.Should().ThrowAsync<DomainException>()).Which;
        refus.Message.Should().Be("La base a refusé la suppression de l'utilisateur, rien n'a été modifié.");
        refus.InnerException.Should().NotBeNull();
        refus.InnerException!.GetBaseException().Message.Should().Contain("refus de test");

        ctx.ChangeTracker.Clear();
        (await ctx.Users.CountAsync(u => u.Id == DeletedId)).Should().Be(1);
        (await ctx.AuditLogs.AsNoTracking().SingleAsync()).UserId.Should().Be(DeletedId);
        (await ctx.VehicleCosts.AsNoTracking().SingleAsync()).CreatedByUserId.Should().Be(DeletedId);
        (await DriverUserIdAsync(ctx)).Should().Be(DeletedId);
        (await ctx.ChatMessages.CountAsync()).Should().Be(3);
        (await ctx.AiChatMessages.CountAsync()).Should().Be(2);
    }

    [Theory]
    [InlineData("société")]
    [InlineData("employés")]
    [InlineData("administration")]
    public async Task Supprimer_son_propre_compte_reste_refuse(string ecran)
    {
        await using var ctx = await SeedAsync();

        var act = Delete(ecran, ctx, DeletedId, callerId: DeletedId);

        await act.Should().ThrowAsync<DomainException>().WithMessage("Vous ne pouvez pas supprimer votre propre compte");
        ctx.ChangeTracker.Clear();
        (await ctx.Users.CountAsync(u => u.Id == DeletedId)).Should().Be(1);
        (await ctx.AuditLogs.AsNoTracking().SingleAsync()).UserId.Should().Be(DeletedId);
    }

    /// <summary>
    /// DELETE /api/employees/{id} n'exige que le droit « Chauffeurs ». Tant que la suppression
    /// échouait en 23503, un administrateur connecté y était protégé de fait ; elle aboutit
    /// désormais : l'écran Employés refuse tout compte administrateur, l'écran Utilisateurs
    /// (droit « Utilisateurs ») reste le chemin prévu.
    /// </summary>
    [Theory]
    [InlineData("administrateur de société")]
    [InlineData("rôle système")]
    [InlineData("niveau d'accès admin")]
    public async Task L_ecran_employes_ne_supprime_pas_un_compte_administrateur(string compte)
    {
        await using var ctx = await SeedAsync();
        ctx.Roles.AddRange(
            new Role { Id = 2, Name = "Administrateur", SocieteId = CompanyId, IsCompanyAdmin = true },
            new Role { Id = 3, Name = "Système", IsSystemRole = true });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        await ctx.Database.ExecuteSqlRawAsync(compte switch
        {
            "administrateur de société" => $"UPDATE users SET RoleId = 2 WHERE id = {DeletedId}",
            "rôle système" => $"UPDATE users SET RoleId = 3 WHERE id = {DeletedId}",
            _ => $"UPDATE users SET AccessLevel = 'admin' WHERE id = {DeletedId}",
        });

        await Delete("employés", ctx, DeletedId).Should().ThrowAsync<ForbiddenAccessException>()
            .WithMessage("Un compte administrateur se supprime depuis l'écran Utilisateurs");

        ctx.ChangeTracker.Clear();
        (await ctx.Users.CountAsync(u => u.Id == DeletedId)).Should().Be(1);
        (await ctx.AuditLogs.AsNoTracking().SingleAsync()).UserId.Should().Be(DeletedId);
        (await ctx.ChatMessages.CountAsync()).Should().Be(3);

        // Chemin prévu : l'écran Utilisateurs pour un administrateur de société, à condition
        // qu'un autre administrateur reste (ici l'appelant) ; l'administration de la plateforme
        // pour un compte système (UserDeletionGardesTests).
        await ctx.Database.ExecuteSqlRawAsync($"UPDATE users SET AccessLevel = 'admin' WHERE id = {CallerId}");
        await Delete(compte == "rôle système" ? "administration" : "société", ctx, DeletedId).Should().NotThrowAsync();
        ctx.ChangeTracker.Clear();
        (await ctx.Users.CountAsync(u => u.Id == DeletedId)).Should().Be(0);
    }

    [Theory]
    [InlineData("société")]
    [InlineData("administration")]
    public async Task Un_compte_inexistant_repond_404(string ecran)
    {
        await using var ctx = await SeedAsync();

        await Delete(ecran, ctx, 999).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Un_compte_d_une_autre_societe_n_est_pas_supprime_depuis_l_ecran_de_la_societe()
    {
        await using var ctx = await SeedAsync();
        await ctx.Database.ExecuteSqlRawAsync($"UPDATE users SET CompanyId = 8 WHERE id = {ColleagueId}");

        await Delete("société", ctx, ColleagueId).Should().ThrowAsync<NotFoundException>();
        (await ctx.Users.CountAsync(u => u.Id == ColleagueId)).Should().Be(1);
    }
}
