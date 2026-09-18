using FluentAssertions;
using GisAPI.Application.Features.Documents;
using GisAPI.Application.Features.Documents.Commands;
using GisAPI.Application.Features.Documents.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Documents;

/// <summary>
/// Écran Échéances — campagne de test Calypso GPA.
///
/// <para>DEF-035 : les jours restants variaient d'un jour selon le chemin de
/// saisie (00:00, 12:00 ou 23:59:59 stockés). Npgsql en mode legacy relit les
/// colonnes timestamptz en heure LOCALE du serveur : sur un serveur à UTC+1,
/// 23:59:59 UTC devient le lendemain et <c>.Date</c> comptait un jour de trop.
/// <see cref="ContexteNpgsqlLegacy"/> reproduit cette relecture sur SQLite (sur
/// une machine réglée exactement sur UTC, l'heure locale est l'heure UTC et le
/// décalage ne peut pas apparaître).</para>
///
/// <para>DEF-033 : /documents/alerts ignorait les permis de conducteur, comptés
/// par /expiries et /stats. DEF-055 : les échéances non renseignées
/// s'intercalaient entre « bientôt » et « à jour ».</para>
/// </summary>
public class DocumentExpiryCalendarTests
{
    private const int CompanyId = 7;
    private const int OtherCompanyId = 5;
    private const int RestrictedUserId = 42;

    private static readonly DateTime Today = DateTime.UtcNow.Date;

    private static DateTime At(DateTime day, int h, int m, int s) =>
        new(day.Year, day.Month, day.Day, h, m, s, DateTimeKind.Utc);

    /// <summary>Relecture des échéances comme Npgsql legacy : instant UTC rendu en heure locale.</summary>
    private sealed class ContexteNpgsqlLegacy : TestGisDbContext
    {
        public ContexteNpgsqlLegacy(DbContextOptions<TestGisDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var timestamptz = new ValueConverter<DateTime, DateTime>(
                v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc).ToLocalTime());

            var vehicle = modelBuilder.Entity<Vehicle>();
            vehicle.Property(v => v.InsuranceExpiry).HasConversion(timestamptz);
            vehicle.Property(v => v.TechnicalInspectionExpiry).HasConversion(timestamptz);
            vehicle.Property(v => v.TaxExpiry).HasConversion(timestamptz);
            vehicle.Property(v => v.RegistrationExpiry).HasConversion(timestamptz);
            vehicle.Property(v => v.TransportPermitExpiry).HasConversion(timestamptz);
            modelBuilder.Entity<Driver>().Property(d => d.PermitExpiry).HasConversion(timestamptz);
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

    private static ICurrentTenantService Admin() => TestDbContextFactory.CreateMockTenantService(CompanyId).Object;

    private static ICurrentTenantService Restricted()
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(t => t.CompanyId).Returns(CompanyId);
        m.Setup(t => t.UserId).Returns(RestrictedUserId);
        m.Setup(t => t.UserRoles).Returns(new[] { "user" });
        m.Setup(t => t.IsAuthenticated).Returns(true);
        return m.Object;
    }

    // ── DEF-035 : jours calendaires quel que soit le chemin de saisie ──────

    [Theory]
    [InlineData("insurance")]
    [InlineData("technical_inspection")]
    [InlineData("tax")]
    [InlineData("registration")]
    [InlineData("transport_permit")]
    public async Task La_correction_d_echeance_stocke_minuit_comme_la_fiche_vehicule(string type)
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 44, CompanyId = CompanyId, Name = "QA-44" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await new UpdateDocumentExpiryCommandHandler(ctx, Admin())
            .Handle(new UpdateDocumentExpiryCommand(44, type, new DateTime(2026, 9, 20)), CancellationToken.None);

