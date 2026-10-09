using FluentAssertions;
using GisAPI.Application.Features.Admin.GpsDevices;
using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.MultiTenancy;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace GisAPI.Tests.Integration;

/// <summary>
/// Suppression définitive d'un boîtier (09/10/2026) sur un VRAI PostgreSQL, avec les clés
/// étrangères de TN : les tests SQLite tournent avec PRAGMA foreign_keys = OFF et ne prouvent
/// ni l'ordre des suppressions (device_commands, device_events, tow_events, alertes, arrêts,
/// relevés carburant sont en NO ACTION vers gps_devices), ni les branches PostgreSQL du
/// handler (SELECT … FOR UPDATE, lots par ctid, frame_debug_log). Ici, un mauvais ordre
/// tombe en 23503 comme en production.
///
/// ACTIVATION — comme ReplaceVehicleDevicePostgresTests : rien ne tourne sans la variable
/// GIS_PG_TEST_CONN (serveur JETABLE, une base par test, jamais TN ni DZ). Exemple :
///   GIS_PG_TEST_CONN='Host=localhost;Port=55432;Username=postgres;Password=pgtest' \
///     dotnet test --filter FullyQualifiedName~SuppressionBoitierPostgresTests
/// </summary>
public class SuppressionBoitierPostgresTests : IAsyncLifetime
{
    private static readonly string? BaseConnectionString = Environment.GetEnvironmentVariable("GIS_PG_TEST_CONN");
    private static bool Enabled => !string.IsNullOrWhiteSpace(BaseConnectionString);

