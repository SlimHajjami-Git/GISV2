using System.Security.Claims;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyCostReport;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyFleetReport;
using GisAPI.Application.Features.Reports.Queries.GetOperatingCostReport;
using GisAPI.Application.Features.Reports.Queries.GetVehicleCostEvolution;
using GisAPI.Application.Features.Reports.Queries.GetVehicleCostRanking;
using GisAPI.Domain.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Middleware;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ReportsController = global::GisAPI.Controllers.ReportsController;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// Défauts mineurs des rapports relevés par la recette Calypso GPA du 16/09/2026
/// (DEF-047, 048, 049, 058, 059, 060, 061, 062).
///
/// Jeu commun, société 1, septembre 2026, parc SANS boîtier :
///   - véhicule 1 « Service 01 » : pleins 400 + 600, compteur 10 000 → 11 000
///     (1 000 km, 1,000/km) ;
///   - véhicule 2 « Logistique 01 » : pleins 3 × 1 000, compteur 20 000 → 25 000
///     (5 000 km, 0,600/km) ;
///   - véhicule 3 « Terrain 01 » : un entretien de 500, aucun kilomètre ;
///   - véhicule 4 : autre société.
/// </summary>
public class RecetteRapportsMineursTests
{
    private const int CompanyId = 1;
    private static readonly DateTime Debut = new(2026, 9, 1);
    private static readonly DateTime Fin = new(2026, 9, 30);

    private static DateTime Sept(int day) => new(2026, 9, day, 9, 0, 0, DateTimeKind.Utc);

    private static async Task SeedAsync(TestGisDbContext ctx)
    {
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Service 01", Plate = "GA-214-RK", CompanyId = CompanyId },
            new Vehicle { Id = 2, Name = "Logistique 01", Plate = "GH-619-XC", CompanyId = CompanyId },
            new Vehicle { Id = 3, Name = "Terrain 01", Plate = "GG-852-BD", CompanyId = CompanyId },
            new Vehicle { Id = 4, Name = "Etranger", CompanyId = 2 });

