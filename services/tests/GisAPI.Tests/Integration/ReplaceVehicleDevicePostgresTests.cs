using System.Data.Common;
using System.Diagnostics;
using FluentAssertions;
using GisAPI.Application.Features.Admin.Vehicles.Commands.ReplaceVehicleDevice;
using GisAPI.Application.Features.Admin.Vehicles.Commands.UpdateAdminVehicle;
using GisAPI.Domain.Entities;
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
/// Remplacement / rattachement de boîtier (constat TN du 14/09/2026 : HTZ 278, 255, 292)
/// rejoué sur un VRAI PostgreSQL — ce que SQLite ne peut pas prouver :
/// verrou « SELECT … FOR UPDATE » et paramètre tableau d'entiers Npgsql, stratégie
/// d'exécution EnableRetryOnFailure, index uniques et clés étrangères de production
/// (gps_positions ON DELETE CASCADE, vehicles.gps_device_id ON DELETE SET NULL),
/// ordre réel des écritures d'EF sur Npgsql.
///
/// ACTIVATION — ces tests ne font RIEN tant que la variable d'environnement
/// GIS_PG_TEST_CONN n'est pas définie : chaque test sort immédiatement en succès.
/// La suite habituelle (dotnet test sans base) reste donc verte et autonome ; xUnit 2
/// n'offre pas de « skip » dynamique et aucun paquet de type SkippableFact n'est
/// référencé, d'où cette sortie anticipée. Exemple :
///   GIS_PG_TEST_CONN='Host=localhost;Port=55432;Username=postgres;Password=pgtest' \
///     dotnet test --filter FullyQualifiedName~ReplaceVehicleDevicePostgresTests
/// La chaîne pointe vers un serveur JETABLE : chaque test y crée sa propre base (nom
/// unique), puis la supprime (GIS_PG_TEST_KEEP_DB=1 pour la garder et l'inspecter).
/// JAMAIS une base TN ou DZ.
/// </summary>
public class ReplaceVehicleDevicePostgresTests : IAsyncLifetime
{
    private static readonly string? BaseConnectionString =
        Environment.GetEnvironmentVariable("GIS_PG_TEST_CONN");

    private static bool Enabled => !string.IsNullOrWhiteSpace(BaseConnectionString);

    static ReplaceVehicleDevicePostgresTests()
    {
        // Program.cs (l.15) : l'API active ce comportement AVANT tout usage de Npgsql.
        // Sans lui, DateTime serait mappé en timestamptz et les écritures n'auraient pas
        // la forme de la production. Vérifié dans InitializeAsync.
        if (Enabled) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    // HTZ 278 — société 6 BELIVE SAV (ids, IMEI et MAT de TN)
    private const int Htz278 = 355;
    private const int WrongDevice278 = 382060;
    private const string WrongImei278 = "860141078677153";
    private const int RealDevice278 = 384940;
    private const string RealImei278 = "860141076677153";
    private const string Mat278 = "NR08G1040";

    // HTZ 255 — société 6 BELIVE SAV
    private const int Htz255 = 328;
    private const int WrongDevice255 = 319546;
    private const string WrongImei255 = "860141076873814";
    private const int RealDevice255 = 340768;
    private const string RealImei255 = "860141076673814";
    private const string Mat255 = "NR08G0935";
    private const string Sim255 = "92005328";

    private const int Belive = 1;
    private const int BeliveSav = 6;

    private const string LockStatement = "SELECT id FROM gps_devices WHERE id = ANY({0}) ORDER BY id FOR UPDATE";

    /// <summary>
    /// Clés étrangères vers gps_devices relevées sur TN le 14/09/2026 (lecture seule,
    /// pg_constraint). confdeltype : a = NO ACTION, c = CASCADE, n = SET NULL.
    /// accident_events, geofence_events, poi_visits et frame_debug_log n'en ont pas.
    /// </summary>
    private static readonly (string Table, string Column, string Name, char OnDelete)[] TnForeignKeys =
    {
        ("device_commands", "device_id", "device_commands_device_id_fkey", 'a'),
        ("device_events", "device_id", "device_events_device_id_fkey", 'a'),
        ("fuel_records", "device_id", "FK_fuel_records_gps_devices_device_id", 'a'),
        ("gps_alerts", "device_id", "FK_gps_alerts_gps_devices_device_id", 'a'),
        ("gps_positions", "device_id", "FK_gps_positions_gps_devices_device_id", 'c'),
        ("tow_events", "device_id", "tow_events_device_id_fkey", 'a'),
        ("vehicle_stops", "device_id", "FK_vehicle_stops_gps_devices_device_id", 'a'),
        ("vehicles", "gps_device_id", "FK_vehicles_gps_devices_gps_device_id", 'n'),
    };

    private readonly ITestOutputHelper _output;
    private string _databaseName = "";
    private string _connectionString = "";
    private NpgsqlDataSource? _dataSource;

    public ReplaceVehicleDevicePostgresTests(ITestOutputHelper output) => _output = output;

    // ── Cycle de vie : base neuve par test ──

    public async Task InitializeAsync()
    {
        if (!Enabled) return;

        _databaseName = $"gisv2_rattach_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..48];
        var admin = new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = "postgres", Pooling = false };
        await using (var conn = new NpgsqlConnection(admin.ConnectionString))
        {
            await conn.OpenAsync();
            await ExecAsync(conn, $"CREATE DATABASE \"{_databaseName}\"");
        }

        _connectionString = new NpgsqlConnectionStringBuilder(BaseConnectionString) { Database = _databaseName }.ConnectionString;

        // Même source de données que DependencyInjection.AddInfrastructure.
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_connectionString);
        dataSourceBuilder.EnableDynamicJson();
        _dataSource = dataSourceBuilder.Build();

