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

    // ── Relecture contradictoire des correctifs (21/09/2026) ─────────────────────

    [Fact]
    public async Task Un_je_pars_rejoue_ne_date_pas_le_debut_d_une_coupure_de_suivi_de_l_heure_declaree()
    {
        // R1c : « Je pars » touché il y a 30 min sans réseau, rejoué maintenant. La coupure de
        // suivi était datée de l'heure déclarée : le moniteur criait aussitôt « Suivi
        // interrompu depuis 30 min », pendant que les points du téléphone arrivaient.
        using var ctx = await ParcAsync();
        var (c, _) = Controleur(ctx, Chauffeur);
        var now = DateTime.UtcNow;
        var geste = now.AddMinutes(-30);

        await c.Depart(10, 101, new DriverEventRequest { ClientTime = geste, SentAt = now });

        ctx.ChangeTracker.Clear();
        var tour = await ctx.Tours.AsNoTracking().SingleAsync(t => t.Id == 10);
        tour.ActualStartTime.Should().BeCloseTo(geste, TimeSpan.FromSeconds(1), "le départ garde l'heure du geste");
        tour.TrackingSource.Should().Be(TrackingSourceSelector.None);
        tour.TrackingSourceSince.Should().BeCloseTo(now, TimeSpan.FromSeconds(5), "heure de réception par le serveur");
    }

    [Fact]
    public async Task Un_depart_anticipe_rejoue_garde_son_heure_et_le_premier_troncon()
    {
        // Wc18 : prévue à H, envoyée à H − 1 h ; « Je pars » à H − 30 min sans réseau, rejoué
        // maintenant. Borné par l'heure PRÉVUE, le départ était daté du rejeu : points du
        // premier tronçon rejetés, arrivées en file redatées.
        using var ctx = await ParcAsync();
        var (c, _) = Controleur(ctx, Chauffeur);
        var now = DateTime.UtcNow;
        var geste = Depart.AddMinutes(-30);

        var r = Corps<System.Text.Json.JsonElement>(await c.Depart(10, 101, new DriverEventRequest { ClientTime = geste, SentAt = now }));
        r.GetProperty("tourStatus").GetString().Should().Be("in_progress");

        // Arrivée à l'étape, en file elle aussi, 20 min après le départ.
        await c.Arrive(10, 102, new DriverEventRequest { ClientTime = geste.AddMinutes(20), SentAt = now });
        var positions = Corps<System.Text.Json.JsonElement>(await c.Positions(new PhonePositionsRequest
        {
            SentAt = now,
            Points = new List<PhonePoint> { new() { RecordedAt = geste.AddMinutes(5), Latitude = 36.7, Longitude = 10.3, AccuracyM = 10 } }
        }));

        ctx.ChangeTracker.Clear();
        var tour = await ctx.Tours.AsNoTracking().Include(t => t.Waypoints).SingleAsync(t => t.Id == 10);
        tour.ActualStartTime.Should().BeCloseTo(geste, TimeSpan.FromSeconds(1));
        tour.Waypoints.Single(w => w.Id == 101).DriverDepartedAt.Should().BeCloseTo(geste, TimeSpan.FromSeconds(1));
        tour.Waypoints.Single(w => w.Id == 102).DriverArrivedAt.Should().BeCloseTo(geste.AddMinutes(20), TimeSpan.FromSeconds(1),
            "l'arrivée en file n'est pas redatée du rejeu");
        tour.Waypoints.Single(w => w.Id == 103).EstimatedArrivalTime.Should().Be(Depart.AddMinutes(120),
            "un départ en avance ne décale pas les estimations comme un retard");
        positions.GetProperty("accepted").GetInt32().Should().Be(1, "les points du premier tronçon sont gardés");
    }

    [Fact]
    public async Task Une_horloge_en_retard_en_direct_ne_fait_pas_un_faux_rejeu()
    {
        // R10c : téléphone en retard de 4 min, chauffeur EN LIGNE à 20 m de l'étape. Sans
        // sentAt, le serveur tenait la déclaration pour rejouée et lisait la trame d'il y a
        // 4 min, quand le camion était encore à 2,5 km : faux avertissement aux gestionnaires.
        using var ctx = await ParcAsync(statut: "in_progress");
        var now = DateTime.UtcNow;
        var retard = TimeSpan.FromMinutes(-4);
        ctx.GpsPositions.Add(new GpsPosition { DeviceId = 900, RecordedAt = now + retard, Latitude = StopLat + 0.0225, Longitude = StopLon, IsValid = true });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var surPlace = new VehiclePositionCache { Latitude = StopLat + 0.0002, Longitude = StopLon, RecordedAt = now.AddSeconds(-10), IgnitionOn = true };
        var (c, _) = Controleur(ctx, Chauffeur, surPlace);

        var r = Corps<System.Text.Json.JsonElement>(await c.Arrive(10, 102, new DriverEventRequest
        {
            ClientTime = now + retard - TimeSpan.FromSeconds(1), SentAt = now + retard
        }));

        r.GetProperty("warning").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        ctx.ChangeTracker.Clear();
        var wp = await ctx.TourWaypoints.AsNoTracking().SingleAsync(w => w.Id == 102);
        wp.DriverDeclarationDistanceM.Should().BeLessThan(100, "position actuelle du boîtier : la déclaration est en direct");
        wp.DriverArrivedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(5), "heure du geste corrigée de l'horloge");
    }

    [Fact]
    public async Task Un_rejeu_borne_a_maintenant_lit_quand_meme_la_trame_a_l_heure_du_geste()
    {
        // R10c : l'heure retenue est bornée à « maintenant » (geste antérieur à la borne),
        // mais le camion était ailleurs au moment du geste : la trame est cherchée là.
        using var ctx = await ParcAsync(statut: "in_progress");
        var now = DateTime.UtcNow;
        var geste = Depart.AddMinutes(-10);
        ctx.GpsPositions.Add(new GpsPosition { DeviceId = 900, RecordedAt = geste.AddMinutes(1), Latitude = StopLat + 0.0002, Longitude = StopLon, IsValid = true });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var loin = new VehiclePositionCache { Latitude = StopLat + 0.135, Longitude = StopLon, RecordedAt = now.AddSeconds(-10), IgnitionOn = true };
        var (c, _) = Controleur(ctx, Chauffeur, loin);

        var r = Corps<System.Text.Json.JsonElement>(await c.Arrive(10, 102, new DriverEventRequest { ClientTime = geste, SentAt = now }));

        r.GetProperty("warning").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        ctx.ChangeTracker.Clear();
        (await ctx.TourWaypoints.AsNoTracking().SingleAsync(w => w.Id == 102)).DriverDeclarationDistanceM.Should().BeLessThan(100);
    }

    private static Tour TourneeSuivante(int id = 13) => new()
    {
        Id = id, CompanyId = CompanyId, Name = "Après-midi", VehicleId = Vehicule, DriverId = Fiche, Status = "in_progress",
        ScheduledStartTime = DateTime.UtcNow.AddMinutes(-10), ActualStartTime = DateTime.UtcNow.AddMinutes(-10),
        SentAt = DateTime.UtcNow.AddHours(-3)
    };

    [Fact]
    public async Task Le_telephone_garde_la_tournee_qu_il_suit_quand_une_autre_demarre_apres()
    {
        // R11c : le gestionnaire démarre la tournée suivante ; la plus récente captait la trace
        // de celle que le chauffeur fait encore, et son téléphone y basculait.
        using var ctx = await ParcAsync(statut: "in_progress");
        ctx.Tours.Add(TourneeSuivante());
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var (c, _) = Controleur(ctx, Chauffeur);
        var now = DateTime.UtcNow;
        PhonePositionsRequest Lot(int? suivie) => new()
        {
            SentAt = now, ActiveTourId = suivie,
            Points = new List<PhonePoint> { new() { RecordedAt = now.AddSeconds(-20), Latitude = 36.5, Longitude = 10.4, AccuracyM = 10 } }
        };

        var r = Corps<System.Text.Json.JsonElement>(await c.Positions(Lot(10)));
        r.GetProperty("activeTourId").GetInt32().Should().Be(10);
        ctx.ChangeTracker.Clear();
        (await ctx.DriverAppPositions.AsNoTracking().SingleAsync()).TourId.Should().Be(10);

        // Application plus ancienne (sans activeTourId) : la plus récemment partie, comme avant.
        Corps<System.Text.Json.JsonElement>(await c.Positions(Lot(null))).GetProperty("activeTourId").GetInt32().Should().Be(13);
    }

    [Theory]
    [InlineData(12)]   // la sienne, mais partie il y a plus de 12 h
    [InlineData(11)]   // celle d'un autre chauffeur
    [InlineData(999)]  // inconnue
    public async Task Une_tournee_suivie_hors_perimetre_retombe_sur_la_plus_recente_en_cours(int suivie)
    {
        using var ctx = await ParcAsync(statut: "in_progress");
        ctx.Tours.Add(TourneeDeLaVeille());
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var (c, _) = Controleur(ctx, Chauffeur);

        var r = Corps<System.Text.Json.JsonElement>(await c.Positions(new PhonePositionsRequest
        {
            SentAt = DateTime.UtcNow, ActiveTourId = suivie,
            Points = new List<PhonePoint> { new() { RecordedAt = DateTime.UtcNow.AddSeconds(-20), Latitude = 36.5, Longitude = 10.4, AccuracyM = 10 } }
        }));

        r.GetProperty("activeTourId").GetInt32().Should().Be(10);
        r.GetProperty("tracking").GetBoolean().Should().BeTrue();
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

    // ── Points après la clôture, lots renvoyés (relecture du 21/09/2026) ────────

    /// <summary>La tournée 10, partie à <see cref="Depart"/> et terminée à <paramref name="fin"/>.</summary>
    private static async Task<TestGisDbContext> TourneeTermineeAsync(DateTime fin)
    {
        var ctx = await ParcAsync(statut: "in_progress");
        var tour = await ctx.Tours.SingleAsync(t => t.Id == 10);
        tour.Status = "completed";
        tour.ActualEndTime = fin;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static PhonePositionsRequest Lot(DateTime sentAt, int? suivie, params PhonePoint[] points) => new()
    {
        SentAt = sentAt, ActiveTourId = suivie, Points = points.ToList()
    };

    private static PhonePoint Point(DateTime recordedAt) =>
        new() { RecordedAt = recordedAt, Latitude = DestLat, Longitude = DestLon, AccuracyM = 10 };

    [Fact]
    public async Task Les_derniers_points_d_une_tournee_terminee_sont_gardes_jusqu_a_sa_fin_plus_2_min()
    {
        // L'app vide sa file juste avant « Je suis arrivé » à destination, puis envoie un
        // dernier lot à l'arrêt du suivi : la tournée est déjà « completed ». Ces points
        // étaient tous refusés (accepted 0, activeTourId null) — la fin de la trace manquait.
        var now = DateTime.UtcNow;
        var fin = now.AddMinutes(-10);
        using var ctx = await TourneeTermineeAsync(fin);
        var (c, _) = Controleur(ctx, Chauffeur);

        var r = Corps<System.Text.Json.JsonElement>(await c.Positions(Lot(now, 10,
            Point(fin.AddSeconds(-30)),
            Point(fin.AddSeconds(90)),
            Point(fin.AddMinutes(3)),             // plus de 2 min après la fin : refusé
            Point(Depart.AddMinutes(-10)))));     // avant le départ − 5 min : refusé, comme en cours de tournée

        r.GetProperty("tracking").GetBoolean().Should().BeFalse("la tournée est finie : le téléphone arrête le suivi");
        r.GetProperty("activeTourId").GetInt32().Should().Be(10, "nommée, pour que le téléphone finisse de vider sa file");
        r.GetProperty("accepted").GetInt32().Should().Be(2);
        ctx.ChangeTracker.Clear();
        var points = await ctx.DriverAppPositions.AsNoTracking().ToListAsync();
        points.Should().HaveCount(2).And.OnlyContain(pt => pt.TourId == 10 && pt.UserId == Chauffeur && pt.DriverId == Fiche);
    }

    [Fact]
    public async Task Les_derniers_points_d_une_tournee_terminee_ne_partent_pas_sur_une_autre_en_cours()
    {
        // Même cas, mais une autre tournée du chauffeur est en cours (démarrée par le
        // gestionnaire) : la retombée sur « la plus récente en cours » y rattachait la fin
        // de la trace de la tournée terminée.
        var now = DateTime.UtcNow;
        using var ctx = await TourneeTermineeAsync(now.AddMinutes(-1));
        ctx.Tours.Add(TourneeSuivante());
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var (c, _) = Controleur(ctx, Chauffeur);

        var r = Corps<System.Text.Json.JsonElement>(await c.Positions(Lot(now, 10, Point(now.AddMinutes(-2)))));

        r.GetProperty("activeTourId").GetInt32().Should().Be(10);
        r.GetProperty("tracking").GetBoolean().Should().BeFalse();
        ctx.ChangeTracker.Clear();
        (await ctx.DriverAppPositions.AsNoTracking().SingleAsync()).TourId.Should().Be(10);
    }

    [Fact]
    public async Task La_tournee_terminee_d_un_autre_chauffeur_ne_recoit_aucun_point()
    {
        using var ctx = await ParcAsync();   // sa tournée 10 est planifiée : rien en cours pour lui
        var autre = await ctx.Tours.SingleAsync(t => t.Id == 11);
        (autre.Status, autre.ActualStartTime, autre.ActualEndTime) = ("completed", Depart, DateTime.UtcNow.AddMinutes(-1));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var (c, _) = Controleur(ctx, Chauffeur);
        var now = DateTime.UtcNow;

        var r = Corps<System.Text.Json.JsonElement>(await c.Positions(Lot(now, 11, Point(now.AddMinutes(-2)))));

        r.GetProperty("tracking").GetBoolean().Should().BeFalse();
        r.GetProperty("activeTourId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        r.GetProperty("accepted").GetInt32().Should().Be(0);
        (await ctx.DriverAppPositions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Un_lot_renvoye_apres_une_reponse_perdue_n_est_pas_enregistre_deux_fois()
    {
        using var ctx = await ParcAsync(statut: "in_progress");
        var (c, _) = Controleur(ctx, Chauffeur);
        var now = DateTime.UtcNow;
        var (t1, t2, t3) = (now.AddSeconds(-60), now.AddSeconds(-40), now.AddSeconds(-20));

        // Premier envoi : le même instant deux fois dans le lot.
        Corps<System.Text.Json.JsonElement>(await c.Positions(Lot(now, 10, Point(t1), Point(t2), Point(t2))))
            .GetProperty("accepted").GetInt32().Should().Be(2, "un instant n'est compté qu'une fois");
        ctx.ChangeTracker.Clear();

        // La réponse s'est perdue : l'application renvoie le lot tel quel.
        Corps<System.Text.Json.JsonElement>(await c.Positions(Lot(now, 10, Point(t1), Point(t2), Point(t2))))
            .GetProperty("accepted").GetInt32().Should().Be(0, "rien de nouveau");
        ctx.ChangeTracker.Clear();

        // Lot suivant, à cheval sur le précédent : seul le point nouveau compte.
        Corps<System.Text.Json.JsonElement>(await c.Positions(Lot(now, 10, Point(t2), Point(t3))))
            .GetProperty("accepted").GetInt32().Should().Be(1);
        ctx.ChangeTracker.Clear();

        (await ctx.DriverAppPositions.AsNoTracking().Select(x => x.RecordedAt).ToListAsync())
            .Should().BeEquivalentTo(new[] { t1, t2, t3 });
    }

    [Fact]
    public async Task Le_meme_instant_chez_deux_chauffeurs_fait_deux_points()
    {
        // L'unicité est PAR COMPTE (index user_id, recorded_at) : deux téléphones peuvent
        // mesurer au même instant.
        using var ctx = await ParcAsync(statut: "in_progress");
        var autre = await ctx.Tours.SingleAsync(t => t.Id == 11);
        (autre.Status, autre.ActualStartTime) = ("in_progress", Depart);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        var now = DateTime.UtcNow;
        var instant = now.AddSeconds(-30);

        var (c1, _) = Controleur(ctx, Chauffeur);
        Corps<System.Text.Json.JsonElement>(await c1.Positions(Lot(now, 10, Point(instant))))
            .GetProperty("accepted").GetInt32().Should().Be(1);
        ctx.ChangeTracker.Clear();
        var (c2, _) = Controleur(ctx, AutreChauffeur);
        Corps<System.Text.Json.JsonElement>(await c2.Positions(Lot(now, 11, Point(instant))))
            .GetProperty("accepted").GetInt32().Should().Be(1);

        ctx.ChangeTracker.Clear();
        (await ctx.DriverAppPositions.AsNoTracking().Select(x => x.UserId).ToListAsync())
            .Should().BeEquivalentTo(new[] { Chauffeur, AutreChauffeur });
    }
}
