using System.Security.Claims;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Hubs;
using GisAPI.Services;
using GisAPI.Services.Tours;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Tours;

/// <summary>
/// Ce que le téléphone d'un chauffeur peut faire, et rien de plus (lot 1, migration 051),
/// sur le VRAI <see cref="DriverAppController"/> : ses tournées envoyées seulement, « Je
/// pars » qui démarre, « Je suis arrivé » qui valide (et clôt à destination), positions
/// bornées à la tournée en cours, jamais dans gps_positions.
/// </summary>
public class DriverAppControllerTests
{
    private const int CompanyId = 7;
    private const int Chauffeur = 61;           // users.id, account_type = driver
    private const int Fiche = 31;               // drivers.id
    private const int AutreChauffeur = 62;
    private const int AutreFiche = 32;
    private const int Salarie = 63;
    private const int Vehicule = 5;
    private static readonly DateTime Depart = new(2026, 9, 21, 7, 0, 0, DateTimeKind.Utc);
    private const double OrigLat = 36.80, OrigLon = 10.18;
    private const double StopLat = 36.40, StopLon = 10.60;
    private const double DestLat = 35.82, DestLon = 10.63;

    private static ICurrentTenantService Tenant(int userId)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(new[] { "user" });
        m.Setup(x => x.IsAuthenticated).Returns(true);
        m.Setup(x => x.IsDriverAccount).Returns(true);
        return m.Object;
    }

    private static (DriverAppController Controller, Mock<INotificationService> Notifs) Controleur(
        TestGisDbContext ctx, int userId, VehiclePositionCache? positionBoitier = null)
    {
        var redis = new Mock<IRedisCacheService>();
        redis.Setup(r => r.GetPositionAsync(It.IsAny<string>())).ReturnsAsync(positionBoitier);
        var notifs = new Mock<INotificationService>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(Mock.Of<IClientProxy>());
        var hub = new Mock<IHubContext<GpsHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var controller = new DriverAppController(ctx, Tenant(userId), redis.Object, notifs.Object, hub.Object,
            NullLogger<DriverAppController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("companyId", CompanyId.ToString()),
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                    }, "test"))
                }
            }
        };
        return (controller, notifs);
    }

    /// <param name="envoyee">La tournée a-t-elle été envoyée au chauffeur ?</param>
    private static async Task<TestGisDbContext> ParcAsync(bool envoyee = true, string statut = "planned")
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Vehicles.Add(new Vehicle { Id = Vehicule, Name = "Camion 5", Plate = "100 TU 5", CompanyId = CompanyId, GpsDeviceId = 900 });
        ctx.GpsDevices.Add(new GpsDevice { Id = 900, DeviceUid = "IMEI900", CompanyId = CompanyId });
        ctx.Drivers.Add(new Driver { Id = Fiche, CompanyId = CompanyId, UserId = Chauffeur, FirstName = "Ali", LastName = "B", AssignedVehicleId = Vehicule });
        ctx.Drivers.Add(new Driver { Id = AutreFiche, CompanyId = CompanyId, UserId = AutreChauffeur, FirstName = "Sami", LastName = "C" });

        var tour = new Tour
        {
            Id = 10, CompanyId = CompanyId, Name = "Livraison Sousse", VehicleId = Vehicule, DriverId = Fiche,
            Status = statut, ScheduledStartTime = Depart, SentAt = envoyee ? Depart.AddHours(-1) : null, SentByUserId = envoyee ? 1 : null,
            ActualStartTime = statut == "in_progress" ? Depart : null, EstimatedDurationMinutes = 120, EstimatedDistanceKm = 140
        };
        ctx.Tours.Add(tour);
        ctx.TourWaypoints.AddRange(
            new TourWaypoint { Id = 101, TourId = 10, SequenceOrder = 0, Type = "origin", Name = "Dépôt", Latitude = OrigLat, Longitude = OrigLon, EstimatedArrivalTime = Depart, WaypointStatus = statut == "in_progress" ? "completed" : "pending", IsCompleted = statut == "in_progress" },
            new TourWaypoint { Id = 102, TourId = 10, SequenceOrder = 1, Type = "waypoint", Name = "Client A", Latitude = StopLat, Longitude = StopLon, EstimatedArrivalTime = Depart.AddMinutes(60), WaypointStatus = "pending" },
            new TourWaypoint { Id = 103, TourId = 10, SequenceOrder = 2, Type = "destination", Name = "Sousse", Latitude = DestLat, Longitude = DestLon, EstimatedArrivalTime = Depart.AddMinutes(120), WaypointStatus = "pending" });
        // Une tournée d'un AUTRE chauffeur, envoyée : jamais visible par le premier.
        ctx.Tours.Add(new Tour { Id = 11, CompanyId = CompanyId, Name = "Autre", VehicleId = Vehicule, DriverId = AutreFiche, Status = "planned", ScheduledStartTime = Depart, SentAt = Depart.AddHours(-1) });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static T Corps<T>(ActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        // camelCase comme la vraie réponse HTTP (ASP.NET) : les tests lisent les mêmes noms que l’app.
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        return System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(ok.Value, options), options)!;
    }

    // ── Périmètre ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_compte_sans_fiche_chauffeur_recoit_403()
    {
        using var ctx = await ParcAsync();
        var (c, _) = Controleur(ctx, Salarie);

        var r = await c.Tours();

        r.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Le_chauffeur_ne_voit_que_ses_tournees_envoyees()
    {
        using var ctx = await ParcAsync();
        var (c, _) = Controleur(ctx, Chauffeur);

        var liste = Corps<List<System.Text.Json.JsonElement>>(await c.Tours());
        liste.Should().HaveCount(1);
        liste[0].GetProperty("id").GetInt32().Should().Be(10);
        liste[0].GetProperty("vehiclePlate").GetString().Should().Be("100 TU 5");

        (await c.Tour(11)).Should().BeOfType<NotFoundResult>("tournée d'un autre chauffeur");
    }

    [Fact]
    public async Task Une_tournee_non_envoyee_reste_invisible_meme_si_elle_est_a_lui()
    {
        using var ctx = await ParcAsync(envoyee: false);
        var (c, _) = Controleur(ctx, Chauffeur);

        Corps<List<System.Text.Json.JsonElement>>(await c.Tours()).Should().BeEmpty();
        (await c.Tour(10)).Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task L_ouverture_est_enregistree_une_seule_fois()
    {
        using var ctx = await ParcAsync();
        var (c, _) = Controleur(ctx, Chauffeur);

        (await c.Opened(10)).Should().BeOfType<NoContentResult>();
        ctx.ChangeTracker.Clear();
        var premiere = (await ctx.Tours.AsNoTracking().SingleAsync(t => t.Id == 10)).OpenedAt;
        premiere.Should().NotBeNull();

        await c.Opened(10);
        ctx.ChangeTracker.Clear();
        (await ctx.Tours.AsNoTracking().SingleAsync(t => t.Id == 10)).OpenedAt.Should().Be(premiere, "la première ouverture fait foi");
    }

    // ── Je pars ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Je_pars_demarre_la_tournee_a_l_heure_declaree_et_previent_le_gestionnaire()
    {
        using var ctx = await ParcAsync();
        var (c, notifs) = Controleur(ctx, Chauffeur);
        var heure = DateTime.UtcNow.AddMinutes(-1);

        var r = Corps<System.Text.Json.JsonElement>(await c.Depart(10, 101, new DriverEventRequest { ClientTime = heure }));

        r.GetProperty("tourStatus").GetString().Should().Be("in_progress");
        r.GetProperty("tracking").GetBoolean().Should().BeTrue();
        r.GetProperty("mode").GetString().Should().Be("full", "aucune position de boîtier fraîche");
        ctx.ChangeTracker.Clear();
        var tour = await ctx.Tours.AsNoTracking().Include(t => t.Waypoints).SingleAsync(t => t.Id == 10);
        tour.ActualStartTime.Should().BeCloseTo(heure, TimeSpan.FromSeconds(1));
        var origine = tour.Waypoints.Single(w => w.Id == 101);
        (origine.IsCompleted, origine.ArrivalSource).Should().Be((true, DriverTourRules.SourceDriver));
        origine.DriverDepartedAt.Should().BeCloseTo(heure, TimeSpan.FromSeconds(1));
        notifs.Verify(n => n.CreateAndSendAsync(CompanyId, 1, "tour_departed", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "tour", 10, "/tournees/10", It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Once, "la personne qui a envoyé la tournée est prévenue");
    }

    [Fact]
    public async Task Je_pars_ailleurs_qu_au_depart_est_refuse_tant_que_la_tournee_est_planifiee()
    {
        using var ctx = await ParcAsync();
        var (c, _) = Controleur(ctx, Chauffeur);

        (await c.Depart(10, 102, null)).Should().BeOfType<BadRequestObjectResult>();
        (await c.Arrive(10, 102, null)).Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Je suis arrivé ─────────────────────────────────────────────────────────

    [Fact]
    public async Task L_arrivee_declaree_valide_l_etape_avec_sa_distance_et_un_avertissement_si_elle_est_loin()
    {
        using var ctx = await ParcAsync(statut: "in_progress");
        // Le boîtier dit : 12 km de l'étape (contact mis, trame fraîche) ; pas de téléphone.
        var boitier = new VehiclePositionCache { Latitude = StopLat + 0.11, Longitude = StopLon, RecordedAt = DateTime.UtcNow.AddSeconds(-20), IgnitionOn = true };
        var (c, notifs) = Controleur(ctx, Chauffeur, boitier);

        var r = Corps<System.Text.Json.JsonElement>(await c.Arrive(10, 102, new DriverEventRequest()));

        r.GetProperty("tourStatus").GetString().Should().Be("in_progress");
        r.GetProperty("warning").GetString().Should().Contain("km de l'étape", "une seule source loin : acceptée, mais dite");
        ctx.ChangeTracker.Clear();
        var wp = await ctx.TourWaypoints.AsNoTracking().SingleAsync(w => w.Id == 102);
        (wp.IsCompleted, wp.ArrivalSource).Should().Be((true, DriverTourRules.SourceDriver));
        wp.DriverDeclarationDistanceM.Should().BeGreaterThan(10_000);
        notifs.Verify(n => n.CreateAndSendAsync(CompanyId, 1, "tour_waypoint", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "tour", 10, "/tournees/10", It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task L_arrivee_est_refusee_quand_boitier_et_telephone_la_contredisent()
    {
        using var ctx = await ParcAsync(statut: "in_progress");
        var boitier = new VehiclePositionCache { Latitude = StopLat + 0.11, Longitude = StopLon, RecordedAt = DateTime.UtcNow.AddSeconds(-20), IgnitionOn = true };
        var (c, _) = Controleur(ctx, Chauffeur, boitier);

        var r = await c.Arrive(10, 102, new DriverEventRequest { Latitude = StopLat + 0.10, Longitude = StopLon, AccuracyM = 15 });

        var conflit = r.Should().BeOfType<ConflictObjectResult>().Subject;
        System.Text.Json.JsonSerializer.Serialize(conflit.Value).Should().Contain(DriverAppController.TooFarCode);
        ctx.ChangeTracker.Clear();
        (await ctx.TourWaypoints.AsNoTracking().SingleAsync(w => w.Id == 102)).IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Arriver_a_destination_avec_une_etape_oubliee_demande_confirmation_puis_clot_la_tournee()
    {
        using var ctx = await ParcAsync(statut: "in_progress");
        var (c, notifs) = Controleur(ctx, Chauffeur);

        var r1 = await c.Arrive(10, 103, new DriverEventRequest());
        System.Text.Json.JsonSerializer.Serialize(r1.Should().BeOfType<ConflictObjectResult>().Subject.Value)
            .Should().Contain(DriverAppController.PendingStopsCode).And.Contain("Client A");

        var r2 = Corps<System.Text.Json.JsonElement>(await c.Arrive(10, 103, new DriverEventRequest { ConfirmSkipPending = true }));
        r2.GetProperty("tourStatus").GetString().Should().Be("completed");
        r2.GetProperty("tracking").GetBoolean().Should().BeFalse("le téléphone arrête le suivi");

        ctx.ChangeTracker.Clear();
        var tour = await ctx.Tours.AsNoTracking().Include(t => t.Waypoints).SingleAsync(t => t.Id == 10);
        tour.ActualEndTime.Should().NotBeNull();
        tour.Waypoints.Single(w => w.Id == 102).WaypointStatus.Should().Be("skipped", "jamais atteinte, pas « completed »");
        tour.Waypoints.Single(w => w.Id == 103).ArrivalSource.Should().Be(DriverTourRules.SourceDriver);
        notifs.Verify(n => n.CreateAndSendAsync(CompanyId, 1, "tour_completed", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "tour", 10, "/tournees/10", It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Positions du téléphone ─────────────────────────────────────────────────

    [Fact]
    public async Task Les_positions_ne_sont_acceptees_que_pour_la_tournee_en_cours_et_dans_sa_fenetre()
    {
        using var ctx = await ParcAsync(statut: "in_progress");
        var (c, _) = Controleur(ctx, Chauffeur);
        var now = DateTime.UtcNow;

        var r = Corps<System.Text.Json.JsonElement>(await c.Positions(new PhonePositionsRequest
        {
            SentAt = now,
            BatteryLevel = 42,
            Points = new List<PhonePoint>
            {
                new() { RecordedAt = now.AddSeconds(-30), Latitude = 36.5, Longitude = 10.4, AccuracyM = 12 },
                new() { RecordedAt = now.AddHours(2), Latitude = 36.5, Longitude = 10.4 },          // futur : rejeté
                new() { RecordedAt = Depart.AddHours(-1), Latitude = 36.5, Longitude = 10.4 },     // avant la tournée : rejeté
                new() { RecordedAt = now.AddSeconds(-10), Latitude = 999, Longitude = 10.4 },       // absurde : rejeté
            }
        }));

        r.GetProperty("tracking").GetBoolean().Should().BeTrue();
        r.GetProperty("activeTourId").GetInt32().Should().Be(10);
        r.GetProperty("accepted").GetInt32().Should().Be(1);
        ctx.ChangeTracker.Clear();
        var pos = await ctx.DriverAppPositions.AsNoTracking().SingleAsync();
        (pos.TourId, pos.DriverId, pos.UserId, pos.CompanyId, pos.BatteryLevel).Should().Be((10, Fiche, Chauffeur, CompanyId, (short)42));
        (await ctx.GpsPositions.CountAsync()).Should().Be(0, "jamais dans les positions du véhicule");
    }

    [Fact]
    public async Task Sans_tournee_en_cours_le_telephone_est_prie_d_arreter()
    {
        using var ctx = await ParcAsync(statut: "planned");
        var (c, _) = Controleur(ctx, Chauffeur);

        var r = Corps<System.Text.Json.JsonElement>(await c.Positions(new PhonePositionsRequest
        {
            Points = new List<PhonePoint> { new() { RecordedAt = DateTime.UtcNow, Latitude = 36.5, Longitude = 10.4 } }
        }));

        r.GetProperty("tracking").GetBoolean().Should().BeFalse();
        (await ctx.DriverAppPositions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task L_horloge_du_telephone_est_corrigee_de_son_decalage()
    {
        using var ctx = await ParcAsync(statut: "in_progress");
        var (c, _) = Controleur(ctx, Chauffeur);
        var now = DateTime.UtcNow;
        var decalage = TimeSpan.FromMinutes(-5);   // le téléphone retarde de 5 min

        await c.Positions(new PhonePositionsRequest
        {
            SentAt = now + decalage,
            Points = new List<PhonePoint> { new() { RecordedAt = now + decalage - TimeSpan.FromSeconds(10), Latitude = 36.5, Longitude = 10.4 } }
        });

        ctx.ChangeTracker.Clear();
        var pos = await ctx.DriverAppPositions.AsNoTracking().SingleAsync();
        pos.RecordedAt.Should().BeCloseTo(now.AddSeconds(-10), TimeSpan.FromSeconds(2), "recordedAt + (serveur − sentAt)");
    }
}