    static SuppressionBoitierPostgresTests()
    {
        if (Enabled) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    private const int Belive = 1;
    private const int Hertz = 4;
    private const int Boitier = 190939;
    private const string Imei = "860141076687632";
    private const int Vehicule = 524;

    // Clés étrangères vers gps_devices telles qu'elles existent sur TN (pg_constraint, 09/10/2026).
    private static readonly (string Table, string Column, string Name, char OnDelete)[] TnForeignKeys =
    {
        ("battery_start_readings", "device_id", "FK_battery_start_readings_gps_devices_device_id", 'c'),
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

    public SuppressionBoitierPostgresTests(ITestOutputHelper output) => _output = output;

    // ── Cycle de vie : base neuve par test ──

    public async Task InitializeAsync()
    {
        if (!Enabled) return;
        _databaseName = $"gisv2_boitier_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..48];
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
            await AlignWithTnAsync();
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

    /// <summary>GisDbContext configuré comme l'API (Npgsql + EnableRetryOnFailure), locataire = administrateur système.</summary>
    private GisDbContext NewApiContext()
    {
        var builder = new DbContextOptionsBuilder<GisDbContext>()
            .UseNpgsql(_dataSource!, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(GisDbContext).Assembly.GetName().Name);
                npgsqlOptions.EnableRetryOnFailure(3);
            });
        builder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        var tenant = new CurrentTenantService();
        tenant.SetTenant(Belive, 1, "admin@belive.tn", new[] { "system_admin" }, Array.Empty<string>());
        return new GisDbContext(builder.Options, tenant);
    }

    /// <summary>
    /// Les clés étrangères de TN vers gps_devices remplacent celles du modèle EF, et le journal
    /// technique de l'ingestion (sans entité EF) est créé comme sur TN, index de la migration 057 compris.
    /// </summary>
    private async Task AlignWithTnAsync()
    {
        await using var conn = await OpenAsync();
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
            CREATE TABLE frame_debug_log (
                id bigserial PRIMARY KEY, device_id integer NULL, device_uid text NULL, mat text NULL,
                frame_type text NOT NULL, flags_hex text NULL, flags_raw smallint NULL, raw_frame text NOT NULL,
                reason text NOT NULL, peer text NULL, created_at timestamptz NOT NULL DEFAULT now());
            CREATE INDEX ix_frame_debug_log_device_id ON frame_debug_log (device_id);
            CREATE INDEX ix_frame_debug_log_device_uid ON frame_debug_log (device_uid);
            """);
    }

    // ── Le test ──

    [Fact]
    public async Task La_suppression_passe_sous_les_cles_etrangeres_de_TN_et_ne_laisse_rien_derriere()
    {
        if (!Enabled) return; // GIS_PG_TEST_CONN absente : voir le commentaire de la classe.

        await SemerAsync();

        DeleteGpsDeviceResult result;
        await using (var ctx = NewApiContext())
        {
            ctx.Database.ProviderName.Should().Be("Npgsql.EntityFrameworkCore.PostgreSQL");
            var handler = new DeleteGpsDeviceCommandHandler(ctx, Tenant(), NullLogger<DeleteGpsDeviceCommandHandler>.Instance)
            {
                BatchSize = 3, // 7 positions → 3 + 3 + 1 : les lots par ctid tournent vraiment
            };
            result = await handler.Handle(new DeleteGpsDeviceCommand(Boitier, " 8601 4107 6687 632 "), CancellationToken.None);
        }

        result.Success.Should().BeTrue(result.Message);
        result.VehicleDeleted.Should().BeTrue();
        _output.WriteLine(result.Message);
        foreach (var d in result.Deleted!) _output.WriteLine($"  {d.Table}: {d.Rows}");

        await using (var fresh = NewApiContext())
        {
            (await fresh.GpsDevices.IgnoreQueryFilters().CountAsync(d => d.Id == Boitier)).Should().Be(0);
            (await fresh.Vehicles.IgnoreQueryFilters().CountAsync(v => v.Id == Vehicule)).Should().Be(0);
            (await fresh.GpsPositions.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.GpsAlerts.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.VehicleStops.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.FuelRecords.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.DeviceCommands.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.DeviceEvents.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.TowEvents.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.BatteryStartReadings.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.GeofenceEvents.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.PoiVisits.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.Notifications.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.FuelEntries.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.Trips.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.Tours.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.TourWaypoints.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.VehicleCosts.IgnoreQueryFilters().CountAsync()).Should().Be(0);
            (await fresh.Repairs.IgnoreQueryFilters().CountAsync()).Should().Be(0);

            // Dossiers de sinistre : conservés, détachés du boîtier et du véhicule, libellé figé.
            var sinistres = await fresh.AccidentEvents.IgnoreQueryFilters().OrderBy(a => a.Id).ToListAsync();
            sinistres.Should().HaveCount(2);
            sinistres.Should().OnlyContain(a => a.GpsDeviceId == null && a.VehicleId == null);
            sinistres.First(a => a.Id == 8).VehicleLabel.Should().Be("HTZ 345");

            var blocage = await fresh.GpsDeviceBlocklist.SingleAsync(b => b.DeviceUid == Imei);
            blocage.DeviceIdBefore.Should().Be(Boitier);
            blocage.CompanyIdBefore.Should().Be(Hertz);

            (await fresh.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.Action == "gps_device_deleted" && a.EntityId == Boitier)).Should().Be(1);
        }

        await using (var conn = await OpenAsync())
        {
            // Journal technique : les lignes du boîtier partent, sauf la trace de blocage.
            (await ScalarAsync<long>(conn, "SELECT count(*) FROM frame_debug_log")).Should().Be(1);
            (await ScalarAsync<string>(conn, "SELECT reason FROM frame_debug_log")).Should().Be("blocked_deleted");

            // La clé étrangère de TN est toujours là : plus rien ne peut se rattacher à l'ancien id
            // (l'ingest tombe en 23503, oublie son cache et consulte la liste de blocage).
            (await ScalarAsync<long>(conn,
                "SELECT count(*) FROM pg_constraint WHERE conname = 'FK_gps_positions_gps_devices_device_id' AND confdeltype = 'c'"))
                .Should().Be(1);
            (await ScalarAsync<long>(conn, $"SELECT count(*) FROM gps_devices WHERE id = {Boitier}")).Should().Be(0);
        }
    }

    // ── Graine : tout ce qu'un boîtier et son véhicule peuvent porter ──

    private async Task SemerAsync()
    {
        await using var ctx = NewApiContext();
        ctx.SubscriptionTypes.Add(TestDataBuilder.CreateSubscriptionType());
        var belive = TestDataBuilder.CreateSociete(id: Belive, subscriptionTypeId: 1);
        belive.Name = "BELIVE";
        var hertz = TestDataBuilder.CreateSociete(id: Hertz, subscriptionTypeId: 1);
        hertz.Name = "HERTZ";
        ctx.Societes.AddRange(belive, hertz);
        await ctx.SaveChangesAsync();

        // Les commandes boîtier et les notifications portent un utilisateur (clé étrangère NOT NULL).
        ctx.Roles.Add(new Role { Id = 1, Name = "Administrateur", SocieteId = Belive, IsCompanyAdmin = true, IsSystemRole = true });
        await ctx.SaveChangesAsync();
        var admin = TestDataBuilder.CreateUser(id: 1, companyId: Belive, email: "admin@belive.tn");
        admin.RoleId = 1;
        ctx.Users.Add(admin);
        await ctx.SaveChangesAsync();

        // Clés étrangères NOT NULL des lignes semées plus bas (relevées sur la base EnsureCreated).
        ctx.FuelTypes.Add(new FuelType { Id = 1, Code = "diesel", Name = "Diesel" });
        ctx.Geofences.Add(TestDataBuilder.CreateGeofence(id: 1, companyId: Hertz, name: "Dépôt"));
        ctx.PointsOfInterest.Add(new PointOfInterest { Id = 1, Name = "Agence", CompanyId = Hertz });
        await ctx.SaveChangesAsync();

        ctx.GpsDevices.Add(new GpsDevice
        {
            Id = Boitier, DeviceUid = Imei, Mat = "NR08G1116", CompanyId = Hertz, Status = "assigned",
            LastCommunication = DateTime.UtcNow.AddHours(-1), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        ctx.Vehicles.Add(new Vehicle
        {
            Id = Vehicule, Name = "HTZ 345", Plate = "HTZ 345", Type = "voiture", Status = "available",
            CompanyId = Hertz, GpsDeviceId = Boitier, HasGps = true, Mileage = 0, FuelType = "diesel"
        });
        await ctx.SaveChangesAsync();

        var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 7; i++)
            ctx.GpsPositions.Add(new GpsPosition { DeviceId = Boitier, RecordedAt = start.AddMinutes(i), Latitude = 36.8, Longitude = 10.18, IsValid = true });
        for (var i = 1; i <= 3; i++)
            ctx.GpsAlerts.Add(new GpsAlert { DeviceId = Boitier, Timestamp = start });
        ctx.VehicleStops.Add(new VehicleStop { VehicleId = Vehicule, DeviceId = Boitier, StartTime = start });
        ctx.VehicleStops.Add(new VehicleStop { VehicleId = Vehicule, DeviceId = Boitier, StartTime = start.AddHours(1) });
        ctx.FuelRecords.Add(new FuelRecord { VehicleId = Vehicule, DeviceId = Boitier, RecordedAt = start });
        ctx.FuelRecords.Add(new FuelRecord { VehicleId = Vehicule, DeviceId = Boitier, RecordedAt = start.AddHours(1) });
        ctx.DeviceCommands.Add(new DeviceCommand { DeviceId = Boitier, UserId = 1 });
        ctx.DeviceEvents.Add(new DeviceEvent { DeviceId = Boitier, EventAt = start });
        ctx.TowEvents.Add(new TowEvent { DeviceId = Boitier, VehicleId = Vehicule, StartedAt = start, LastSeenAt = start });
        ctx.BatteryStartReadings.Add(new BatteryStartReading { DeviceId = Boitier, StartAt = start, BatteryRaw = 80 });
        ctx.GeofenceEvents.Add(new GeofenceEvent { DeviceId = Boitier, VehicleId = Vehicule, GeofenceId = 1 });
        ctx.PoiVisits.Add(new PoiVisit { DeviceId = Boitier, VehicleId = Vehicule, PoiId = 1 });
        ctx.Notifications.Add(new Notification { UserId = 1, CompanyId = Hertz, ReferenceType = "device", ReferenceId = Boitier });
        ctx.Notifications.Add(new Notification { UserId = 1, CompanyId = Hertz, ReferenceType = "vehicle", ReferenceId = Vehicule });
        ctx.AccidentEvents.Add(new AccidentEvent { Id = 7, GpsDeviceId = Boitier, DeviceUid = Imei, CompanyId = Hertz, IncidentAt = start });
        ctx.AccidentEvents.Add(new AccidentEvent { Id = 8, VehicleId = Vehicule, DeviceUid = Imei, CompanyId = Hertz, IncidentAt = start });
        ctx.FuelEntries.Add(new FuelEntry { VehicleId = Vehicule, CompanyId = Hertz, InvoiceDate = start, FuelTypeId = 1 });
        ctx.FuelEntries.Add(new FuelEntry { VehicleId = Vehicule, CompanyId = Hertz, InvoiceDate = start, FuelTypeId = 1 });
        ctx.Trips.Add(new Trip { VehicleId = Vehicule, StartTime = start });
        ctx.Tours.Add(new Tour { Id = 1, VehicleId = Vehicule, CompanyId = Hertz, ScheduledStartTime = start });
        ctx.TourWaypoints.Add(new TourWaypoint { TourId = 1 });
        ctx.VehicleCosts.Add(new VehicleCost { VehicleId = Vehicule, CompanyId = Hertz });
        ctx.Repairs.Add(new Repair { VehicleId = Vehicule, SocieteId = Hertz });
        await ctx.SaveChangesAsync();

        await using var conn = await OpenAsync();
        await ExecAsync(conn, $"""
            INSERT INTO frame_debug_log (device_id, device_uid, frame_type, raw_frame, reason)
            VALUES ({Boitier}, '{Imei}', 'info', 'info(imei={Imei})', 'mat_mismatch: stored=NR08G1116 incoming=NR08G1115'),
                   (NULL, '{Imei}', 'position', '', 'blocked_deleted');
            """);
    }

    private static CurrentTenantService Tenant()
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(Belive, 1, "admin@belive.tn", new[] { "system_admin" }, Array.Empty<string>());
        return tenant;
    }

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
}
