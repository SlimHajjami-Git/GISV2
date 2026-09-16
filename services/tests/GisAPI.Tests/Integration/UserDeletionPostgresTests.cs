using FluentAssertions;
using GisAPI.Application.Features.Admin.Users.Commands.DeleteAdminUser;
using GisAPI.Application.Features.Employees.Commands.DeleteEmployee;
using GisAPI.Application.Features.Users.Commands.DeleteUser;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Infrastructure.MultiTenancy;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace GisAPI.Tests.Integration;

/// <summary>
/// USR-DEL (16/09/2026) rejoué sur un VRAI PostgreSQL, ce que SQLite ne prouve pas : relevé
/// pg_catalog du helper, stratégie EnableRetryOnFailure de l'API autour de la transaction,
/// règles NO ACTION / RESTRICT / CASCADE réelles des clés étrangères vers users(id).
///
/// ACTIVATION — comme ReplaceVehicleDevicePostgresTests : rien ne s'exécute tant que
/// GIS_PG_TEST_CONN n'est pas définie (chaque test sort en succès). La chaîne pointe vers un
/// serveur JETABLE ; chaque test y crée sa base puis la supprime (GIS_PG_TEST_KEEP_DB=1 pour
/// la garder). JAMAIS une base TN ou DZ.
///   GIS_PG_TEST_CONN='Host=localhost;Port=55432;Username=postgres;Password=pgtest' \
///     dotnet test --filter FullyQualifiedName~UserDeletionPostgresTests
/// </summary>
public class UserDeletionPostgresTests : IAsyncLifetime
{
    private static readonly string? BaseConnectionString =
        Environment.GetEnvironmentVariable("GIS_PG_TEST_CONN");

    private static bool Enabled => !string.IsNullOrWhiteSpace(BaseConnectionString);

    static UserDeletionPostgresTests()
    {
        // Program.cs : comportement « legacy » des horodatages, actif avant tout usage de Npgsql.
        if (Enabled) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    private const int CompanyId = 7;
    private const int CallerId = 1;
    private const int DeletedId = 42;
    private const int ColleagueId = 43;
    private const int VehicleId = 40;
    private const int CostId = 900;
    private const int DriverId = 5;

    /// <summary>
    /// Clés étrangères vers users(id) de la base locale (copie du schéma de DZ, pg_constraint
    /// relevé le 16/09/2026) : table, colonne, NOT NULL, règle ON DELETE (a NO ACTION,
    /// r RESTRICT, c CASCADE, n SET NULL). Celles de TN en sont un sous-ensemble (TnCatalogFixture).
    /// </summary>
    private static readonly (string Table, string Column, bool NotNull, char OnDelete)[] ProductionForeignKeys =
    {
        ("AccidentClaims", "CreatedByUserId", false, 'a'), ("AccidentClaims", "DriverId", false, 'a'),
        ("ai_chat_messages", "user_id", true, 'r'), ("audit_logs", "UserId", false, 'a'),
        ("chat_messages", "receiver_id", true, 'r'), ("chat_messages", "sender_id", true, 'r'),
        ("driver_assignments", "AssignedByUserId", false, 'a'), ("driver_assignments", "DriverId", true, 'c'),
        ("driver_scores", "DriverId", true, 'c'), ("drivers", "user_id", false, 'c'),
        ("driving_events", "DriverId", false, 'a'), ("fuel_entries", "driver_id", false, 'n'),
        ("fuel_records", "driver_id", false, 'a'), ("gps_alerts", "resolved_by_user_id", false, 'a'),
        ("notifications", "UserId", true, 'c'), ("part_transactions", "created_by_user_id", false, 'n'),
        ("refresh_tokens", "UserId", true, 'c'), ("report_schedules", "CreatedByUserId", true, 'c'),
        ("reports", "CreatedByUserId", false, 'a'), ("reservations", "ApprovedByUserId", false, 'a'),
        ("reservations", "AssignedDriverId", false, 'a'), ("reservations", "RequestedByUserId", false, 'a'),
        ("speed_limit_alerts", "AcknowledgedById", false, 'a'), ("trips", "DriverId", false, 'a'),
        ("user_device_tokens", "user_id", true, 'c'), ("user_vehicles", "assigned_by", false, 'n'),
        ("user_vehicles", "user_id", true, 'c'), ("vehicle_costs", "created_by_user_id", false, 'a'),
        ("vehicle_stops", "driver_id", false, 'a'), ("vehicle_user_assignments", "user_id", true, 'c'),
        ("vehicles", "assigned_supervisor_id", false, 'n'),
    };

    private readonly ITestOutputHelper _output;
    private string _databaseName = "";
    private string _connectionString = "";
    private NpgsqlDataSource? _dataSource;

    public UserDeletionPostgresTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        if (!Enabled) return;

        _databaseName = $"gisv2_userdel_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..48];
        var admin = new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = "postgres", Pooling = false };
        await using (var conn = new NpgsqlConnection(admin.ConnectionString))
        {
            await conn.OpenAsync();
            await ExecAsync(conn, $"CREATE DATABASE \"{_databaseName}\"");
        }