        ctx.FuelEntries.AddRange(
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Sept(2), Volume = 40, TotalAmount = 400, OdometerKm = 10_000 },
            new FuelEntry { VehicleId = 1, CompanyId = CompanyId, InvoiceDate = Sept(20), Volume = 60, TotalAmount = 600, OdometerKm = 11_000 },
            new FuelEntry { VehicleId = 2, CompanyId = CompanyId, InvoiceDate = Sept(3), Volume = 100, TotalAmount = 1_000, OdometerKm = 20_000 },
            new FuelEntry { VehicleId = 2, CompanyId = CompanyId, InvoiceDate = Sept(15), Volume = 100, TotalAmount = 1_000, OdometerKm = 22_500 },
            new FuelEntry { VehicleId = 2, CompanyId = CompanyId, InvoiceDate = Sept(28), Volume = 100, TotalAmount = 1_000, OdometerKm = 25_000 });

        ctx.VehicleCosts.Add(new VehicleCost { VehicleId = 3, CompanyId = CompanyId, Type = "maintenance", Amount = 500, Date = Sept(10) });

        await ctx.SaveChangesAsync();
    }

    private static ICurrentTenantService Admin() => TestDbContextFactory.CreateMockTenantService(CompanyId).Object;

    private static ICurrentTenantService Restreint(int userId)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(new[] { "user" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    // ───────────────── DEF-047 : dépense « repair » dans Réparations ─────────────────

    [Fact]
    public async Task DEF047_une_depense_repair_est_une_reparation_dans_les_couts_mensuels_comme_dans_le_cout_d_exploitation()
    {
        using var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 41, Name = "GK-128-ZF", Plate = "GK-128-ZF", CompanyId = CompanyId });
        ctx.VehicleCosts.AddRange(
            new VehicleCost { VehicleId = 41, CompanyId = CompanyId, Type = "repair", Amount = 300m, Date = Sept(12) },
            new VehicleCost { VehicleId = 41, CompanyId = CompanyId, Type = "insurance", Amount = 625m, Date = Sept(5) });
        await ctx.SaveChangesAsync();

        var mensuel = await new GetMonthlyCostReportQueryHandler(ctx, Admin())
            .Handle(new GetMonthlyCostReportQuery(2026, 9), CancellationToken.None);
        var exploitation = await new GetOperatingCostReportQueryHandler(ctx, Admin())
            .Handle(new GetOperatingCostReportQuery(Debut, new DateTime(2026, 9, 13)), CancellationToken.None);

        mensuel.TotalRepairCostDzd.Should().Be(300m);
        var ligne = mensuel.Vehicles.Single(v => v.VehicleId == 41);
        ligne.RepairCostDzd.Should().Be(300m);
        ligne.OtherCostDzd.Should().Be(625m, "seule l'assurance reste en « Autres »");

        (mensuel.TotalRepairCostDzd, mensuel.TotalOtherCostDzd)
            .Should().Be((exploitation.TotalRepairCost, exploitation.TotalOtherCost));
    }

    // ───────────────── DEF-048 : mois hors bornes → 400 ─────────────────

    [Theory]
    [InlineData(2026, 13, "Mois invalide")]
    [InlineData(2026, 0, "Mois invalide")]
    [InlineData(2026, -1, "Mois invalide")]
    [InlineData(0, 9, "Année invalide")]
    [InlineData(9999, 12, "Année invalide")]
    public async Task DEF048_les_rapports_mensuels_refusent_un_mois_ou_une_annee_hors_bornes_en_erreur_metier(int year, int month, string debutMessage)
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var flotte = () => new GetMonthlyFleetReportQueryHandler(ctx, Admin())
            .Handle(new GetMonthlyFleetReportQuery(year, month), CancellationToken.None);
        var couts = () => new GetMonthlyCostReportQueryHandler(ctx, Admin())
            .Handle(new GetMonthlyCostReportQuery(year, month), CancellationToken.None);

        // DomainException exactement : ExceptionHandlingMiddleware la rend en 400,
        // alors que l'ArgumentOutOfRangeException de new DateTime partait en 500.
        (await flotte.Should().ThrowExactlyAsync<DomainException>()).WithMessage(debutMessage + "*");
        (await couts.Should().ThrowExactlyAsync<DomainException>()).WithMessage(debutMessage + "*");
    }

    [Fact]
    public async Task DEF048_le_refus_sort_en_400_avec_le_message_francais()
    {
        var (status, message) = await RenduParLeMiddlewareAsync(() =>
        {
            ReportRequestRules.EnsureValidMonth(2026, 13);
            return Task.CompletedTask;
        });

        status.Should().Be(StatusCodes.Status400BadRequest);
        message.Should().Be("Mois invalide : il doit être compris entre 1 et 12.");
    }

    // ───────────────── DEF-049 : devise de la société ─────────────────

    /// <summary>Contexte de test qui mappe les réglages de société en JSON, comme la production.</summary>
    private sealed class ContexteAvecReglagesSociete : TestGisDbContext
    {
        public ContexteAvecReglagesSociete(DbContextOptions<TestGisDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Societe>().OwnsOne(s => s.Settings, b => b.ToJson("settings"));
        }

        public static ContexteAvecReglagesSociete Create()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            // Comme TestDbContextFactory.Create : la logique métier, pas les contraintes.
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_keys = OFF;";
                cmd.ExecuteNonQuery();
            }
            var options = new DbContextOptionsBuilder<TestGisDbContext>().UseSqlite(connection).Options;
            var ctx = new ContexteAvecReglagesSociete(options);
            ctx.Database.EnsureCreated();
            return ctx;
        }
    }

    [Fact]
    public async Task DEF049_le_rapport_mensuel_flotte_libelle_ses_couts_dans_la_devise_de_la_societe()
    {
        using var ctx = ContexteAvecReglagesSociete.Create();
        var societe = TestDataBuilder.CreateSociete(CompanyId, subscriptionTypeId: null);
        societe.Settings = new SocieteSettings { Currency = "EUR" };
        ctx.Societes.Add(societe);
        await SeedAsync(ctx);

        var rapport = await new GetMonthlyFleetReportQueryHandler(ctx, Admin())
            .Handle(new GetMonthlyFleetReportQuery(2026, 9), CancellationToken.None);

        rapport.KeyPerformanceIndicators.Single(k => k.Name == "Coût par kilomètre").Unit.Should().Be("EUR/km");
        rapport.Charts.MaintenanceCostByType.Unit.Should().Be("EUR");
    }

    [Fact]
    public async Task DEF049_sans_devise_de_societe_le_rapport_prend_celle_du_deploiement()
    {
        using var ctx = ContexteAvecReglagesSociete.Create();
        var societe = TestDataBuilder.CreateSociete(CompanyId, subscriptionTypeId: null);
        societe.Settings = null;
        ctx.Societes.Add(societe);
        await SeedAsync(ctx);

        var rapport = await new GetMonthlyFleetReportQueryHandler(ctx, Admin())
            .Handle(new GetMonthlyFleetReportQuery(2026, 9), CancellationToken.None);

        rapport.KeyPerformanceIndicators.Single(k => k.Name == "Coût par kilomètre").Unit
            .Should().Be($"{AppCurrency.Default}/km");
        ReportCurrency.Resolve("  ").Should().Be(AppCurrency.Default);
        ReportCurrency.Resolve(" EUR ").Should().Be("EUR");
    }

    // ───────────────── DEF-058 : taille du parc indépendante du filtre véhicule ─────────────────

    [Fact]
    public async Task DEF058_filtre_sur_un_vehicule_le_cout_d_exploitation_garde_la_taille_du_parc()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var rapport = await new GetOperatingCostReportQueryHandler(ctx, Admin())
            .Handle(new GetOperatingCostReportQuery(Debut, Fin, VehicleId: 1), CancellationToken.None);

        rapport.FleetSize.Should().Be(3, "le parc de la société, l'autre société exclue");
        rapport.VehicleCount.Should().Be(1);
        rapport.Vehicles.Should().ContainSingle(v => v.VehicleId == 1);
        rapport.TotalCost.Should().Be(1_000m, "les totaux restent ceux du véhicule filtré");
    }

    [Fact]
    public async Task DEF058_la_taille_du_parc_reste_bornee_a_la_portee_de_l_appelant()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        ctx.UserVehicles.AddRange(
            new UserVehicle { UserId = 42, VehicleId = 1 },
            new UserVehicle { UserId = 42, VehicleId = 2 });
        await ctx.SaveChangesAsync();

        var rapport = await new GetOperatingCostReportQueryHandler(ctx, Restreint(42))
            .Handle(new GetOperatingCostReportQuery(Debut, Fin, VehicleId: 1), CancellationToken.None);

        rapport.FleetSize.Should().Be(2, "un employé ne voit que ses deux véhicules");
        rapport.VehicleCount.Should().Be(1);
    }

    // ───────────────── DEF-059 : « les plus coûteux » = coût total ─────────────────

    [Fact]
    public async Task DEF059_le_classement_des_plus_couteux_suit_le_cout_total_et_garde_le_cout_au_km()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var classement = await new GetVehicleCostRankingQueryHandler(ctx, Admin())
            .Handle(new GetVehicleCostRankingQuery(Debut, Fin, Top: 10), CancellationToken.None);

        classement.Vehicles.Select(v => v.VehicleId).Should().Equal(2, 1, 3);
        classement.Vehicles.Select(v => v.Rank).Should().Equal(1, 2, 3);
        classement.Vehicles.Select(v => v.TotalCost).Should().BeInDescendingOrder();
        classement.Vehicles.Select(v => v.CostPerKm).Should().Equal(0.6m, 1m, null);

        // Un top 1 garde le véhicule le plus cher, pas le plus cher au km.
        var top1 = await new GetVehicleCostRankingQueryHandler(ctx, Admin())
            .Handle(new GetVehicleCostRankingQuery(Debut, Fin, Top: 1), CancellationToken.None);
        top1.Vehicles.Should().ContainSingle().Which.VehicleId.Should().Be(2);
        top1.TotalCost.Should().Be(4_500m, "les KPI portent toujours sur tout le parc");
    }

    [Fact]
    public async Task DEF059_le_cout_d_exploitation_reel_reste_classe_au_cout_au_km()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var rapport = await new GetOperatingCostReportQueryHandler(ctx, Admin())
            .Handle(new GetOperatingCostReportQuery(Debut, Fin), CancellationToken.None);

        rapport.Vehicles.Select(v => v.VehicleId).Should().Equal(1, 2, 3);
    }

    // ───────────────── DEF-060 : 404 en français ─────────────────

    [Fact]
    public async Task DEF060_l_evolution_des_couts_d_un_vehicule_inconnu_repond_404_en_francais()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var (status, message) = await RenduParLeMiddlewareAsync(() => new GetVehicleCostEvolutionQueryHandler(ctx, Admin())
            .Handle(new GetVehicleCostEvolutionQuery(4, Debut, Fin), CancellationToken.None));

        status.Should().Be(StatusCodes.Status404NotFound);
        message.Should().Be("Véhicule introuvable.");
    }

    /// <summary>Vrai GisDbContext en mémoire, pour les routes qui vérifient le véhicule dans le contrôleur.</summary>
    private sealed class ContexteControleurEnMemoire : GisDbContext
    {
        public ContexteControleurEnMemoire()
            : base(new DbContextOptionsBuilder<GisDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
                    .Options,
                TestDbContextFactory.CreateMockTenantService(CompanyId).Object) { }

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

    [Fact]
    public async Task DEF060_les_routes_qui_verifient_le_vehicule_dans_le_controleur_repondent_404_en_francais()
    {
        // Chemin exact de la fiche : GET costs/evolution/20, véhicule 20 d'une autre société,
        // refusé par le contrôleur avant tout appel au médiateur.
        using var ctx = new ContexteControleurEnMemoire();
        ctx.Vehicles.Add(new Vehicle { Id = 20, Name = "Etranger", Plate = "GZ-000-ZZ", CompanyId = 2 });
        await ctx.SaveChangesAsync();

        // Tenant administrateur : le refus doit venir du cloisonnement SOCIÉTÉ,
        // que la portée utilisateur (VehicleScope) ne porte pas.
        var controller = new ReportsController(ctx, new Mock<IMediator>(MockBehavior.Strict).Object, null!,
            TestDbContextFactory.CreateMockTenantService(CompanyId).Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("companyId", CompanyId.ToString()) }))
                }
            }
        };

        var reponses = new ActionResult?[]
        {
            (await controller.GetVehicleCostEvolution(20)).Result,
            (await controller.GetDailyReport(20)).Result,
            (await controller.GetMileageReport(20)).Result,
            (await controller.GetStopsReport(20)).Result,
            (await controller.GetTripsReport(20)).Result,
            (await controller.GetMileagePeriodReport(20)).Result
        };

        reponses.Should().AllSatisfy(r =>
        {
            r.Should().BeOfType<NotFoundObjectResult>()
                .Which.Value.Should().BeEquivalentTo(new { message = "Véhicule introuvable." });
        });
    }

    [Fact]
    public void DEF060_le_format_general_de_NotFoundException_ne_change_pas()
    {
        new NotFoundException("Vehicle", 4).Message.Should().Be("Entity \"Vehicle\" (4) was not found.");
        new NotFoundException("Véhicule introuvable.").Message.Should().Be("Véhicule introuvable.");
    }

    // ───────────────── DEF-061 : période inversée → 400 ─────────────────

    [Fact]
    public async Task DEF061_toutes_les_routes_de_rapport_a_periode_refusent_une_periode_inversee()
    {
        // Strict : un appel au médiateur (rapport calculé) ferait échouer le test.
        var mediator = new Mock<IMediator>(MockBehavior.Strict);
        // Contexte et service de mail nuls : le refus doit précéder tout accès.
        var controller = new ReportsController(null!, mediator.Object, null!, null!);
        var debut = new DateTime(2026, 8, 31);
        var fin = new DateTime(2026, 8, 1);

        var reponses = new ActionResult?[]
        {
            (await controller.GetOperatingCostReport(debut, fin)).Result,
            (await controller.GetVehicleCostRanking(debut, fin)).Result,
            (await controller.GetRepairFrequencyReport(debut, fin)).Result,
            (await controller.GetVehicleCostEvolution(39, debut, fin)).Result,
            (await controller.GetMileageReport(39, debut, fin)).Result,
            (await controller.GetMileageReports(debut, fin)).Result,
            (await controller.GetStopsReport(39, debut, fin)).Result,
            (await controller.GetStopsReportAll(debut, fin)).Result,
            (await controller.GetTripsReport(39, debut, fin)).Result,
            (await controller.GetTripsReportAll(debut, fin)).Result,
            (await controller.GetMileagePeriodReport(39, debut, fin, "day")).Result
        };

        reponses.Should().AllSatisfy(r =>
        {
            r.Should().BeOfType<BadRequestObjectResult>()
                .Which.Value.Should().BeEquivalentTo(new { message = "La date de début doit précéder la date de fin." });
        });
    }

    [Fact]
    public async Task DEF061_une_periode_d_un_seul_jour_reste_acceptee()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetOperatingCostReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperatingCostReportDto)null!);
        var controller = new ReportsController(null!, mediator.Object, null!, null!);

        var reponse = await controller.GetOperatingCostReport(new DateTime(2026, 8, 1, 18, 0, 0), new DateTime(2026, 8, 1, 6, 0, 0));

        reponse.Result.Should().BeOfType<OkObjectResult>("début et fin le même jour, heures ignorées");
    }

    // ───────────────── DEF-062 : note de distance sans boîtier ─────────────────

    [Fact]
    public async Task DEF062_parc_sans_boitier_la_note_ne_parle_que_des_releves_compteur()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var rapport = await new GetOperatingCostReportQueryHandler(ctx, Admin())
            .Handle(new GetOperatingCostReportQuery(Debut, Fin), CancellationToken.None);

        rapport.DistanceNote.Should().Be(OperatingCostReportBuilder.DistanceNoteWithoutGps);
        rapport.DistanceNote.Should().NotContain("GPS");
    }

    [Fact]
    public async Task DEF062_parc_equipe_la_note_mentionne_les_trajets_GPS_mais_seulement_dans_la_portee()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        ctx.GpsDevices.Add(new GpsDevice { Id = 7, DeviceUid = "DEV-7", CompanyId = CompanyId });
        ctx.Vehicles.Add(new Vehicle { Id = 5, Name = "Camion", Plate = "1 TU 1", CompanyId = CompanyId, GpsDeviceId = 7 });
        ctx.UserVehicles.Add(new UserVehicle { UserId = 42, VehicleId = 1 });
        await ctx.SaveChangesAsync();

        var admin = await new GetOperatingCostReportQueryHandler(ctx, Admin())
            .Handle(new GetOperatingCostReportQuery(Debut, Fin), CancellationToken.None);
        admin.DistanceNote.Should().Be(OperatingCostReportBuilder.DistanceNoteWithGps);

        // L'employé ne voit que le véhicule 1, sans boîtier : pas de trajets GPS pour lui.
        var employe = await new GetOperatingCostReportQueryHandler(ctx, Restreint(42))
            .Handle(new GetOperatingCostReportQuery(Debut, Fin), CancellationToken.None);
        employe.DistanceNote.Should().Be(OperatingCostReportBuilder.DistanceNoteWithoutGps);
    }

    // ───────────────── Outils ─────────────────

    /// <summary>Statut et message que l'API renverrait pour cette action, via le middleware réel.</summary>
    private static async Task<(int Status, string? Message)> RenduParLeMiddlewareAsync(Func<Task> action)
    {
        var middleware = new ExceptionHandlingMiddleware(_ => action(), NullLogger<ExceptionHandlingMiddleware>.Instance);
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(http);

        http.Response.Body.Position = 0;
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Response.Body);
        return (http.Response.StatusCode, doc.RootElement.GetProperty("message").GetString());
    }
}
