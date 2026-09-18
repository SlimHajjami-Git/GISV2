using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Dashboard.Queries.GetGpaDashboard;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Moq;

namespace GisAPI.Tests.Application.Dashboard;

/// <summary>
/// Tableau de bord GPA, alertes de documents — recette GPA, DEF-035.
///
/// <para>Les alertes prenaient <c>expiry.Value.Date</c> : le jour LOCAL de
/// l'instant que Npgsql legacy relit en heure du serveur. Hors UTC, une
/// échéance saisie à 23:59:59 UTC (ancienne correction d'échéance) tombait au
/// lendemain : « 10 j » quand l'écran Échéances disait 9, une vignette échue la
/// veille annoncée « à renouveler (0 j) », une visite technique à J+60 sortie de
/// la fenêtre. <see cref="ContexteNpgsqlLegacy"/> reproduit cette relecture sur
/// SQLite.</para>
/// </summary>
public class GpaDashboardExpiryCalendarTests
{
    private const int CompanyId = 1;

    // « Aujourd'hui » = 11/09/2026, horloge figée.
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(int month, int day, int h, int m, int s) =>
        new(2026, month, day, h, m, s, DateTimeKind.Utc);

    private sealed class ContexteNpgsqlLegacy : TestGisDbContext
    {
        public ContexteNpgsqlLegacy(DbContextOptions<TestGisDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Instant UTC écrit tel quel, relu en heure locale (Kind=Local) comme Npgsql legacy.
            var timestamptz = new ValueConverter<DateTime, DateTime>(
                v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc).ToLocalTime());

            var vehicle = modelBuilder.Entity<Vehicle>();
            vehicle.Property(v => v.InsuranceExpiry).HasConversion(timestamptz);
            vehicle.Property(v => v.TechnicalInspectionExpiry).HasConversion(timestamptz);
            vehicle.Property(v => v.TaxExpiry).HasConversion(timestamptz);
            vehicle.Property(v => v.RegistrationExpiry).HasConversion(timestamptz);
            vehicle.Property(v => v.TransportPermitExpiry).HasConversion(timestamptz);
        }
    }

    private static TestGisDbContext NpgsqlLegacy()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys = OFF;";
            cmd.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<TestGisDbContext>()
            .UseSqlite(connection, contextOwnsConnection: true)
            .Options;
        var ctx = new ContexteNpgsqlLegacy(options);
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static IDateTimeProvider Clock()
    {
        var m = new Mock<IDateTimeProvider>();
        m.Setup(c => c.UtcNow).Returns(Now);
        m.Setup(c => c.Today).Returns(DateOnly.FromDateTime(Now));
        return m.Object;
    }

    private static ICurrentTenantService SystemAdmin()
    {
        var m = TestDbContextFactory.CreateMockTenantService(CompanyId);
        m.Setup(t => t.IsSystemAdmin).Returns(true);
        return m.Object;
    }

    [FactMachineHorsUtc]
    public async Task Les_alertes_de_documents_comptent_le_jour_UTC_quelle_que_soit_l_heure_stockee()
    {
        using var ctx = NpgsqlLegacy();
        ctx.Vehicles.AddRange(
            // Même assurance au 20/09, saisie à minuit ou à 23:59:59 UTC : 9 jours dans les deux cas.
            new Vehicle { Id = 1, CompanyId = CompanyId, Name = "QA minuit", Plate = "QA-001", InsuranceExpiry = Utc(9, 20, 0, 0, 0) },
            new Vehicle { Id = 2, CompanyId = CompanyId, Name = "QA tardive", Plate = "QA-002", InsuranceExpiry = Utc(9, 20, 23, 59, 59) },
            // Échue la veille : expirée, pas « à renouveler (0 j) ».
            new Vehicle { Id = 3, CompanyId = CompanyId, Name = "QA veille", Plate = "QA-003", TaxExpiry = Utc(9, 10, 23, 59, 59) },
            // J+60 : borne incluse de la fenêtre documents.
            new Vehicle { Id = 4, CompanyId = CompanyId, Name = "QA borne", Plate = "QA-004", TechnicalInspectionExpiry = Utc(11, 10, 23, 59, 59) });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var dashboard = await new GetGpaDashboardQueryHandler(ctx, SystemAdmin(), Clock())
            .Handle(new GetGpaDashboardQuery(new DateTime(2026, 1, 1), new DateTime(2026, 9, 11)), CancellationToken.None);

        var documents = dashboard.Alerts!.Where(a => a.Kind == "document").ToDictionary(a => a.Plate!);
        documents.Keys.Should().BeEquivalentTo(new[] { "QA-001", "QA-002", "QA-003", "QA-004" });

        foreach (var plate in new[] { "QA-001", "QA-002" })
        {
            documents[plate].DaysLeft.Should().Be(9, "l'assurance au 20/09 est à 9 jours calendaires du 11/09 ({0})", plate);
            documents[plate].Detail.Should().Be("expire le 20/09/2026 (9 j)");
            documents[plate].Date.Should().Be(Utc(9, 20, 0, 0, 0));
            documents[plate].Date!.Value.Kind.Should().Be(DateTimeKind.Utc);
        }

        var vignette = documents["QA-003"];
        vignette.Severity.Should().Be("critical");
        vignette.Title.Should().Be("Vignette expirée");
        vignette.Detail.Should().Be("expirée le 10/09/2026 (il y a 1 j)");
        vignette.DaysLeft.Should().Be(-1);

        var visite = documents["QA-004"];
        visite.DaysLeft.Should().Be(60);
        visite.Detail.Should().Be("expire le 10/11/2026 (60 j)");

        dashboard.AlertCounts!.Critical.Should().Be(1);
    }
}