        try
        {
            await using (var ctx = NewApiContext())
            {
                (await ctx.Database.EnsureCreatedAsync()).Should().BeTrue();
            }

            await AlignConstraintsWithTnAsync();
        }
        catch
        {
            // xUnit 2 n'appelle pas DisposeAsync si InitializeAsync échoue : on nettoie ici.
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
    /// GisDbContext configuré EXACTEMENT comme l'API (DependencyInjection.AddInfrastructure) :
    /// UseNpgsql(dataSource avec EnableDynamicJson), MigrationsAssembly, EnableRetryOnFailure(3),
    /// PendingModelChangesWarning ignoré. Locataire : administrateur système (espace /api/admin).
    /// </summary>
    private GisDbContext NewApiContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<GisDbContext>()
            .UseNpgsql(_dataSource!, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(GisDbContext).Assembly.GetName().Name);
                npgsqlOptions.EnableRetryOnFailure(3);
            });
        builder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        if (interceptors.Length > 0) builder.AddInterceptors(interceptors);

        var tenant = new CurrentTenantService();
        tenant.SetTenant(Belive, 1, "admin@belive.tn", new[] { "system_admin" }, Array.Empty<string>());
        return new GisDbContext(builder.Options, tenant);
    }

    /// <summary>
    /// Contexte d'un « acteur concurrent » (l'ingestion écrit hors EF en prod) : sans
    /// stratégie de retry, pour pouvoir ouvrir une transaction à la main.
    /// </summary>
    private GisDbContext NewActorContext()
    {
        var options = new DbContextOptionsBuilder<GisDbContext>()
            .UseNpgsql(_dataSource!)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new GisDbContext(options);
    }

    /// <summary>
    /// Remplace les clés étrangères vers gps_devices créées par le modèle EF par celles
    /// de TN, et garantit les index uniques de TN (vehicles.gps_device_id,
    /// gps_devices.device_uid). Ainsi, un mauvais ordre d'écriture échoue ici comme en prod.
    /// </summary>
    private async Task AlignConstraintsWithTnAsync()
    {
        await using var conn = await OpenAsync();

        // Timestamps : l'API tourne en comportement « legacy » (timestamp sans fuseau).
        (await ScalarAsync<string>(conn,
            "SELECT data_type FROM information_schema.columns WHERE table_name = 'gps_positions' AND column_name = 'recorded_at'"))
            .Should().Be("timestamp without time zone", "Npgsql.EnableLegacyTimestampBehavior doit être actif comme dans Program.cs");

        _output.WriteLine("Clés étrangères vers gps_devices créées par EnsureCreated :");
        foreach (var line in await ListForeignKeysToDevicesAsync(conn)) _output.WriteLine("  " + line);
        _output.WriteLine("Index de vehicles / gps_devices créés par EnsureCreated :");
        foreach (var line in await ListAsync(conn,
                     "SELECT indexdef FROM pg_indexes WHERE tablename IN ('vehicles','gps_devices') ORDER BY tablename, indexname"))
            _output.WriteLine("  " + line);

        await ExecAsync(conn, """
            DO $$
            DECLARE r record;
            BEGIN
              FOR r IN SELECT conrelid::regclass AS tbl, conname FROM pg_constraint
                       WHERE contype = 'f' AND confrelid = 'gps_devices'::regclass
              LOOP
                EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I', r.tbl, r.conname);
              END LOOP;
            END $$;
            """);

        foreach (var fk in TnForeignKeys)
        {
            var action = fk.OnDelete switch { 'c' => " ON DELETE CASCADE", 'n' => " ON DELETE SET NULL", _ => "" };
            await ExecAsync(conn,
                $"ALTER TABLE {fk.Table} ADD CONSTRAINT \"{fk.Name}\" FOREIGN KEY ({fk.Column}) REFERENCES gps_devices(id){action}");
        }

        await ExecAsync(conn, """
            DROP INDEX IF EXISTS "IX_vehicles_gps_device_id";
            CREATE UNIQUE INDEX "IX_vehicles_gps_device_id" ON public.vehicles USING btree (gps_device_id);
            DROP INDEX IF EXISTS "IX_gps_devices_device_uid";
            CREATE UNIQUE INDEX "IX_gps_devices_device_uid" ON public.gps_devices USING btree (device_uid);
            """);

        var aligned = await ListForeignKeysToDevicesAsync(conn);
        aligned.Should().BeEquivalentTo(
            TnForeignKeys.Select(fk => $"{fk.Table}.{fk.Column} {fk.OnDelete}"),
            "le schéma de test doit porter exactement les clés étrangères de TN");
    }

    private static Task<List<string>> ListForeignKeysToDevicesAsync(NpgsqlConnection conn) => ListAsync(conn, """
        SELECT c.conrelid::regclass::text || '.' || a.attname || ' ' || c.confdeltype::text
        FROM pg_constraint c
        JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
        WHERE c.contype = 'f' AND c.confrelid = 'gps_devices'::regclass
        ORDER BY 1
        """);

    // ── 1. Npgsql : fournisseur, paramètre tableau et verrou réel ──

    [Fact]
    public async Task Npgsql_ProviderIsNpgsql_IntArrayParameterAccepted_AndRowsReallyLocked()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.
        await SeedHtz278Async();
        await SeedHtz255Async();

        await using var ctx = NewApiContext();
        // Le handler ne verrouille QUE si le fournisseur porte exactement ce nom.
        ctx.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");

        var strategy = ctx.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await ctx.Database.BeginTransactionAsync();
            // Même forme que le handler : IReadOnlyList<int>.ToArray() → int[] → integer[].
            IReadOnlyList<int> touched = new List<int> { WrongDevice278, RealDevice278 };
            await ctx.Database.ExecuteSqlRawAsync(LockStatement, new object[] { touched.ToArray() });

            await using var other = await OpenAsync();
            foreach (var id in touched)
            {
                var act = async () => await ExecAsync(other, $"SELECT id FROM gps_devices WHERE id = {id} FOR UPDATE NOWAIT");
                (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState
                    .Should().Be(PostgresErrorCodes.LockNotAvailable, $"la fiche #{id} doit être verrouillée");
            }
            // Une fiche hors liste n'est pas verrouillée.
            await ExecAsync(other, $"SELECT id FROM gps_devices WHERE id = {RealDevice255} FOR UPDATE NOWAIT");

            await tx.RollbackAsync();
        });
    }

    // ── 2. Rattachement HTZ 278 puis rejeu du formulaire ──

    [Fact]
    public async Task Attach_Htz278_ThenFormReplayInFreshContext_FinalStateVerifiedInRawSql()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.
        await SeedHtz278Async();
        await SeedHtz255Async();

        var recorder = new WriteRecorder();
        ReplaceVehicleDeviceResult replace;
        await using (var ctx = NewApiContext(recorder))
        {
            replace = await new ReplaceVehicleDeviceCommandHandler(ctx).Handle(
                new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);
        }
        _output.WriteLine("Remplacement : " + replace.Message);
        _output.WriteLine("Écritures envoyées à PostgreSQL, dans l'ordre :");
        foreach (var w in recorder.Writes) _output.WriteLine("  " + w);

        replace.Success.Should().BeTrue(replace.Message);
        replace.Mode.Should().Be(ReplaceVehicleDeviceCommandHandler.ModeAttach);
        replace.DeviceId.Should().Be(RealDevice278);
        replace.ReleasedDeviceId.Should().Be(WrongDevice278);

        // Ordre réel : verrou, puis véhicule déplacé AVANT la suppression de sa fiche. Avec
        // ON DELETE SET NULL, l'ordre inverse passerait sans erreur en base : on le vérifie
        // donc sur les commandes elles-mêmes.
        var writes = recorder.Writes;
        var lockAt = writes.FindIndex(w => w.StartsWith("SELECT id FROM gps_devices WHERE id = ANY", StringComparison.Ordinal));
        var vehicleAt = writes.FindIndex(w => w.StartsWith("UPDATE vehicles", StringComparison.Ordinal));
        var deleteAt = writes.FindIndex(w => w.StartsWith("DELETE FROM gps_devices", StringComparison.Ordinal));
        lockAt.Should().BeGreaterThanOrEqualTo(0, "le verrou FOR UPDATE doit être émis");
        vehicleAt.Should().BeGreaterThan(lockAt);
        deleteAt.Should().BeGreaterThan(vehicleAt, "le véhicule doit quitter la fiche avant sa suppression");
        writes.Count(w => w.StartsWith("UPDATE vehicles", StringComparison.Ordinal))
            .Should().Be(1, "un second UPDATE du véhicule (remise à NULL du lien) trahirait une mauvaise fixup EF");

        await AssertHtz278AttachedAsync(expectedMileage: 1000);

        // Rejeu tel que l'écran l'envoie : gpsDeviceId = fiche retenue, IMEI réel, MAT, SIM, kilométrage.
        UpdateAdminVehicleResult replay;
        await using (var ctx = NewApiContext())
        {
            replay = await new UpdateAdminVehicleCommandHandler(ctx).Handle(
                FormUpdate(Htz278, "HTZ 278", gpsDeviceId: replace.DeviceId, imei: RealImei278, mat: Mat278,
                    sim: null, mileage: 12345),
                CancellationToken.None);
        }
        replay.Success.Should().BeTrue(replay.Error);
        replay.Vehicle!.GpsDeviceId.Should().Be(RealDevice278);

        await AssertHtz278AttachedAsync(expectedMileage: 12345);

        // HTZ 255, semé dans la même base, n'a pas bougé.
        await using var conn = await OpenAsync();
        (await ScalarAsync<int>(conn, $"SELECT gps_device_id FROM vehicles WHERE id = {Htz255}")).Should().Be(WrongDevice255);
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM gps_devices WHERE id IN ({WrongDevice255}, {RealDevice255})")).Should().Be(2);
    }

    private async Task AssertHtz278AttachedAsync(int expectedMileage)
    {
        await using var conn = await OpenAsync();
        (await ScalarAsync<int>(conn, $"SELECT gps_device_id FROM vehicles WHERE id = {Htz278}")).Should().Be(RealDevice278);
        (await ScalarAsync<bool>(conn, $"SELECT has_gps FROM vehicles WHERE id = {Htz278}")).Should().BeTrue();
        (await ScalarAsync<int>(conn, $"SELECT mileage FROM vehicles WHERE id = {Htz278}")).Should().Be(expectedMileage);
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM gps_devices WHERE id = {WrongDevice278}")).Should().Be(0);
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM gps_positions WHERE device_id = {RealDevice278}")).Should().Be(3);
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM gps_alerts WHERE device_id = {RealDevice278}")).Should().Be(2);
        var row = await RowAsync(conn,
            $"SELECT company_id, status, device_uid, mat, fuel_sensor_mode FROM gps_devices WHERE id = {RealDevice278}");
        row["company_id"].Should().Be(BeliveSav);
        row["status"].Should().Be("assigned");
        row["device_uid"].Should().Be(RealImei278);
        row["mat"].Should().Be(Mat278);
        row["fuel_sensor_mode"].Should().Be("liters");
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM vehicles WHERE gps_device_id = {RealDevice278}")).Should().Be(1);
    }

    // ── 3. Rattachement HTZ 255 : SIM reprise de la fiche vide ──

    [Fact]
    public async Task Attach_Htz255_SimTakenFromEmptyDevice_ThenFormReplayWithSim_FinalStateVerifiedInRawSql()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.
        await SeedHtz255Async();

        ReplaceVehicleDeviceResult replace;
        await using (var ctx = NewApiContext())
        {
            replace = await new ReplaceVehicleDeviceCommandHandler(ctx).Handle(
                new ReplaceVehicleDeviceCommand(Htz255, RealImei255, NewMat: Mat255), CancellationToken.None);
        }
        _output.WriteLine("Remplacement : " + replace.Message);
        replace.Success.Should().BeTrue(replace.Message);
        replace.Mode.Should().Be(ReplaceVehicleDeviceCommandHandler.ModeAttach);

        await using (var conn = await OpenAsync())
        {
            (await ScalarAsync<string>(conn, $"SELECT sim_number FROM gps_devices WHERE id = {RealDevice255}")).Should().Be(Sim255);
        }

        UpdateAdminVehicleResult replay;
        await using (var ctx = NewApiContext())
        {
            replay = await new UpdateAdminVehicleCommandHandler(ctx).Handle(
                FormUpdate(Htz255, "HTZ 255", gpsDeviceId: replace.DeviceId, imei: RealImei255, mat: Mat255,
                    sim: Sim255, mileage: 54321),
                CancellationToken.None);
        }
        replay.Success.Should().BeTrue(replay.Error);

        await using var check = await OpenAsync();
        (await ScalarAsync<int>(check, $"SELECT gps_device_id FROM vehicles WHERE id = {Htz255}")).Should().Be(RealDevice255);
        (await ScalarAsync<int>(check, $"SELECT mileage FROM vehicles WHERE id = {Htz255}")).Should().Be(54321);
        (await ScalarAsync<long>(check, $"SELECT count(*) FROM gps_devices WHERE id = {WrongDevice255}")).Should().Be(0);
        (await ScalarAsync<long>(check, $"SELECT count(*) FROM gps_positions WHERE device_id = {RealDevice255}")).Should().Be(4);
        (await ScalarAsync<long>(check, $"SELECT count(*) FROM gps_alerts WHERE device_id = {RealDevice255}")).Should().Be(1);
        var row = await RowAsync(check, $"SELECT company_id, sim_number, device_uid FROM gps_devices WHERE id = {RealDevice255}");
        row["company_id"].Should().Be(BeliveSav);
        row["sim_number"].Should().Be(Sim255);
        row["device_uid"].Should().Be(RealImei255);
        (await ScalarAsync<long>(check, $"SELECT count(*) FROM gps_devices WHERE sim_number = '{Sim255}'")).Should().Be(1);
    }

    // ── 4. Mode renommage historique : fiche occupante vide supprimée, IMEI unique ──

    [Fact]
    public async Task Rename_VehicleDeviceHasHistory_EmptyOccupantDeleted_UniqueImeiIndexHolds()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.
        await SeedCompaniesAsync();
        await using (var seed = NewActorContext())
        {
            seed.GpsDevices.Add(Device(10, "860141070000011", "MAT-OLD", "11111111", BeliveSav, "assigned", "liters"));
            seed.GpsDevices.Add(Device(20, "860141070000029", null, null, Belive, "unassigned"));
            seed.Vehicles.Add(Vehicle(139, "HTZ 139", 10));
            AddPositions(seed, 10, 25);
            AddAlerts(seed, 10, 1);
            await seed.SaveChangesAsync();
        }

        var recorder = new WriteRecorder();
        ReplaceVehicleDeviceResult result;
        await using (var ctx = NewApiContext(recorder))
        {
            result = await new ReplaceVehicleDeviceCommandHandler(ctx).Handle(
                new ReplaceVehicleDeviceCommand(139, "860141070000029", NewSimNumber: "22222222"), CancellationToken.None);
        }
        _output.WriteLine("Remplacement : " + result.Message);
        _output.WriteLine("Écritures envoyées à PostgreSQL, dans l'ordre :");
        foreach (var w in recorder.Writes) _output.WriteLine("  " + w);

        result.Success.Should().BeTrue(result.Message);
        // L'index unique device_uid impose de supprimer la fiche occupante AVANT de renommer.
        var writes = recorder.Writes;
        writes.FindIndex(w => w.StartsWith("DELETE FROM gps_devices", StringComparison.Ordinal))
            .Should().BeGreaterThan(writes.FindIndex(w => w.Contains("FOR UPDATE", StringComparison.Ordinal)))
            .And.BeLessThan(writes.FindIndex(w => w.StartsWith("UPDATE gps_devices", StringComparison.Ordinal)));
        result.Mode.Should().Be(ReplaceVehicleDeviceCommandHandler.ModeRename);
        result.DeviceId.Should().Be(10);
        result.ReleasedDeviceId.Should().Be(20);

        await using var conn = await OpenAsync();
        (await ScalarAsync<long>(conn, "SELECT count(*) FROM gps_devices WHERE id = 20")).Should().Be(0);
        var row = await RowAsync(conn, "SELECT device_uid, sim_number, company_id FROM gps_devices WHERE id = 10");
        row["device_uid"].Should().Be("860141070000029");
        row["sim_number"].Should().Be("22222222");
        row["company_id"].Should().Be(BeliveSav);
        (await ScalarAsync<int>(conn, "SELECT gps_device_id FROM vehicles WHERE id = 139")).Should().Be(10);
        (await ScalarAsync<long>(conn, "SELECT count(*) FROM gps_positions WHERE device_id = 10")).Should().Be(25);
        (await ScalarAsync<long>(conn, "SELECT count(*) FROM gps_alerts WHERE device_id = 10")).Should().Be(1);
    }

    // ── 5. Verrou tenu ailleurs : le rattachement attend puis aboutit ──

    [Fact]
    public async Task Attach_WaitsForRowLockHeldByAnotherTransaction_ThenSucceeds()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.
        await SeedHtz278Async();

        await using var holder = await OpenAsync();
        var holderPid = await ScalarAsync<int>(holder, "SELECT pg_backend_pid()");
        await using var holderTx = await holder.BeginTransactionAsync();
        await ExecAsync(holder, $"SELECT id FROM gps_devices WHERE id = {WrongDevice278} FOR UPDATE");

        var sw = Stopwatch.StartNew();
        var attach = Task.Run(async () =>
        {
            await using var ctx = NewApiContext();
            return await new ReplaceVehicleDeviceCommandHandler(ctx).Handle(
                new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);
        });

        var blocked = await WaitUntilBlockedAsync("%FOR UPDATE%", holderPid, TimeSpan.FromSeconds(15), attach);
        blocked.Should().BeTrue("le rattachement doit attendre le verrou tenu sur la fiche #382060");
        attach.IsCompleted.Should().BeFalse();

        await Task.Delay(1500);
        attach.IsCompleted.Should().BeFalse("le verrou est toujours tenu");
        var heldFor = sw.Elapsed;
        await holderTx.CommitAsync();

        var result = await attach.WaitAsync(TimeSpan.FromSeconds(30));
        _output.WriteLine($"Rattachement terminé après {sw.Elapsed.TotalMilliseconds:F0} ms (verrou relâché à {heldFor.TotalMilliseconds:F0} ms) : {result.Message}");
        result.Success.Should().BeTrue(result.Message);
        sw.Elapsed.Should().BeGreaterThanOrEqualTo(heldFor);
        await AssertHtz278AttachedAsync(expectedMileage: 1000);
    }

    // ── 6. Position insérée sur la fiche vide AVANT le rattachement : refus, rien supprimé ──

    [Fact]
    public async Task Attach_Refused_WhenPositionWasInsertedOnEmptyDeviceBefore_NothingDeleted()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.
        await SeedHtz278Async();
        await using (var actor = NewActorContext())
        {
            AddPositions(actor, WrongDevice278, 1);
            await actor.SaveChangesAsync();
        }

        ReplaceVehicleDeviceResult result;
        await using (var ctx = NewApiContext())
        {
            result = await new ReplaceVehicleDeviceCommandHandler(ctx).Handle(
                new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);
        }
        _output.WriteLine("Refus : " + result.Message);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("les deux fiches contiennent des données").And.Contain("1 position(s)");
        await AssertNothingChangedAsync(positionsOnWrongDevice: 1);
    }

    // ── 7. Position insérée pendant que le rattachement attend le verrou : replanification, refus ──

    [Fact]
    public async Task Attach_Refused_WhenPositionInsertCommitsWhileWaitingForLock_ReplanUnderLockSeesIt()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.
        await SeedHtz278Async();

        // L'« ingestion » insère une position sur la fiche vide dans une transaction encore
        // ouverte : invisible pour le premier plan (READ COMMITTED), et sa vérification de
        // clé étrangère tient un FOR KEY SHARE sur gps_devices #382060, qui bloque le FOR UPDATE.
        await using var actor = NewActorContext();
        await using var actorTx = await actor.Database.BeginTransactionAsync();
        AddPositions(actor, WrongDevice278, 1);
        await actor.SaveChangesAsync();
        var actorPid = PidOf(actor);

        var attach = Task.Run(async () =>
        {
            await using var ctx = NewApiContext();
            return await new ReplaceVehicleDeviceCommandHandler(ctx).Handle(
                new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);
        });

        (await WaitUntilBlockedAsync("%FOR UPDATE%", actorPid, TimeSpan.FromSeconds(15), attach))
            .Should().BeTrue("le premier plan n'a pas vu la position non validée et le verrou doit attendre l'insertion");

        await actorTx.CommitAsync();

        var result = await attach.WaitAsync(TimeSpan.FromSeconds(30));
        _output.WriteLine("Refus après verrou : " + result.Message);
        result.Success.Should().BeFalse("le plan refait sous verrou voit la position désormais validée");
        result.Message.Should().Contain("les deux fiches contiennent des données").And.Contain("1 position(s)");
        await AssertNothingChangedAsync(positionsOnWrongDevice: 1);
    }

    // ── 8. Position insérée pendant que le rattachement TIENT le verrou : l'insertion attend puis échoue ──

    [Fact]
    public async Task PositionInsertedWhileAttachHoldsLock_WaitsThenFailsOnForeignKey_NoSilentCascade()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.
        await SeedHtz278Async();

        var pause = new PauseAfterLockInterceptor();
        var attach = Task.Run(async () =>
        {
            await using var ctx = NewApiContext(pause);
            return await new ReplaceVehicleDeviceCommandHandler(ctx).Handle(
                new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);
        });

        var lockedPid = await pause.LockTaken.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var insert = Task.Run(async () =>
        {
            await using var actor = NewActorContext();
            AddPositions(actor, WrongDevice278, 1);
            await actor.SaveChangesAsync();
        });

        (await WaitUntilBlockedAsync("%INSERT INTO gps_positions%", lockedPid, TimeSpan.FromSeconds(15), insert))
            .Should().BeTrue("l'insertion d'une position sur une fiche verrouillée doit attendre");

        pause.Release.SetResult();
        var result = await attach.WaitAsync(TimeSpan.FromSeconds(30));
        result.Success.Should().BeTrue(result.Message);

        var awaitInsert = () => insert.WaitAsync(TimeSpan.FromSeconds(30));
        var failure = await awaitInsert.Should().ThrowAsync<DbUpdateException>();
        var pg = failure.Which.InnerException.Should().BeOfType<PostgresException>().Subject;
        _output.WriteLine($"Insertion concurrente refusée : {pg.SqlState} {pg.MessageText}");
        pg.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);

        await AssertHtz278AttachedAsync(expectedMileage: 1000);
        await using var conn = await OpenAsync();
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM gps_positions WHERE device_id = {WrongDevice278}")).Should().Be(0);
    }

    // ── 9. Réponse du COMMIT perdue : la stratégie Npgsql rejoue, le résultat reste celui du rattachement ──

    [Fact]
    public async Task Attach_CommitResponseLost_NpgsqlRetryStrategyReplays_ResultIsTheAttachNotARename()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.
        await SeedHtz278Async();

        var lost = new LoseFirstCommitResponseInterceptor();
        var recorder = new WriteRecorder();
        ReplaceVehicleDeviceResult result;
        await using (var ctx = NewApiContext(lost, recorder))
        {
            result = await new ReplaceVehicleDeviceCommandHandler(ctx).Handle(
                new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278), CancellationToken.None);
        }
        _output.WriteLine("Résultat après rejeu : " + result.Message);
        foreach (var w in recorder.Writes) _output.WriteLine("  " + w);

        lost.Thrown.Should().BeTrue("la réponse du premier COMMIT doit avoir été perdue");
        result.Success.Should().BeTrue(result.Message);
        result.Mode.Should().Be(ReplaceVehicleDeviceCommandHandler.ModeAttach);
        result.DeviceId.Should().Be(RealDevice278);
        result.ReleasedDeviceId.Should().Be(WrongDevice278);
        result.Message.Should().Contain($"La fiche #{WrongDevice278} (IMEI {WrongImei278}, jamais utilisée) a été supprimée.")
            .And.NotContain("→");
        recorder.Writes.Count(w => w.StartsWith("DELETE FROM gps_devices", StringComparison.Ordinal))
            .Should().Be(1, "le rejeu ne doit rien réécrire");
        recorder.Writes.Count(w => w.Contains("FOR UPDATE", StringComparison.Ordinal))
            .Should().Be(1, "le rejeu reconnaît l'état atteint avant de reprendre un verrou");
        await AssertHtz278AttachedAsync(expectedMileage: 1000);
    }

    // ── 10. Appareil choisi dans la liste : proposition, remplacement, rejeu de l'écran ──

    [Fact]
    public async Task DeviceChosenFromList_ProposalThenAttachThenScreenReplay_NothingTransferredBeforeConfirmation()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.
        await SeedHtz278Async();

        // 1. Le formulaire désigne #384940 par son id (liste « appareil existant »).
        UpdateAdminVehicleResult proposal;
        await using (var ctx = NewApiContext())
        {
            proposal = await new UpdateAdminVehicleCommandHandler(ctx).Handle(
                FormUpdate(Htz278, "HTZ 278", gpsDeviceId: RealDevice278, imei: RealImei278, mat: Mat278, sim: null, mileage: 12345),
                CancellationToken.None);
        }
        _output.WriteLine("Proposition : " + proposal.Error);
        proposal.Success.Should().BeFalse();
        proposal.ReplaceSuggested.Should().BeTrue();
        proposal.Error.Should().StartWith("Cet appareil GPS appartient à une autre société.");
        await AssertNothingChangedAsync(positionsOnWrongDevice: 0);

        // 2. Confirmation : remplacement avec les valeurs du formulaire (mode « liters »).
        ReplaceVehicleDeviceResult replace;
        await using (var ctx = NewApiContext())
        {
            replace = await new ReplaceVehicleDeviceCommandHandler(ctx).Handle(
                new ReplaceVehicleDeviceCommand(Htz278, RealImei278, NewMat: Mat278, NewFuelSensorMode: "liters"),
                CancellationToken.None);
        }
        replace.Success.Should().BeTrue(replace.Message);

        // 3. Rejeu de l'écran : fiche retenue, sans identifiants ni réglages déjà appliqués.
        UpdateAdminVehicleResult replay;
        await using (var ctx = NewApiContext())
        {
            replay = await new UpdateAdminVehicleCommandHandler(ctx).Handle(
                FormUpdate(Htz278, "HTZ 278", gpsDeviceId: replace.DeviceId, imei: null, mat: null, sim: null, mileage: 12345)
                    with { GpsFuelSensorMode = null },
                CancellationToken.None);
        }
        replay.Success.Should().BeTrue(replay.Error);
        await AssertHtz278AttachedAsync(expectedMileage: 12345);
    }

    /// <summary>
    /// Laisse PostgreSQL valider le premier COMMIT, puis simule la perte de sa réponse par
    /// une erreur transitoire (TimeoutException, que la stratégie Npgsql rejoue).
    /// </summary>
    private sealed class LoseFirstCommitResponseInterceptor : DbTransactionInterceptor
    {
        public bool Thrown { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Thrown) return Task.CompletedTask;
            Thrown = true;
            throw new TimeoutException("Réponse du COMMIT perdue (simulée).");
        }
    }

    /// <summary>
    /// Met le handler en pause juste après son « SELECT … FOR UPDATE » (verrou acquis,
    /// transaction ouverte), pour ouvrir la fenêtre où l'ingestion écrirait.
    /// </summary>
    private sealed class PauseAfterLockInterceptor : DbCommandInterceptor
    {
        public TaskCompletionSource<int> LockTaken { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal) && !LockTaken.Task.IsCompleted)
            {
                LockTaken.SetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                await Release.Task;
            }
            return result;
        }
    }

    /// <summary>
    /// Relève, dans l'ordre d'envoi, chaque instruction d'écriture (INSERT / UPDATE / DELETE)
    /// et le verrou FOR UPDATE, une ligne par instruction (EF regroupe plusieurs
    /// instructions dans une même commande).
    /// </summary>
    private sealed class WriteRecorder : DbCommandInterceptor
    {
        private readonly List<string> _writes = new();

        public List<string> Writes
        {
            get { lock (_writes) return _writes.ToList(); }
        }

        private void Record(DbCommand command)
        {
            var statements = command.CommandText
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => string.Join(' ', s.Split(new[] { '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)))
                .Where(s => s.StartsWith("INSERT", StringComparison.Ordinal)
                            || s.StartsWith("UPDATE", StringComparison.Ordinal)
                            || s.StartsWith("DELETE", StringComparison.Ordinal)
                            || s.Contains("FOR UPDATE", StringComparison.Ordinal));
            lock (_writes) _writes.AddRange(statements);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    // ── Semis ──

    private async Task SeedCompaniesAsync()
    {
        await using var seed = NewActorContext();
        seed.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        var belive = TestDataBuilder.CreateSociete(id: Belive, subscriptionTypeId: 1);
        belive.Name = "BELIVE";
        var sav = TestDataBuilder.CreateSociete(id: BeliveSav, subscriptionTypeId: 1);
        sav.Name = "BELIVE SAV";
        seed.Societes.AddRange(belive, sav);
        await seed.SaveChangesAsync();
    }

    /// <summary>
    /// HTZ 278 : véhicule 355 (société 6) sur la fiche #382060 à IMEI mal saisi, strictement
    /// vide ; vrai boîtier #384940 (même MAT) créé par l'ingestion en société 1, sans
    /// véhicule, avec 3 positions et 2 alertes.
    /// </summary>
    private async Task SeedHtz278Async()
    {
        if (!await CompaniesSeededAsync()) await SeedCompaniesAsync();
        await using var seed = NewActorContext();
        seed.GpsDevices.Add(Device(WrongDevice278, WrongImei278, Mat278, null, BeliveSav, "assigned", "liters"));
        seed.GpsDevices.Add(Device(RealDevice278, RealImei278, Mat278, null, Belive, "unassigned"));
        seed.Vehicles.Add(Vehicle(Htz278, "HTZ 278", WrongDevice278));
        AddPositions(seed, RealDevice278, 3);
        AddAlerts(seed, RealDevice278, 2);
        await seed.SaveChangesAsync();
    }

    /// <summary>HTZ 255 : la SIM 92005328 est sur la fiche vide #319546 ; vrai boîtier #340768 sans SIM.</summary>
    private async Task SeedHtz255Async()
    {
        if (!await CompaniesSeededAsync()) await SeedCompaniesAsync();
        await using var seed = NewActorContext();
        seed.GpsDevices.Add(Device(WrongDevice255, WrongImei255, Mat255, Sim255, BeliveSav, "assigned", "liters"));
        seed.GpsDevices.Add(Device(RealDevice255, RealImei255, Mat255, null, Belive, "unassigned"));
        seed.Vehicles.Add(Vehicle(Htz255, "HTZ 255", WrongDevice255));
        AddPositions(seed, RealDevice255, 4);
        AddAlerts(seed, RealDevice255, 1);
        await seed.SaveChangesAsync();
    }

    private async Task<bool> CompaniesSeededAsync()
    {
        await using var conn = await OpenAsync();
        return await ScalarAsync<long>(conn, "SELECT count(*) FROM societes") > 0;
    }

    private async Task AssertNothingChangedAsync(int positionsOnWrongDevice)
    {
        await using var conn = await OpenAsync();
        (await ScalarAsync<int>(conn, $"SELECT gps_device_id FROM vehicles WHERE id = {Htz278}")).Should().Be(WrongDevice278);
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM gps_devices WHERE id IN ({WrongDevice278}, {RealDevice278})")).Should().Be(2);
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM gps_positions WHERE device_id = {WrongDevice278}")).Should().Be(positionsOnWrongDevice);
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM gps_positions WHERE device_id = {RealDevice278}")).Should().Be(3);
        (await ScalarAsync<long>(conn, $"SELECT count(*) FROM gps_alerts WHERE device_id = {RealDevice278}")).Should().Be(2);
        var real = await RowAsync(conn, $"SELECT company_id, status FROM gps_devices WHERE id = {RealDevice278}");
        real["company_id"].Should().Be(Belive);
        real["status"].Should().Be("unassigned");
    }

    private static UpdateAdminVehicleCommand FormUpdate(
        int vehicleId, string plate, int? gpsDeviceId, string? imei, string? mat, string? sim, int mileage) => new(
        Id: vehicleId,
        Name: plate,
        Type: "voiture", Brand: null, Model: null,
        Plate: plate,
        Year: 2024, Color: null, Status: "available",
        HasGps: true, Mileage: mileage,
        FuelType: "diesel", FuelTankCapacity: null,
        CompanyId: BeliveSav,
        GpsDeviceId: gpsDeviceId,
        GpsImei: imei,
        GpsMat: mat,
        GpsBrand: "NORON", GpsModel: "NR024", GpsFirmwareVersion: null, GpsFuelSensorMode: "liters",
        GpsSimNumber: sim, GpsSimOperator: null, GpsInstallationDate: null);

    private static GpsDevice Device(int id, string imei, string? mat, string? sim, int companyId, string status,
        string fuelSensorMode = "raw_255") => new()
    {
        Id = id,
        DeviceUid = imei,
        Mat = mat,
        SimNumber = sim,
        CompanyId = companyId,
        Status = status,
        FuelSensorMode = fuelSensorMode,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private static Vehicle Vehicle(int id, string plate, int deviceId) => new()
    {
        Id = id,
        Name = plate,
        Plate = plate,
        Type = "voiture",
        Status = "available",
        CompanyId = BeliveSav,
        GpsDeviceId = deviceId,
        HasGps = true,
        Mileage = 1000,
        FuelType = "diesel"
    };

    private static void AddPositions(GisDbContext context, int deviceId, int count)
    {
        var start = new DateTime(2026, 9, 14, 6, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < count; i++)
            context.GpsPositions.Add(new GpsPosition
            {
                DeviceId = deviceId,
                RecordedAt = start.AddMinutes(i * 5 + Random.Shared.Next(0, 4)),
                Latitude = 36.8,
                Longitude = 10.18,
                IsValid = true,
                CreatedAt = DateTime.UtcNow
            });
    }

    private static void AddAlerts(GisDbContext context, int deviceId, int count)
    {
        for (var i = 0; i < count; i++)
            context.GpsAlerts.Add(new GpsAlert
            {
                DeviceId = deviceId,
                CompanyId = Belive,
                Type = "overspeed",
                Message = "Excès de vitesse",
                Timestamp = new DateTime(2026, 9, 14, 7, i, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow
            });
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

    private static int PidOf(GisDbContext context) =>
        ((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID;

    /// <summary>
    /// Attend qu'une session de CETTE base soit en attente de verrou (pg_stat_activity),
    /// bloquée par <paramref name="blockerPid"/> (pg_blocking_pids), sur une requête
    /// correspondant à <paramref name="queryLike"/>. Échoue tôt si la tâche observée se termine.
    /// </summary>
    private async Task<bool> WaitUntilBlockedAsync(string queryLike, int blockerPid, TimeSpan timeout, Task observed)
    {
        await using var monitor = await OpenAsync();
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (observed.IsCompleted)
            {
                if (observed.IsFaulted) await observed; // remonte l'exception réelle
                return false;
            }

            await using var cmd = new NpgsqlCommand("""
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database()
                  AND wait_event_type = 'Lock'
                  AND query LIKE @q
                  AND @blocker = ANY(pg_blocking_pids(pid))
                """, monitor);
            cmd.Parameters.AddWithValue("q", queryLike);
            cmd.Parameters.AddWithValue("blocker", blockerPid);
            if ((long)(await cmd.ExecuteScalarAsync())! > 0)
            {
                _output.WriteLine($"Attente de verrou constatée après {sw.Elapsed.TotalMilliseconds:F0} ms (bloquée par pid {blockerPid}).");
                return true;
            }
            await Task.Delay(50);
        }
        return false;
    }
}
