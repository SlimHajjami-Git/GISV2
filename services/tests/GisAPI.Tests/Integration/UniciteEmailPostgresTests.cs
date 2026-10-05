using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Users.Commands.CreateUser;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Infrastructure.MultiTenancy;
using GisAPI.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace GisAPI.Tests.Integration;

/// <summary>
/// Le cas HERTZ du 03/10/2026 rejoué sur un VRAI PostgreSQL, seul endroit où les deux
/// pièces du piège coexistent : le filtre par société de GisDbContext, qui cache les
/// comptes des autres sociétés, ET l'index UNIQUE `idx_users_email`, qui lui couvre toute
/// la table. SQLite ne porte pas le filtre (modèle EF mis en cache par type de contexte),
/// le reste du contrat est donc verrouillé dans UniciteEmailTests.
///
/// ACTIVATION — comme UserDeletionPostgresTests : rien ne s'exécute tant que
/// GIS_PG_TEST_CONN n'est pas définie. La chaîne pointe vers un serveur JETABLE ; chaque
/// test y crée sa base puis la supprime. JAMAIS une base TN ou DZ.
///   GIS_PG_TEST_CONN='Host=localhost;Port=55432;Username=postgres;Password=pgtest' \
///     dotnet test --filter FullyQualifiedName~UniciteEmailPostgresTests
/// </summary>
public class UniciteEmailPostgresTests : IAsyncLifetime
{
    private static readonly string? BaseConnectionString =
        Environment.GetEnvironmentVariable("GIS_PG_TEST_CONN");

    private static bool Enabled => !string.IsNullOrWhiteSpace(BaseConnectionString);

    static UniciteEmailPostgresTests()
    {
        if (Enabled) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    private const int MaSociete = 7;
    private const int AutreSociete = 99;
    private const int AdminId = 1;
    private const int RoleEmploye = 1;
    private const string PrisAilleurs = "aymen.tahri@parenin.com.tn";

    private readonly ITestOutputHelper _output;
    private string _databaseName = "";
    private NpgsqlDataSource? _dataSource;

    public UniciteEmailPostgresTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        if (!Enabled) return;

        _databaseName = $"gisv2_mail_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..48];
        var admin = new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = "postgres", Pooling = false };
        await using (var conn = new NpgsqlConnection(admin.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = _databaseName }.ConnectionString;
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson();
        _dataSource = dataSourceBuilder.Build();

        try
        {
            await using var ctx = Contexte(systemAdmin: true);
            (await ctx.Database.EnsureCreatedAsync()).Should().BeTrue();
            await SeedAsync(ctx);
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (!Enabled) return;

        if (_dataSource != null) await _dataSource.DisposeAsync();
        NpgsqlConnection.ClearAllPools();

        var admin = new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = "postgres", Pooling = false };
        await using var conn = new NpgsqlConnection(admin.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private GisDbContext Contexte(bool systemAdmin = false, int companyId = MaSociete)
    {
        var builder = new DbContextOptionsBuilder<GisDbContext>()
            .UseNpgsql(_dataSource!, o => o.MigrationsAssembly(typeof(GisDbContext).Assembly.GetName().Name));
        builder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        var tenant = new CurrentTenantService();
        tenant.SetTenant(companyId, AdminId, "admin@hertz.tn",
            systemAdmin ? new[] { "system_admin" } : new[] { "company_admin" }, Array.Empty<string>());
        return new GisDbContext(builder.Options, tenant);
    }

    private static async Task SeedAsync(GisDbContext ctx)
    {
        ctx.SubscriptionTypes.Add(new SubscriptionType { Id = 1, Name = "Test", MaxUsers = 50, MaxVehicles = 50, IsActive = true });
        ctx.Societes.Add(new Societe { Id = MaSociete, Name = "HERTZ", SubscriptionTypeId = 1, IsActive = true });
        ctx.Societes.Add(new Societe { Id = AutreSociete, Name = "PARENIN", SubscriptionTypeId = 1, IsActive = true });
        ctx.Roles.Add(new Role { Id = RoleEmploye, Name = "Opérateur", SocieteId = MaSociete, IsCompanyAdmin = false });
        ctx.Roles.Add(new Role { Id = 2, Name = "Administrateur", SocieteId = MaSociete, IsCompanyAdmin = true });
        ctx.Roles.Add(new Role { Id = 3, Name = "Opérateur", SocieteId = AutreSociete, IsCompanyAdmin = false });
        ctx.Users.Add(new User { Id = AdminId, CompanyId = MaSociete, RoleId = 2, Email = "admin@hertz.tn", FirstName = "Ahmed", LastName = "BEN ALI", PasswordHash = "x", Status = "active" });
        ctx.Users.Add(new User { Id = 50, CompanyId = AutreSociete, RoleId = 3, Email = PrisAilleurs, FirstName = "Aymen", LastName = "TAHRI", PasswordHash = "x", Status = "active" });
        await ctx.SaveChangesAsync();

        // Les identifiants ci-dessus sont posés à la main : la séquence, elle, est restée à 1
        // et le compte créé par le test entrerait en collision de clé primaire.
        await ctx.Database.ExecuteSqlRawAsync(
            "SELECT setval(pg_get_serial_sequence('users', 'id'), 1000, true), " +
            "setval(pg_get_serial_sequence('roles', 'id'), 1000, true), " +
            "setval(pg_get_serial_sequence('societes', 'id'), 1000, true)");
    }

    private static CreateUserCommandHandler Creation(GisDbContext ctx)
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(MaSociete, AdminId, "admin@hertz.tn", new[] { "company_admin" }, Array.Empty<string>());
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hash");
        return new CreateUserCommandHandler(ctx, tenant, hasher.Object, Mock.Of<IPublisher>());
    }

    [Fact]
    public async Task Le_compte_de_l_autre_societe_est_invisible_mais_son_email_reste_pris()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.

        await using var ctx = Contexte();

        (await ctx.Users.AnyAsync(u => u.Email == PrisAilleurs))
            .Should().BeFalse("le filtre par société le masque — c'est exactement le piège");
        (await ctx.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == PrisAilleurs))
            .Should().BeTrue("l'index UNIQUE idx_users_email, lui, couvre toute la table");
    }

    [Fact]
    public async Task Creer_le_compte_de_HERTZ_avec_l_email_de_PARENIN_rend_un_refus_lisible_et_non_un_500()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.

        await using var ctx = Contexte();

        var refus = await Assert.ThrowsAsync<ConflictException>(() => Creation(ctx).Handle(
            new CreateUserCommand("ste", "parnin", PrisAilleurs, "92273151", "Secret123!", RoleEmploye),
            CancellationToken.None));

        refus.Message.Should().Be("Cet email est déjà utilisé");

        // Avant le correctif, c'est PostgreSQL qui refusait, en 23505 — donc en 500.
        await using var relecture = Contexte(systemAdmin: true);
        (await relecture.Users.CountAsync()).Should().Be(2, "rien n'a été écrit");
    }

    [Fact]
    public async Task Une_adresse_libre_cree_bien_le_compte()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.

        await using var ctx = Contexte();

        var dto = await Creation(ctx).Handle(
            new CreateUserCommand("ste", "parnin", "ste.parnin@hertz.tn", "92273151", "Secret123!", RoleEmploye),
            CancellationToken.None);

        await using var relecture = Contexte(systemAdmin: true);
        var compte = await relecture.Users.AsNoTracking().SingleAsync(u => u.Id == dto.Id);
        (compte.Email, compte.CompanyId).Should().Be(("ste.parnin@hertz.tn", MaSociete));
    }
}