        _connectionString = new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = _databaseName }.ConnectionString;
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
        dataSourceBuilder.EnableDynamicJson();
        _dataSource = dataSourceBuilder.Build();

        try
        {
            await using (var ctx = NewApiContext())
                (await ctx.Database.EnsureCreatedAsync()).Should().BeTrue();

            await AlignUserForeignKeysWithProductionAsync();
            await SeedAsync();
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

        if (Environment.GetEnvironmentVariable("GIS_PG_TEST_KEEP_DB") == "1")
        {
            _output.WriteLine($"Base conservée : {_databaseName}");
            return;
        }

        var admin = new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = "postgres", Pooling = false };
        await using var conn = new NpgsqlConnection(admin.ConnectionString);
        await conn.OpenAsync();
        await ExecAsync(conn, $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
    }

    /// <summary>
    /// GisDbContext configuré comme l'API (DependencyInjection.AddInfrastructure), dont
    /// EnableRetryOnFailure(3). Locataire : administrateur de la société 7 (ou système),
    /// ou d'une autre société pour vérifier le filtre de locataire.
    /// </summary>
    private (GisDbContext Context, CurrentTenantService Tenant) NewApiContextFor(int userId, bool systemAdmin, int companyId = CompanyId)
    {
        var builder = new DbContextOptionsBuilder<GisDbContext>()
            .UseNpgsql(_dataSource!, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(GisDbContext).Assembly.GetName().Name);
                npgsqlOptions.EnableRetryOnFailure(3);
            });
        builder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        var tenant = new CurrentTenantService();
        tenant.SetTenant(companyId, userId, "admin@test.com",
            systemAdmin ? new[] { "system_admin" } : new[] { "admin" }, Array.Empty<string>());
        return (new GisDbContext(builder.Options, tenant), tenant);
    }

    private GisDbContext NewApiContext() => NewApiContextFor(CallerId, systemAdmin: true).Context;

    /// <summary>
    /// Remplace les clés étrangères vers users créées par le modèle EF par celles de la
    /// production (règle et nullabilité), et ajoute drivers.user_id, colonne héritée absente
    /// du modèle. Les tables absentes du modèle EF sont ignorées (listées dans la sortie).
    /// </summary>
    private async Task AlignUserForeignKeysWithProductionAsync()
    {
        await using var conn = await OpenAsync();

        await ExecAsync(conn, """
            DO $$
            DECLARE r record;
            BEGIN
              FOR r IN SELECT conrelid::regclass AS tbl, conname FROM pg_constraint
                       WHERE contype = 'f' AND confrelid = 'users'::regclass
              LOOP
                EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I', r.tbl, r.conname);
              END LOOP;
            END $$;
            ALTER TABLE drivers ADD COLUMN IF NOT EXISTS user_id integer NULL;
            """);

        var expected = new List<string>();
        foreach (var fk in ProductionForeignKeys)
        {
            var exists = await ScalarAsync<long>(conn,
                $"SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{fk.Table}' AND column_name = '{fk.Column}'");
            if (exists == 0)
            {
                _output.WriteLine($"Absente du modèle EF : {fk.Table}.{fk.Column}");
                continue;
            }

            // Une colonne qui visait drivers dans le modèle EF vise users en production.
            await ExecAsync(conn, $"""
                DO $$
                DECLARE r record;
                BEGIN
                  FOR r IN SELECT c.conname FROM pg_constraint c
                           JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
                           WHERE c.contype = 'f' AND c.conrelid = '"{fk.Table}"'::regclass AND a.attname = '{fk.Column}'
                  LOOP
                    EXECUTE format('ALTER TABLE %I DROP CONSTRAINT %I', '{fk.Table}', r.conname);
                  END LOOP;
                END $$;
                """);
            var action = fk.OnDelete switch { 'c' => " ON DELETE CASCADE", 'n' => " ON DELETE SET NULL", 'r' => " ON DELETE RESTRICT", _ => "" };
            await ExecAsync(conn, $"""
                ALTER TABLE "{fk.Table}" ALTER COLUMN "{fk.Column}" {(fk.NotNull ? "SET" : "DROP")} NOT NULL;
                ALTER TABLE "{fk.Table}" ADD CONSTRAINT "fk_prod_{fk.Table}_{fk.Column}" FOREIGN KEY ("{fk.Column}") REFERENCES users(id){action};
                """);
            expected.Add($"{(fk.NotNull ? "NO" : "YES")}|{fk.OnDelete}|{fk.Table}.{fk.Column}");
        }

        var aligned = await ListAsync(conn, """
            SELECT CASE WHEN a.attnotnull THEN 'NO' ELSE 'YES' END || '|' || c.confdeltype::text || '|' || cl.relname || '.' || a.attname
            FROM pg_constraint c
            JOIN pg_class cl ON cl.oid = c.conrelid
            JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY (c.conkey)
            WHERE c.contype = 'f' AND c.confrelid = 'users'::regclass
            """);
        aligned.Should().BeEquivalentTo(expected, "le schéma de test doit porter les clés étrangères de production");
        aligned.Should().Contain(new[]
        {
            "YES|a|audit_logs.UserId", "YES|a|vehicle_costs.created_by_user_id", "YES|c|drivers.user_id",
            "NO|r|chat_messages.sender_id", "NO|r|chat_messages.receiver_id", "NO|r|ai_chat_messages.user_id",
            "NO|c|notifications.UserId", "NO|c|refresh_tokens.UserId", "NO|c|user_vehicles.user_id",
        });
    }

    private async Task SeedAsync()
    {
        await using (var seed = NewApiContext())
        {
            seed.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
            seed.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId, subscriptionTypeId: 1));
            seed.Roles.Add(new Role { Id = 1, Name = "Employé", SocieteId = CompanyId });
            seed.Users.AddRange(
                TestDataBuilder.CreateUser(id: CallerId, companyId: CompanyId, email: "admin@test.com"),
                TestDataBuilder.CreateUser(id: DeletedId, companyId: CompanyId, email: "parti@test.com"),
                TestDataBuilder.CreateUser(id: ColleagueId, companyId: CompanyId, email: "collegue@test.com"));
            var vehicle = TestDataBuilder.CreateVehicle(id: VehicleId, companyId: CompanyId);
            vehicle.FuelType = "diesel";
            seed.Vehicles.Add(vehicle);
            await seed.SaveChangesAsync();

            seed.AuditLogs.Add(new AuditLog { UserId = DeletedId, CompanyId = CompanyId, Action = "login", EntityType = "User", EntityId = DeletedId, Timestamp = DateTime.UtcNow });
            seed.VehicleCosts.Add(new VehicleCost { Id = CostId, VehicleId = VehicleId, CompanyId = CompanyId, Type = "fuel", Amount = 120m, Date = new DateTime(2026, 9, 10), CreatedByUserId = DeletedId });
            seed.Drivers.Add(new Driver { Id = DriverId, CompanyId = CompanyId, FirstName = "Sami", LastName = "Chauffeur" });
            seed.ChatMessages.AddRange(
                new ChatMessage { CompanyId = CompanyId, SenderId = DeletedId, ReceiverId = CallerId, Content = "envoyé" },
                new ChatMessage { CompanyId = CompanyId, SenderId = CallerId, ReceiverId = DeletedId, Content = "reçu" },
                new ChatMessage { CompanyId = CompanyId, SenderId = CallerId, ReceiverId = ColleagueId, Content = "entre collègues" });
            seed.AiChatMessages.AddRange(
                new AiChatMessage { CompanyId = CompanyId, UserId = DeletedId, VehicleId = VehicleId, Content = "question du compte supprimé" },
                new AiChatMessage { CompanyId = CompanyId, UserId = ColleagueId, VehicleId = VehicleId, Content = "question d'un collègue" });
            seed.Notifications.Add(new Notification { CompanyId = CompanyId, UserId = DeletedId, Type = "info", Title = "t", Message = "m" });
            seed.RefreshTokens.Add(new RefreshToken { UserId = DeletedId, Token = "jeton", ExpiresAt = DateTime.UtcNow.AddDays(7) });
            seed.UserVehicles.Add(new UserVehicle { UserId = DeletedId, VehicleId = VehicleId, AssignedById = CallerId });
            seed.UserVehicles.Add(new UserVehicle { UserId = ColleagueId, VehicleId = VehicleId, AssignedById = DeletedId });
            await seed.SaveChangesAsync();
        }

        await using var conn = await OpenAsync();
        await ExecAsync(conn, $"UPDATE drivers SET user_id = {DeletedId} WHERE id = {DriverId}");
    }

    private Func<Task> Delete(string ecran, GisDbContext ctx, CurrentTenantService tenant) => ecran switch
    {
        "société" => () => new DeleteUserCommandHandler(ctx, tenant).Handle(new DeleteUserCommand(DeletedId), CancellationToken.None),
        "employés" => () => new DeleteEmployeeCommandHandler(ctx, tenant).Handle(new DeleteEmployeeCommand(DeletedId), CancellationToken.None),
        _ => () => new DeleteAdminUserCommandHandler(ctx, tenant).Handle(new DeleteAdminUserCommand(DeletedId), CancellationToken.None),
    };

    [Theory]
    [InlineData("société")]
    [InlineData("employés")]
    [InlineData("administration")]
    public async Task Un_compte_connecte_est_supprime_et_son_historique_conserve(string ecran)
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.

        var (ctx, tenant) = NewApiContextFor(CallerId, systemAdmin: ecran == "administration");
        await using (ctx)
        {
            ctx.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
            await Delete(ecran, ctx, tenant).Should().NotThrowAsync();
            // Un SaveChanges ultérieur sur le même contexte n'a rien à réécrire.
            (await ctx.SaveChangesAsync()).Should().Be(0);
        }

        await using var conn = await OpenAsync();
        (await ListAsync(conn, "SELECT id::text FROM users ORDER BY id")).Should().Equal(CallerId.ToString(), ColleagueId.ToString());

        // Conservé, détaché du compte.
        (await RowAsync(conn, "SELECT \"UserId\", \"Action\", \"EntityId\" FROM audit_logs"))
            .Should().Equal(new Dictionary<string, object?> { ["UserId"] = null, ["Action"] = "login", ["EntityId"] = DeletedId });
        (await RowAsync(conn, $"SELECT created_by_user_id, amount FROM vehicle_costs WHERE id = {CostId}"))
            .Should().Equal(new Dictionary<string, object?> { ["created_by_user_id"] = null, ["amount"] = 120m });
        (await RowAsync(conn, $"SELECT user_id, first_name FROM drivers WHERE id = {DriverId}"))
            .Should().Equal(new Dictionary<string, object?> { ["user_id"] = null, ["first_name"] = "Sami" });
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM user_vehicles WHERE user_id = {ColleagueId} AND assigned_by IS NULL")).Should().Be(1);

        // Supprimé : ses messages, sa conversation avec l'assistant, ses notifications, jetons et affectations.
        (await ListAsync(conn, "SELECT content FROM chat_messages")).Should().Equal("entre collègues");
        (await ListAsync(conn, "SELECT user_id::text FROM ai_chat_messages")).Should().Equal(ColleagueId.ToString());
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM notifications WHERE \"UserId\" = {DeletedId}")).Should().Be(0);
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM refresh_tokens WHERE \"UserId\" = {DeletedId}")).Should().Be(0);
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM user_vehicles WHERE user_id = {DeletedId}")).Should().Be(0);
    }

    [Fact]
    public async Task Une_instruction_refusee_annule_toute_la_suppression()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.

        await using (var setup = await OpenAsync())
        {
            // vehicle_costs est traitée APRÈS ai_chat_messages, audit_logs, chat_messages et drivers.
            await ExecAsync(setup, """
                CREATE FUNCTION refus_test() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'refus de test'; END $$;
                CREATE TRIGGER refus_test BEFORE UPDATE ON vehicle_costs FOR EACH ROW EXECUTE FUNCTION refus_test();
                """);
        }

        var (ctx, tenant) = NewApiContextFor(CallerId, systemAdmin: false);
        await using (ctx)
        {
            (await Delete("société", ctx, tenant).Should().ThrowAsync<DomainException>())
                .Which.Message.Should().Be("La base a refusé la suppression de l'utilisateur, rien n'a été modifié.");
        }

        await using var conn = await OpenAsync();
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM users WHERE id = {DeletedId}")).Should().Be(1);
        (await ScalarAsync<int>(conn, "SELECT \"UserId\" FROM audit_logs")).Should().Be(DeletedId);
        (await ScalarAsync<int>(conn, "SELECT user_id FROM drivers")).Should().Be(DeletedId);
        (await ScalarAsync<long>(conn, "SELECT count(*) FROM chat_messages")).Should().Be(3);
        (await ScalarAsync<long>(conn, "SELECT count(*) FROM ai_chat_messages")).Should().Be(2);
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM notifications WHERE \"UserId\" = {DeletedId}")).Should().Be(1);
    }

    /// <summary>
    /// L'écran Employés ne borne sa recherche que par le filtre de locataire du DbContext, que
    /// TestGisDbContext n'a pas : un utilisateur d'une autre société ne trouve pas le compte,
    /// qui reste intact avec son historique et ses conversations.
    /// </summary>
    [Fact]
    public async Task L_ecran_employes_d_une_autre_societe_ne_trouve_pas_le_compte()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.

        var (ctx, tenant) = NewApiContextFor(ColleagueId, systemAdmin: false, companyId: 8);
        await using (ctx)
        {
            await Delete("employés", ctx, tenant).Should().ThrowAsync<DomainException>().WithMessage("Employé introuvable");
        }

        await using var conn = await OpenAsync();
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM users WHERE id = {DeletedId}")).Should().Be(1);
        (await ScalarAsync<int>(conn, "SELECT \"UserId\" FROM audit_logs")).Should().Be(DeletedId);
        (await ScalarAsync<long>(conn, "SELECT count(*) FROM ai_chat_messages")).Should().Be(2);
        (await ScalarAsync<long>(conn, "SELECT count(*) FROM chat_messages")).Should().Be(3);
    }

    // ── Accès SQL brut ──

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_connectionString) { Pooling = false }.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    private static async Task<Dictionary<string, object?>> RowAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue($"une ligne attendue pour : {sql}");
        var row = new Dictionary<string, object?>();
        for (var i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        return row;
    }

    private static async Task<List<string>> ListAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<string>();
        while (await reader.ReadAsync()) list.Add(reader.GetString(0));
        return list;
    }
}