        ctx.ChangeTracker.Clear();
        var v = await ctx.Vehicles.AsNoTracking().SingleAsync();
        var stored = type switch
        {
            "insurance" => v.InsuranceExpiry,
            "technical_inspection" => v.TechnicalInspectionExpiry,
            "tax" => v.TaxExpiry,
            "registration" => v.RegistrationExpiry,
            _ => v.TransportPermitExpiry
        };
        stored.Should().Be(new DateTime(2026, 9, 20, 0, 0, 0), "même heure que PUT /api/vehicles et le renouvellement");
    }

    [Fact]
    public async Task Les_jours_restants_ne_dependent_pas_de_l_heure_stockee()
    {
        using var ctx = NpgsqlLegacy();
        var hours = new[] { (0, 0, 0), (12, 0, 0), (23, 59, 59) };
        for (var i = 0; i < hours.Length; i++)
        {
            var (h, m, s) = hours[i];
            ctx.Vehicles.Add(new Vehicle
            {
                Id = i + 1, CompanyId = CompanyId, Name = $"V{i + 1}",
                InsuranceExpiry = At(Today.AddDays(7), h, m, s),
                RegistrationExpiry = At(Today.AddDays(-12), h, m, s),
                // Échue hier : expirée, pas « bientôt » à 0 jour.
                TaxExpiry = At(Today.AddDays(-1), h, m, s)
            });
            ctx.Drivers.Add(new Driver
            {
                Id = i + 1, CompanyId = CompanyId, FirstName = "QA", LastName = $"Permis {i + 1}",
                PermitExpiry = At(Today.AddDays(12), h, m, s)
            });
        }
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var list = (await new GetExpiriesQueryHandler(ctx, Admin())
            .Handle(new GetExpiriesQuery(), CancellationToken.None)).Items;
        list.Where(e => e.DocumentType == "insurance").Select(e => e.DaysUntilExpiry).Should().Equal(7, 7, 7);
        list.Where(e => e.DocumentType == "registration").Select(e => e.DaysUntilExpiry).Should().Equal(-12, -12, -12);
        list.Where(e => e.DocumentType == "tax").Should().OnlyContain(e => e.DaysUntilExpiry == -1 && e.Status == "expired");
        list.Where(e => e.DocumentType == "driver_permit").Select(e => e.DaysUntilExpiry).Should().Equal(12, 12, 12);

        for (var id = 1; id <= hours.Length; id++)
        {
            var vehicle = await new GetVehicleExpiriesQueryHandler(ctx, Admin())
                .Handle(new GetVehicleExpiriesQuery(id), CancellationToken.None);
            vehicle.Single(e => e.DocumentType == "insurance").DaysUntilExpiry.Should().Be(7);
            vehicle.Single(e => e.DocumentType == "registration").DaysUntilExpiry.Should().Be(-12);
        }

        var alerts = await new GetExpiryAlertsQueryHandler(ctx, Admin())
            .Handle(new GetExpiryAlertsQuery(7), CancellationToken.None);
        alerts.Count(e => e.DocumentType == "insurance").Should().Be(3, "7 jours restants entrent dans un seuil de 7 jours");
        alerts.Where(e => e.DocumentType == "registration").Select(e => e.DaysUntilExpiry).Should().Equal(-12, -12, -12);

        var stats = await new GetExpiryStatsQueryHandler(ctx, Admin())
            .Handle(new GetExpiryStatsQuery(), CancellationToken.None);
        stats.ExpiredCount.Should().Be(6, "carte grise et vignette échues des trois véhicules");
        stats.ExpiringSoonCount.Should().Be(6, "assurances et permis des trois heures de saisie");
        stats.OkCount.Should().Be(0);
    }

    [Fact]
    public async Task La_date_renvoyee_est_le_jour_compte_a_minuit_UTC()
    {
        // Ligne écrite par l'ancienne correction d'échéance (23:59:59 UTC) :
        // un navigateur à UTC+1 affichait le lendemain à côté de « 7 jours ».
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle
        {
            Id = 1, CompanyId = CompanyId, Name = "QA-1", Plate = "QA-001",
            InsuranceExpiry = At(Today.AddDays(7), 23, 59, 59)
        });
        ctx.Drivers.Add(new Driver
        {
            Id = 1, CompanyId = CompanyId, FirstName = "QA", LastName = "Un",
            PermitExpiry = At(Today.AddDays(12), 23, 59, 59), AssignedVehicleId = 1
        });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var assurance = At(Today.AddDays(7), 0, 0, 0);
        var permis = At(Today.AddDays(12), 0, 0, 0);

        var list = (await new GetExpiriesQueryHandler(ctx, Admin())
            .Handle(new GetExpiriesQuery(), CancellationToken.None)).Items;
        list.Single(e => e.DocumentType == "insurance").ExpiryDate.Should().Be(assurance);
        list.Single(e => e.DocumentType == "driver_permit").ExpiryDate.Should().Be(permis);
        list.Single(e => e.DocumentType == "registration").ExpiryDate.Should().BeNull();

        (await new GetVehicleExpiriesQueryHandler(ctx, Admin())
                .Handle(new GetVehicleExpiriesQuery(1), CancellationToken.None))
            .Single(e => e.DocumentType == "insurance").ExpiryDate.Should().Be(assurance);

        var alerts = await new GetExpiryAlertsQueryHandler(ctx, Admin())
            .Handle(new GetExpiryAlertsQuery(30), CancellationToken.None);
        alerts.Single(e => e.DocumentType == "insurance").ExpiryDate.Should().Be(assurance);
        alerts.Single(e => e.DocumentType == "driver_permit").ExpiryDate.Should().Be(permis);
        alerts.Should().OnlyContain(e => e.ExpiryDate!.Value.Kind == DateTimeKind.Utc);
    }

    [FactMachineHorsUtc]
    public void Hors_UTC_le_jour_d_une_echeance_relue_en_heure_locale_reste_le_jour_UTC()
    {
        // Relecture Npgsql legacy : Kind=Local dans le fuseau de la machine. À
        // UTC+1, 23:59:59 UTC tombe le lendemain ; à UTC-X, minuit UTC tombe la veille.
        var tardive = new DateTimeOffset(2026, 9, 20, 23, 59, 59, TimeSpan.Zero).LocalDateTime;
        var minuit = new DateTimeOffset(2026, 9, 20, 0, 0, 0, TimeSpan.Zero).LocalDateTime;
        var jour = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);

        (tardive.Date != jour.Date || minuit.Date != jour.Date).Should().BeTrue(
            "précondition : hors UTC, la date locale d'au moins une des deux heures change de jour");
        ExpiryCalendar.Day(tardive).Should().Be(jour);
        ExpiryCalendar.Day(minuit).Should().Be(jour);
        ExpiryCalendar.DaysUntil(tardive, jour.AddDays(-7)).Should().Be(7);
        ExpiryCalendar.DaysUntil(minuit, jour.AddDays(-7)).Should().Be(7);
    }

    // ── DEF-033 : les alertes signalent les permis de conducteur ───────────

    private static async Task<TestGisDbContext> WithDriversAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, CompanyId = CompanyId, Name = "QA-1", Plate = "QA-001", InsuranceExpiry = Today.AddDays(200) },
            new Vehicle { Id = 2, CompanyId = CompanyId, Name = "QA-2", Plate = "QA-002", InsuranceExpiry = Today.AddDays(200) },
            new Vehicle { Id = 9, CompanyId = OtherCompanyId, Name = "Autre", InsuranceExpiry = Today.AddDays(200) });
        ctx.Drivers.AddRange(
            new Driver { Id = 1, CompanyId = CompanyId, FirstName = "QA", LastName = "Un", PermitType = "B",
                         PermitNumber = "P-1", PermitExpiry = Today.AddDays(12), AssignedVehicleId = 1 },
            new Driver { Id = 2, CompanyId = CompanyId, FirstName = "QA", LastName = "Deux", PermitExpiry = Today.AddDays(-3) },
            new Driver { Id = 3, CompanyId = CompanyId, FirstName = "QA", LastName = "Trois", PermitExpiry = Today.AddDays(45), AssignedVehicleId = 2 },
            new Driver { Id = 4, CompanyId = CompanyId, FirstName = "QA", LastName = "Sans permis" },
            new Driver { Id = 5, CompanyId = OtherCompanyId, FirstName = "Autre", LastName = "Société", PermitExpiry = Today.AddDays(1) });
        ctx.UserVehicles.Add(new UserVehicle { UserId = RestrictedUserId, VehicleId = 1 });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    [Fact]
    public async Task Les_alertes_signalent_un_permis_qui_expire_ou_expire()
    {
        using var ctx = await WithDriversAsync();

        var alerts = await new GetExpiryAlertsQueryHandler(ctx, Admin())
            .Handle(new GetExpiryAlertsQuery(30), CancellationToken.None);

        var permits = alerts.Where(a => a.DocumentType == "driver_permit").ToList();
        permits.Should().HaveCount(2, "le permis à 45 jours dépasse le seuil, l'autre société et le chauffeur sans permis sont exclus");

        var un = permits.Single(p => p.VehicleName == "QA Un");
        un.Status.Should().Be("expiring_soon");
        un.DaysUntilExpiry.Should().Be(12);
        un.VehicleId.Should().Be(1);
        un.VehiclePlate.Should().Be("Permis B");
        un.DocumentNumber.Should().Be("P-1");

        var deux = permits.Single(p => p.VehicleName == "QA Deux");
        deux.Status.Should().Be("expired");
        deux.DaysUntilExpiry.Should().Be(-3);

        alerts.Select(a => a.DaysUntilExpiry).Should().BeInAscendingOrder();

        var horizon60 = await new GetExpiryAlertsQueryHandler(ctx, Admin())
            .Handle(new GetExpiryAlertsQuery(60), CancellationToken.None);
        horizon60.Count(a => a.DocumentType == "driver_permit").Should().Be(3);
    }

    [Fact]
    public async Task Les_alertes_de_permis_concordent_avec_la_liste_des_echeances()
    {
        using var ctx = await WithDriversAsync();

        var list = (await new GetExpiriesQueryHandler(ctx, Admin())
            .Handle(new GetExpiriesQuery(DocumentType: "driver_permit"), CancellationToken.None)).Items;
        var alerts = await new GetExpiryAlertsQueryHandler(ctx, Admin())
            .Handle(new GetExpiryAlertsQuery(30), CancellationToken.None);

        alerts.Where(a => a.DocumentType == "driver_permit")
            .Select(a => (a.VehicleName, a.Status, a.DaysUntilExpiry))
            .Should().BeEquivalentTo(list
                .Where(e => e.Status is "expired" or "expiring_soon")
                .Select(e => (e.VehicleName, e.Status, e.DaysUntilExpiry)));
    }

    [Fact]
    public async Task Un_utilisateur_restreint_n_est_alerte_que_des_permis_de_ses_vehicules()
    {
        using var ctx = await WithDriversAsync();

        var alerts = await new GetExpiryAlertsQueryHandler(ctx, Restricted())
            .Handle(new GetExpiryAlertsQuery(60), CancellationToken.None);

        alerts.Where(a => a.DocumentType == "driver_permit").Select(a => a.VehicleName)
            .Should().Equal("QA Un");
    }

    // ── DEF-055 : non renseignées en fin de liste ───────────────────────────

    [Fact]
    public async Task Les_echeances_non_renseignees_viennent_apres_les_echeances_a_jour()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle
        {
            Id = 1, CompanyId = CompanyId, Name = "QA-1",
            InsuranceExpiry = Today.AddDays(90),
            TechnicalInspectionExpiry = Today.AddDays(17),
            TaxExpiry = Today.AddDays(-4)
            // Carte grise et autorisation de transport non renseignées.
        });
        ctx.Drivers.Add(new Driver { Id = 1, CompanyId = CompanyId, FirstName = "QA", LastName = "Un", PermitExpiry = Today.AddDays(400) });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var list = (await new GetExpiriesQueryHandler(ctx, Admin())
            .Handle(new GetExpiriesQuery(), CancellationToken.None)).Items;
        list.Select(e => e.Status).Should().Equal("expired", "expiring_soon", "ok", "ok", "unknown", "unknown");
        list.Select(e => e.DaysUntilExpiry).Take(4).Should().Equal(-4, 17, 90, 400);

        var vehicle = await new GetVehicleExpiriesQueryHandler(ctx, Admin())
            .Handle(new GetVehicleExpiriesQuery(1), CancellationToken.None);
        vehicle.Select(e => e.Status).Should().Equal("expired", "expiring_soon", "ok", "unknown", "unknown");
    }
}
