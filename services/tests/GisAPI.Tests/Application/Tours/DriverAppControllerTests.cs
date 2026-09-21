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
    private const int Admin = 1;                // administrateur de la société : dans l'audience du véhicule
    private const int Operateur = 2;            // opérateur NON affecté au véhicule
    private const int Vehicule = 5;
    // Relatif à maintenant : une date fixe (le 21/09/2026 07:00) faisait échouer les tests
    // « en cours » dès que l'horloge dépassait la fenêtre de suivi de 12 h.
    private static readonly DateTime Depart = DateTime.UtcNow.AddHours(-1);
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
        ctx.Roles.Add(new Role { Id = 1, Name = "Administrateur", SocieteId = CompanyId, IsCompanyAdmin = true });
        ctx.Roles.Add(new Role { Id = 2, Name = "Opérateur", SocieteId = CompanyId });
        var admin = TestDataBuilder.CreateUser(id: Admin, companyId: CompanyId, email: "admin@test.com");
        admin.RoleId = 1;
        var operateur = TestDataBuilder.CreateUser(id: Operateur, companyId: CompanyId, email: "op@test.com");
        operateur.RoleId = 2;
        ctx.Users.AddRange(admin, operateur);
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
        notifs.Verify(n => n.CreateAndSendAsync(CompanyId, Admin, "tour_departed", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "tour", 10, "/tournees/10", It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()),
            Times.Once, "l'administrateur voit le véhicule, il est prévenu");
    }

    [Theory]
    [InlineData("active")]      // retiré du véhicule (il n'y est pas affecté)
    [InlineData("inactive")]    // compte désactivé depuis l'envoi
    public async Task L_expediteur_hors_du_perimetre_du_vehicule_n_est_plus_prevenu(string statut)
    {
        // Relecture du 21/09/2026 (F9/F25) : tours."SentByUserId" était ajouté à l'audience
        // sans contrôle — un compte retiré du véhicule ou désactivé restait notifié.
        using var ctx = await ParcAsync();
        var tour = await ctx.Tours.SingleAsync(t => t.Id == 10);
        tour.SentByUserId = Operateur;
        (await ctx.Users.SingleAsync(u => u.Id == Operateur)).Status = statut;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var (c, notifs) = Controleur(ctx, Chauffeur);

        await c.Depart(10, 101, new DriverEventRequest());

        notifs.Verify(n => n.CreateAndSendAsync(CompanyId, Operateur, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        notifs.Verify(n => n.CreateAndSendAsync(CompanyId, Admin, "tour_departed", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "tour", 10, "/tournees/10", It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Once);
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

    // ── Relecture du 21/09/2026 ────────────────────────────────────────────────

    private static Tour TourneeDeLaVeille() => new()
    {
        Id = 12, CompanyId = CompanyId, Name = "Hier", VehicleId = Vehicule, DriverId = Fiche, Status = "in_progress",
        ScheduledStartTime = DateTime.UtcNow.AddHours(-20), ActualStartTime = DateTime.UtcNow.AddHours(-20),
        SentAt = DateTime.UtcNow.AddHours(-21)
    };

    [Fact]
    public async Task Une_tournee_de_la_veille_restee_en_cours_ne_capte_pas_les_positions_du_jour()
    {
        // F17 : la plus ANCIENNE tournée en cours était choisie ; hors de sa fenêtre, elle
        // refusait tous les points et répondait tracking:false — le suivi du jour s'arrêtait.
        using var ctx = await ParcAsync(statut: "in_progress");
        ctx.Tours.Add(TourneeDeLaVeille());
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var (c, _) = Controleur(ctx, Chauffeur);
        var now = DateTime.UtcNow;

        var r = Corps<System.Text.Json.JsonElement>(await c.Positions(new PhonePositionsRequest
        {
            SentAt = now,
            Points = new List<PhonePoint> { new() { RecordedAt = now.AddSeconds(-20), Latitude = 36.5, Longitude = 10.4, AccuracyM = 10 } }
        }));

        r.GetProperty("tracking").GetBoolean().Should().BeTrue("la tournée du jour est suivie");
        r.GetProperty("activeTourId").GetInt32().Should().Be(10);
        r.GetProperty("accepted").GetInt32().Should().Be(1);
        ctx.ChangeTracker.Clear();
        (await ctx.DriverAppPositions.AsNoTracking().SingleAsync()).TourId.Should().Be(10);
    }

    [Fact]
    public async Task Seule_une_tournee_partie_il_y_a_plus_de_12_h_le_telephone_est_prie_d_arreter()
    {
        using var ctx = await ParcAsync(statut: "planned");
        ctx.Tours.Add(TourneeDeLaVeille());
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var (c, _) = Controleur(ctx, Chauffeur);

        var r = Corps<System.Text.Json.JsonElement>(await c.Positions(new PhonePositionsRequest
        {
            Points = new List<PhonePoint> { new() { RecordedAt = DateTime.UtcNow, Latitude = 36.5, Longitude = 10.4 } }
        }));

        r.GetProperty("tracking").GetBoolean().Should().BeFalse();
        (await ctx.DriverAppPositions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Une_declaration_rejouee_ne_se_mesure_pas_a_la_position_actuelle_du_boitier()
    {
        // F18 : « Je suis arrivé » touché il y a 25 min sans réseau, à 20 m de l'étape ; le
        // camion est maintenant 15 km plus loin. Aucune trame du boîtier autour de l'heure
        // déclarée : seul le téléphone, capturé au moment du geste, témoigne.
        using var ctx = await ParcAsync(statut: "in_progress");
        var maintenant = new VehiclePositionCache { Latitude = StopLat + 0.135, Longitude = StopLon, RecordedAt = DateTime.UtcNow.AddSeconds(-10), IgnitionOn = true };
        var (c, _) = Controleur(ctx, Chauffeur, maintenant);
        var heure = DateTime.UtcNow.AddMinutes(-25);

        var r = Corps<System.Text.Json.JsonElement>(await c.Arrive(10, 102, new DriverEventRequest
        {
            ClientTime = heure, Latitude = StopLat + 0.0002, Longitude = StopLon, AccuracyM = 10
        }));

        r.GetProperty("warning").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "à 20 m de l'étape au moment du geste");
        ctx.ChangeTracker.Clear();
        var wp = await ctx.TourWaypoints.AsNoTracking().SingleAsync(w => w.Id == 102);
        wp.DriverDeclarationDistanceM.Should().BeLessThan(100);
        wp.DriverArrivedAt.Should().BeCloseTo(heure, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Une_declaration_rejouee_lit_la_trame_du_boitier_a_l_heure_declaree_y_compris_pour_le_refus()
    {
        // F18, même règle pour TOO_FAR : le boîtier est MAINTENANT sur l'étape (le camion y
        // est arrivé depuis), mais à l'heure déclarée sa trame et le téléphone étaient tous
        // deux à plus de 2 km — la déclaration est refusée.
        using var ctx = await ParcAsync(statut: "in_progress");
        var heure = DateTime.UtcNow.AddMinutes(-25);
        ctx.GpsPositions.Add(new GpsPosition { DeviceId = 900, RecordedAt = heure.AddMinutes(1), Latitude = StopLat + 0.027, Longitude = StopLon, IsValid = true });
        ctx.GpsPositions.Add(new GpsPosition { DeviceId = 900, RecordedAt = heure.AddMinutes(10), Latitude = StopLat, Longitude = StopLon, IsValid = true });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var surPlace = new VehiclePositionCache { Latitude = StopLat, Longitude = StopLon, RecordedAt = DateTime.UtcNow.AddSeconds(-10), IgnitionOn = true };
        var (c, _) = Controleur(ctx, Chauffeur, surPlace);

        var r = await c.Arrive(10, 102, new DriverEventRequest
        {
            ClientTime = heure, Latitude = StopLat + 0.0225, Longitude = StopLon, AccuracyM = 10
        });

        System.Text.Json.JsonSerializer.Serialize(r.Should().BeOfType<ConflictObjectResult>().Subject.Value)
            .Should().Contain(DriverAppController.TooFarCode);
    }

    [Fact]
    public async Task Une_declaration_rejouee_sans_trame_a_l_heure_declaree_n_est_pas_refusee_sur_la_position_actuelle()
    {
        // F18 : sans trame à ± 3 min, le boîtier ne témoigne pas ; le téléphone seul (loin)
        // ne suffit jamais à refuser — la déclaration passe, avec son avertissement.
        using var ctx = await ParcAsync(statut: "in_progress");
        var loin = new VehiclePositionCache { Latitude = StopLat + 0.027, Longitude = StopLon, RecordedAt = DateTime.UtcNow.AddSeconds(-10), IgnitionOn = true };
        var (c, _) = Controleur(ctx, Chauffeur, loin);

        var r = Corps<System.Text.Json.JsonElement>(await c.Arrive(10, 102, new DriverEventRequest
        {
            ClientTime = DateTime.UtcNow.AddMinutes(-25), Latitude = StopLat + 0.0225, Longitude = StopLon, AccuracyM = 10
        }));

        r.GetProperty("warning").GetString().Should().Contain("km de l'étape");
    }

    [Fact]
    public async Task Je_suis_arrive_a_une_destination_deja_validee_cloture_quand_meme_la_tournee()
    {
        // F19 : le gestionnaire avait coché l'étape et la destination depuis le détail (sans
        // clôturer). La déclaration à destination ne changeait rien à l'étape, et la clôture
        // n'était faite que dans ce cas : la tournée restait « en cours » indéfiniment.
        using var ctx = await ParcAsync(statut: "in_progress");
        foreach (var w in await ctx.TourWaypoints.Where(w => w.Id == 102 || w.Id == 103).ToListAsync())
        {
            TourPlanning.MarkReached(w, DateTime.UtcNow.AddMinutes(-5));
            w.ArrivalSource = DriverTourRules.SourceManager;
        }
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var (c, notifs) = Controleur(ctx, Chauffeur);

        var r = Corps<System.Text.Json.JsonElement>(await c.Arrive(10, 103, new DriverEventRequest()));

        r.GetProperty("tourStatus").GetString().Should().Be("completed");
        r.GetProperty("tracking").GetBoolean().Should().BeFalse("le téléphone arrête le suivi");
        ctx.ChangeTracker.Clear();
        var tour = await ctx.Tours.AsNoTracking().Include(t => t.Waypoints).SingleAsync(t => t.Id == 10);
        (tour.Status, tour.ActualEndTime.HasValue).Should().Be(("completed", true));
        tour.Waypoints.Single(w => w.Id == 103).ArrivalSource.Should().Be(DriverTourRules.SourceManager, "la validation du gestionnaire garde sa source");
        notifs.Verify(n => n.CreateAndSendAsync(CompanyId, Admin, "tour_completed", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), "tour", 10, "/tournees/10", It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), Times.Once);
        notifs.Verify(n => n.CreateAndSendAsync(CompanyId, Admin, "tour_waypoint", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>(),
            It.IsAny<CancellationToken>()), Times.Never, "l'étape n'a pas changé d'état");
    }

    [Fact]
    public async Task La_cloture_par_le_chauffeur_calcule_la_distance_reelle_comme_le_moniteur()
    {
        // F21 : seule la clôture automatique calculait distance et carburant réels.
        using var ctx = await ParcAsync(statut: "in_progress");
        // Trace du boîtier pendant la tournée : 0,1° de latitude ≈ 11,1 km.
        ctx.GpsPositions.AddRange(
            new GpsPosition { DeviceId = 900, RecordedAt = Depart.AddMinutes(5), Latitude = 36.80, Longitude = 10.18, IsValid = true },
            new GpsPosition { DeviceId = 900, RecordedAt = Depart.AddMinutes(15), Latitude = 36.85, Longitude = 10.18, IsValid = true },
            new GpsPosition { DeviceId = 900, RecordedAt = Depart.AddMinutes(25), Latitude = 36.90, Longitude = 10.18, IsValid = true });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var (c, _) = Controleur(ctx, Chauffeur);

        await c.Arrive(10, 103, new DriverEventRequest { ConfirmSkipPending = true });

        ctx.ChangeTracker.Clear();
        var tour = await ctx.Tours.AsNoTracking().SingleAsync(t => t.Id == 10);
        tour.Status.Should().Be("completed");
        tour.ActualDistanceKm.Should().BeApproximately(11.12m, 0.1m);
        tour.ActualFuelLiters.Should().BeApproximately(0.89m, 0.02m, "8 L/100 km, comme la clôture automatique");
    }
}
