using FluentAssertions;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GisAPI.Tests.Services;

/// <summary>
/// Relecture de DEF-030 : chaque refus de connexion écrit désormais une ligne
/// « login_failed » (EntityType « User ») dans audit_logs. Le tableau de bord
/// d'administration compte l'usage des modules depuis cette table : sans filtre, une
/// force brute de 500 essais apparaissait comme 500 « Connexions » et une tendance
/// en hausse. Test sur le VRAI AdminController, GisDbContext en mémoire.
/// </summary>
public class AdminFeatureUsageLoginFailedTests
{
    private sealed class ContexteEnMemoire : GisDbContext
    {
        public ContexteEnMemoire(DbContextOptions<GisDbContext> options, ICurrentTenantService tenant)
            : base(options, tenant) { }

        // Colonnes propres à PostgreSQL (jsonb, tableaux…) sans équivalent en mémoire.
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToList())
                foreach (var property in entity.GetDeclaredProperties().ToList())
                    if (!Simple(property.ClrType) && !property.IsKey() && !property.IsForeignKey())
                        modelBuilder.Entity(entity.ClrType).Ignore(property.Name);
        }

        private static bool Simple(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
                || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid) || t == typeof(byte[])
                || t == typeof(DateOnly) || t == typeof(TimeOnly);
        }
    }

    private static GisDbContext Contexte()
    {
        // Administrateur système : le filtre de société d'audit_logs laisse tout voir.
        var tenant = new Mock<ICurrentTenantService>();
        tenant.Setup(x => x.IsSystemAdmin).Returns(true);
        tenant.Setup(x => x.IsAuthenticated).Returns(true);
        return new ContexteEnMemoire(new DbContextOptionsBuilder<GisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
            .Options, tenant.Object);
    }

    private static AdminController Controleur(GisDbContext ctx) =>
        new(ctx, null!, null!, null!, null!, null!, null!);

    private static AuditLog Ligne(string action, int? userId, DateTime timestamp) => new()
    {
        UserId = userId,
        CompanyId = 7,
        Action = action,
        EntityType = "User",
        EntityId = 42,
        Timestamp = timestamp
    };

    [Fact]
    public async Task Les_refus_de_connexion_ne_comptent_ni_dans_les_connexions_ni_dans_leur_tendance()
    {
        using var ctx = Contexte();
        var now = DateTime.UtcNow;
        // Semaine en cours : 2 connexions réussies, 30 refus.
        ctx.AuditLogs.Add(Ligne("login", 42, now.AddDays(-1)));
        ctx.AuditLogs.Add(Ligne("logout", 42, now.AddDays(-1)));
        for (var i = 0; i < 30; i++)
            ctx.AuditLogs.Add(Ligne(LoginCommandHandler.FailedLoginAction, null, now.AddHours(-2)));
        // Semaine précédente : 2 connexions réussies, 50 refus.
        ctx.AuditLogs.Add(Ligne("login", 42, now.AddDays(-9)));
        ctx.AuditLogs.Add(Ligne("login", 43, now.AddDays(-10)));
        for (var i = 0; i < 50; i++)
            ctx.AuditLogs.Add(Ligne(LoginCommandHandler.FailedLoginAction, null, now.AddDays(-8)));
        await ctx.SaveChangesAsync();

        var result = await Controleur(ctx).GetFeatureUsage();

        var usages = result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<List<FeatureUsageDto>>().Subject;
        var connexions = usages.Should().ContainSingle(u => u.Feature == "Connexions").Subject;
        connexions.UsageCount.Should().Be(2, "avant : 32, refus compris");
        connexions.UniqueUsers.Should().Be(1);
        connexions.Trend.Should().Be(0, "2 connexions contre 2 la semaine d'avant ; avant : -38 %");
    }

    [Fact]
    public async Task Une_semaine_faite_uniquement_de_refus_ne_fait_pas_apparaitre_de_module_Connexions()
    {
        using var ctx = Contexte();
        for (var i = 0; i < 10; i++)
            ctx.AuditLogs.Add(Ligne(LoginCommandHandler.FailedLoginAction, null, DateTime.UtcNow.AddHours(-1)));
        await ctx.SaveChangesAsync();

        var result = await Controleur(ctx).GetFeatureUsage();

        result.Result.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeAssignableTo<List<FeatureUsageDto>>().Subject
            .Should().NotContain(u => u.Feature == "Connexions");
    }
}
