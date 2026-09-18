using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Controllers;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Tours;

/// <summary>
/// Lot 0 « tournée envoyée au chauffeur » (18/09/2026), testé sur le VRAI
/// <see cref="ToursController"/> — il dépend désormais d'IGisDbContext, que
/// <see cref="TestGisDbContext"/> implémente (SQLite en mémoire).
///
/// Constats couverts :
/// - la modification acceptait le véhicule (et les zones) d'une autre société,
///   alors que le moniteur tourne hors filtre société : oracle de position ;
/// - aucune route /api/tours n'appliquait la portée véhicules d'un utilisateur
///   restreint ;
/// - le chauffeur n'était ni validé ni retirable ;
/// - la modification effaçait heures prévues, zones et marges des étapes ;
/// - les chemins manuels ne tenaient pas WaypointStatus ;
/// - le démarrage manuel en retard ne décalait pas les estimations.
///
/// Relecture du même jour : filtre société de EndPause non couvert, position
/// d'un véhicule transféré à une autre société, étape sans échéance qui
/// verrouillait la destination, chauffeur inchangé mais supprimé qui bloquait
/// la modification, étape insérée ignorée par les heures suivantes.
/// </summary>
public class ToursLot0Tests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;
    private const int AdminUserId = 1;
    private const int RestrictedUserId = 51;
    private const int OrphanUserId = 52;

    // Tournée 1 : véhicule 1 (affecté à l'utilisateur restreint), 3 étapes.
    private static readonly DateTime Start = new(2030, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private const double OriginLat = 36.80, OriginLon = 10.18;
    private const double StopLat = 36.40, StopLon = 10.60;
    private const double DestLat = 35.82, DestLon = 10.63;

    private static ICurrentTenantService Tenant(int userId, params string[] roles)
    {
        var m = new Mock<ICurrentTenantService>();
        m.Setup(x => x.CompanyId).Returns(CompanyId);
        m.Setup(x => x.UserId).Returns(userId);
        m.Setup(x => x.UserRoles).Returns(roles);
        m.Setup(x => x.IsAuthenticated).Returns(true);
        return m.Object;
    }

    private static ICurrentTenantService Admin() => Tenant(AdminUserId, "company_admin");
    private static ICurrentTenantService Restricted() => Tenant(RestrictedUserId, "user");
    private static ICurrentTenantService Orphan() => Tenant(OrphanUserId, "user");

    /// <summary>Le vrai contrôleur, avec le claim companyId qu'il lit lui-même.
    /// <paramref name="route"/> null = routage indisponible.</summary>
    private static ToursController Controller(TestGisDbContext ctx, ICurrentTenantService tenant,
        ValhallaRouteResult? route = null, IRedisCacheService? redis = null)
    {
        var valhalla = new Mock<IValhallaService>();
        valhalla.Setup(v => v.GetRouteFromWaypointsAsync(It.IsAny<List<ValhallaPoint>>()))
            .ReturnsAsync(route);

        var controller = new ToursController(
            ctx, tenant, valhalla.Object, redis ?? new Mock<IRedisCacheService>().Object,
            NullLogger<ToursController>.Instance);

        var identity = new ClaimsIdentity(new[]
        {
            new Claim("companyId", (tenant.CompanyId ?? 0).ToString()),
            new Claim(ClaimTypes.NameIdentifier, (tenant.UserId ?? 0).ToString())
        }, "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    private static async Task SeedAsync(TestGisDbContext ctx)
    {
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Opel", Plate = "111 TU 1", CompanyId = CompanyId },
            new Vehicle { Id = 2, Name = "Camion", Plate = "222 TU 2", CompanyId = CompanyId },
            new Vehicle { Id = 9, Name = "Etranger", Plate = "999 TU 9", CompanyId = OtherCompanyId });

        ctx.Drivers.AddRange(
            new Driver { Id = 1, FirstName = "Ali", LastName = "Ben Salah", CompanyId = CompanyId, AssignedVehicleId = 1, Status = "active" },
            new Driver { Id = 2, FirstName = "Sami", LastName = "Inactif", CompanyId = CompanyId, Status = "inactive" },
            new Driver { Id = 3, FirstName = "Karim", LastName = "Actif", CompanyId = CompanyId, Status = "active" },
            new Driver { Id = 9, FirstName = "Autre", LastName = "Societe", CompanyId = OtherCompanyId, Status = "active" });

        ctx.Geofences.AddRange(
            new Geofence { Id = 1, Name = "Dépôt Sousse", CompanyId = CompanyId },
            new Geofence { Id = 9, Name = "Zone étrangère", CompanyId = OtherCompanyId });

        ctx.UserVehicles.Add(new UserVehicle { UserId = RestrictedUserId, VehicleId = 1 });

        ctx.Tours.AddRange(
            new Tour
            {
                Id = 1, Name = "Tunis - Sousse", CompanyId = CompanyId, VehicleId = 1, DriverId = 1,
                Status = "planned", ScheduledStartTime = Start, ScheduledEndTime = Start.AddMinutes(70),
                EstimatedDurationMinutes = 60, TotalPauseMinutes = 10,
                Waypoints = new List<TourWaypoint>
                {
                    new() { Id = 11, SequenceOrder = 0, Type = "origin", Name = "Tunis", Latitude = OriginLat, Longitude = OriginLon,
                            EstimatedArrivalTime = Start, DeadlineMarginMinutes = 60 },
                    new() { Id = 12, SequenceOrder = 1, Type = "waypoint", Name = "Arrêt", Latitude = StopLat, Longitude = StopLon,
                            GeofenceId = 1, EstimatedLegMinutes = 40, DeadlineMarginMinutes = 30, PlannedPauseMinutes = 10,
                            EstimatedArrivalTime = Start.AddMinutes(40) },
                    new() { Id = 13, SequenceOrder = 2, Type = "destination", Name = "Sousse", Latitude = DestLat, Longitude = DestLon,
                            EstimatedLegMinutes = 20, DeadlineMarginMinutes = 45, EstimatedArrivalTime = Start.AddMinutes(70) }
                }
            },
            new Tour
            {
                Id = 2, Name = "Camion hors portée", CompanyId = CompanyId, VehicleId = 2,
                Status = "planned", ScheduledStartTime = Start,
                Waypoints = new List<TourWaypoint>
                {
                    new() { Id = 21, SequenceOrder = 0, Type = "origin", Latitude = OriginLat, Longitude = OriginLon, EstimatedArrivalTime = Start },
                    new() { Id = 22, SequenceOrder = 1, Type = "destination", Latitude = DestLat, Longitude = DestLon, EstimatedArrivalTime = Start.AddMinutes(60) }
                }
            },
            new Tour
            {
                Id = 9, Name = "Autre société", CompanyId = OtherCompanyId, VehicleId = 9,
                Status = "planned", ScheduledStartTime = Start,
                Waypoints = new List<TourWaypoint>
                {
                    new() { Id = 91, SequenceOrder = 0, Type = "origin", Latitude = OriginLat, Longitude = OriginLon },
                    new() { Id = 92, SequenceOrder = 1, Type = "destination", Latitude = DestLat, Longitude = DestLon }
                }
            });

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
    }

    // ---------------------------------------------------------------- outils

    private static int? StatusOf(IActionResult result) => (result as IStatusCodeActionResult)?.StatusCode;

    private static JsonElement Payload(ActionResult result) =>
        JsonSerializer.SerializeToElement(result.Should().BeAssignableTo<ObjectResult>().Subject.Value);

    private static string Message(ActionResult result)
    {
        result.Should().BeOfType<BadRequestObjectResult>();
        return Payload(result).GetProperty("message").GetString()!;
    }

    private static async Task<List<int>> ListedIdsAsync(TestGisDbContext ctx, ICurrentTenantService tenant)
    {
        var result = await Controller(ctx, tenant).GetTours();
        return Payload(result).GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("Id").GetInt32())
            .OrderBy(id => id)
            .ToList();
    }

    private static async Task<Tour> ReloadAsync(TestGisDbContext ctx, int id)
    {
        ctx.ChangeTracker.Clear();
        return await ctx.Tours.AsNoTracking()
            .Include(t => t.Waypoints)
            .Include(t => t.Pauses)
            .FirstAsync(t => t.Id == id);
    }

    private static List<TourWaypoint> Ordered(Tour t) => t.Waypoints.OrderBy(w => w.SequenceOrder).ToList();

    /// <summary>Étapes de la tournée 1 telles que l'écran les renvoie à la modification.</summary>
    private static List<TourWaypointRequest> Tour1Waypoints() => new()
    {
        new() { Name = "Tunis", Latitude = OriginLat, Longitude = OriginLon, DeadlineMarginMinutes = 60 },
        new() { Name = "Arrêt", Latitude = StopLat, Longitude = StopLon, GeofenceId = 1, DeadlineMarginMinutes = 30, PlannedPauseMinutes = 10 },
        new() { Name = "Sousse", Latitude = DestLat, Longitude = DestLon, DeadlineMarginMinutes = 45 }
    };

    private static CreateTourRequest NewTour(int vehicleId, int? driverId = null, int? geofenceId = null) => new()
    {
        Name = "Nouvelle", VehicleId = vehicleId, DriverId = driverId, ScheduledStartTime = Start,
        Waypoints = new()
        {
            new() { Latitude = OriginLat, Longitude = OriginLon },
            new() { Latitude = DestLat, Longitude = DestLon, GeofenceId = geofenceId }
        }
    };

    /// <summary>IsCompleted vrai si et seulement si WaypointStatus vaut « completed ».</summary>
    private static void ShouldBeCoherent(IEnumerable<TourWaypoint> waypoints)
    {
        foreach (var w in waypoints)
            w.IsCompleted.Should().Be(w.WaypointStatus == "completed",
                $"l'étape {w.Id} a IsCompleted={w.IsCompleted} et WaypointStatus={w.WaypointStatus}");
    }

    // ------------------------------------------- 2a. véhicule et zones d'une autre société

    [Fact]
    public async Task Update_refuses_the_vehicle_of_another_company()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var result = await Controller(ctx, Admin()).UpdateTour(1, new UpdateTourRequest { VehicleId = 9 });

        Message(result).Should().Be("Véhicule introuvable ou non affecté à votre compte");
        (await ReloadAsync(ctx, 1)).VehicleId.Should().Be(1, "la tournée ne doit jamais pointer sur le véhicule d'une autre société");
    }

    [Fact]
    public async Task Create_refuses_the_vehicle_of_another_company()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var result = await Controller(ctx, Admin()).CreateTour(NewTour(vehicleId: 9));

        StatusOf(result).Should().Be(400);
        ctx.ChangeTracker.Clear();
        (await ctx.Tours.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Geofence_of_another_company_is_refused_on_create_and_update()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        Message(await Controller(ctx, Admin()).CreateTour(NewTour(vehicleId: 1, geofenceId: 9)))
            .Should().Be("Zone géofence introuvable");

        var waypoints = Tour1Waypoints();
        waypoints[1].GeofenceId = 9;
        Message(await Controller(ctx, Admin()).UpdateTour(1, new UpdateTourRequest { Waypoints = waypoints }))
            .Should().Be("Zone géofence introuvable");

        Ordered(await ReloadAsync(ctx, 1))[1].GeofenceId.Should().Be(1, "la zone d'origine est conservée");

        // Une zone de la société passe.
        StatusOf(await Controller(ctx, Admin()).CreateTour(NewTour(vehicleId: 1, geofenceId: 1))).Should().Be(201);
    }

    // ------------------------------------------- 2b. portée véhicules (VehicleScope)

    [Fact]
    public async Task Admin_sees_every_tour_of_the_company_and_never_another_company()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        (await ListedIdsAsync(ctx, Admin())).Should().Equal(1, 2);
        Payload(await Controller(ctx, Admin()).GetTourStats()).GetProperty("total").GetInt32().Should().Be(2);
        StatusOf(await Controller(ctx, Admin()).GetTour(2)).Should().Be(200);
        StatusOf(await Controller(ctx, Admin()).GetTour(9)).Should().Be(404);
    }

    [Fact]
    public async Task Restricted_user_lists_and_counts_only_tours_of_assigned_vehicles()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        (await ListedIdsAsync(ctx, Restricted())).Should().Equal(new[] { 1 }, "seul le véhicule 1 lui est affecté");
        Payload(await Controller(ctx, Restricted()).GetTourStats()).GetProperty("total").GetInt32().Should().Be(1);

        (await ListedIdsAsync(ctx, Orphan())).Should().BeEmpty("aucune affectation = aucune tournée, jamais l'absence de filtre");
    }

    [Fact]
    public async Task Restricted_user_gets_404_on_every_route_of_a_planned_tour_outside_scope()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        StatusOf(await Controller(ctx, Restricted()).GetTour(2)).Should().Be(404);
        StatusOf(await Controller(ctx, Restricted()).GetTourTracking(2)).Should().Be(404);
        StatusOf(await Controller(ctx, Restricted()).UpdateTour(2, new UpdateTourRequest { Name = "piratée" })).Should().Be(404);
        StatusOf(await Controller(ctx, Restricted()).StartTour(2)).Should().Be(404);
        StatusOf(await Controller(ctx, Restricted()).CancelTour(2)).Should().Be(404);
        StatusOf(await Controller(ctx, Restricted()).DeleteTour(2)).Should().Be(404);

        var tour = await ReloadAsync(ctx, 2);
        tour.Name.Should().Be("Camion hors portée");
        tour.Status.Should().Be("planned");

        // Sa propre tournée reste accessible.
        StatusOf(await Controller(ctx, Restricted()).GetTour(1)).Should().Be(200);
        StatusOf(await Controller(ctx, Restricted()).GetTourTracking(1)).Should().Be(200);
    }

    [Fact]
    public async Task Restricted_user_gets_404_on_every_action_of_a_running_tour_outside_scope()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        var running = await ctx.Tours.FirstAsync(t => t.Id == 2);
        running.Status = "in_progress";
        running.ActualStartTime = Start;
        ctx.TourPauses.Add(new TourPause { Id = 5, TourId = 2, StartTime = Start.AddMinutes(10) });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        StatusOf(await Controller(ctx, Restricted()).CompleteWaypoint(2, 22)).Should().Be(404);
        StatusOf(await Controller(ctx, Restricted()).AddPause(2, new AddPauseRequest())).Should().Be(404);
        StatusOf(await Controller(ctx, Restricted()).EndPause(2, 5)).Should().Be(404);
        StatusOf(await Controller(ctx, Restricted()).CompleteTour(2)).Should().Be(404);
        StatusOf(await Controller(ctx, Restricted()).CancelTour(2)).Should().Be(404);

        var tour = await ReloadAsync(ctx, 2);
        tour.Status.Should().Be("in_progress");
        tour.Pauses.Should().ContainSingle().Which.EndTime.Should().BeNull();
        tour.Waypoints.Should().OnlyContain(w => !w.IsCompleted);
    }

    [Fact]
    public async Task Admin_gets_404_on_every_action_of_a_running_tour_of_another_company()
    {
        // L'administrateur voit tout son parc (VehicleScope = null) : seul le
        // filtre société protège ces routes. EndPause a sa propre requête, hors
        // ScopedToursAsync — relecture du 18/09 : aucun test ne la couvrait.
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        var foreign = await ctx.Tours.FirstAsync(t => t.Id == 9);
        foreign.Status = "in_progress";
        foreign.ActualStartTime = Start;
        ctx.TourPauses.Add(new TourPause { Id = 95, TourId = 9, StartTime = Start.AddMinutes(10) });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        StatusOf(await Controller(ctx, Admin()).EndPause(9, 95)).Should().Be(404);
        StatusOf(await Controller(ctx, Admin()).AddPause(9, new AddPauseRequest())).Should().Be(404);
        StatusOf(await Controller(ctx, Admin()).StartTour(9)).Should().Be(404, "trouvée, elle répondrait 400 (déjà en cours)");
        StatusOf(await Controller(ctx, Admin()).CompleteWaypoint(9, 92)).Should().Be(404);
        StatusOf(await Controller(ctx, Admin()).CompleteTour(9)).Should().Be(404);
        StatusOf(await Controller(ctx, Admin()).CancelTour(9)).Should().Be(404);
        StatusOf(await Controller(ctx, Admin()).UpdateTour(9, new UpdateTourRequest { Name = "piratée" })).Should().Be(404);
        StatusOf(await Controller(ctx, Admin()).GetTourTracking(9)).Should().Be(404);
        StatusOf(await Controller(ctx, Admin()).DeleteTour(9)).Should().Be(404);

        var tour = await ReloadAsync(ctx, 9);
        tour.Status.Should().Be("in_progress");
        tour.Name.Should().Be("Autre société");
        tour.Pauses.Should().ContainSingle().Which.EndTime.Should().BeNull();
        tour.Waypoints.Should().OnlyContain(w => !w.IsCompleted);
    }

    // ------------------------------------------- véhicule transféré à une autre société

    private static async Task GiveVehicle1ADeviceAsync(TestGisDbContext ctx)
    {
        ctx.GpsDevices.Add(new GpsDevice { Id = 1, DeviceUid = "dev-1", CompanyId = CompanyId });
        (await ctx.Vehicles.FirstAsync(v => v.Id == 1)).GpsDeviceId = 1;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
    }

    /// <summary>Ce que fait UpdateAdminVehicleCommandHandler : véhicule et boîtier
    /// changent de société, les tournées ne bougent pas.</summary>
    private static async Task TransferVehicle1Async(TestGisDbContext ctx)
    {
        (await ctx.Vehicles.FirstAsync(v => v.Id == 1)).CompanyId = OtherCompanyId;
        (await ctx.GpsDevices.FirstAsync(d => d.Id == 1)).CompanyId = OtherCompanyId;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Tracking_no_longer_reads_the_position_of_a_vehicle_transferred_to_another_company()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        await GiveVehicle1ADeviceAsync(ctx);
        var redis = new Mock<IRedisCacheService>();
        redis.Setup(r => r.GetPositionAsync("dev-1")).ReturnsAsync(new VehiclePositionCache
        {
            DeviceUid = "dev-1", Latitude = 36.5, Longitude = 10.3, RecordedAt = DateTime.UtcNow
        });

        Payload(await Controller(ctx, Admin(), redis: redis.Object).GetTourTracking(1))
            .GetProperty("vehicle").ValueKind.Should().Be(JsonValueKind.Object);

        await TransferVehicle1Async(ctx);

        var after = Payload(await Controller(ctx, Admin(), redis: redis.Object).GetTourTracking(1));
        after.GetProperty("vehicle").ValueKind.Should().Be(JsonValueKind.Null,
            "la position appartient désormais à l'autre société");
    }

    [Fact]
    public async Task Detail_timeline_no_longer_reads_the_trace_of_a_vehicle_transferred_to_another_company()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        await GiveVehicle1ADeviceAsync(ctx);
        var tour = await ctx.Tours.FirstAsync(t => t.Id == 1);
        tour.Status = "completed";
        tour.ActualStartTime = Start;
        tour.ActualEndTime = Start.AddMinutes(30);
        foreach (var (minute, speed) in new[] { (1, 0.0), (5, 60.0), (10, 55.0), (20, 0.0) })
            ctx.GpsPositions.Add(new GpsPosition
            {
                DeviceId = 1, RecordedAt = Start.AddMinutes(minute), Latitude = OriginLat, Longitude = OriginLon,
                SpeedKph = speed, IsValid = true, CreatedAt = Start.AddMinutes(minute)
            });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        Payload(await Controller(ctx, Admin()).GetTour(1))
            .GetProperty("timeline").ValueKind.Should().Be(JsonValueKind.Object);

        await TransferVehicle1Async(ctx);

        Payload(await Controller(ctx, Admin()).GetTour(1))
            .GetProperty("timeline").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Restricted_user_cannot_create_or_move_a_tour_onto_a_vehicle_outside_scope()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        StatusOf(await Controller(ctx, Restricted()).CreateTour(NewTour(vehicleId: 2))).Should().Be(400);
        StatusOf(await Controller(ctx, Restricted()).UpdateTour(1, new UpdateTourRequest { VehicleId = 2 })).Should().Be(400);
        (await ReloadAsync(ctx, 1)).VehicleId.Should().Be(1);

        StatusOf(await Controller(ctx, Restricted()).CreateTour(NewTour(vehicleId: 1))).Should().Be(201, "son propre véhicule reste utilisable");
    }

    // ------------------------------------------- 3a. chauffeur

    [Fact]
    public async Task Driver_of_another_company_is_refused_on_create_and_update()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        Message(await Controller(ctx, Admin()).CreateTour(NewTour(vehicleId: 1, driverId: 9))).Should().Be("Chauffeur introuvable");
        Message(await Controller(ctx, Admin()).UpdateTour(1, new UpdateTourRequest { DriverId = 9 })).Should().Be("Chauffeur introuvable");

        (await ReloadAsync(ctx, 1)).DriverId.Should().Be(1);
    }

    [Fact]
    public async Task Inactive_driver_cannot_be_assigned()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        Message(await Controller(ctx, Admin()).CreateTour(NewTour(vehicleId: 1, driverId: 2))).Should().Be("Ce chauffeur n'est pas actif");
        Message(await Controller(ctx, Admin()).UpdateTour(1, new UpdateTourRequest { DriverId = 2 })).Should().Be("Ce chauffeur n'est pas actif");

        StatusOf(await Controller(ctx, Admin()).UpdateTour(1, new UpdateTourRequest { DriverId = 3 })).Should().Be(200);
        (await ReloadAsync(ctx, 1)).DriverId.Should().Be(3);
    }

    [Fact]
    public async Task Driver_is_removed_by_an_explicit_null_and_kept_when_absent()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        StatusOf(await Controller(ctx, Admin()).UpdateTour(1, new UpdateTourRequest { Name = "Renommée" })).Should().Be(200);
        (await ReloadAsync(ctx, 1)).DriverId.Should().Be(1, "propriété absente = chauffeur inchangé");

        StatusOf(await Controller(ctx, Admin()).UpdateTour(1, new UpdateTourRequest { DriverId = null })).Should().Be(200);
        (await ReloadAsync(ctx, 1)).DriverId.Should().BeNull("null explicite = « Aucun chauffeur »");
    }

    [Fact]
    public async Task Update_accepts_the_unchanged_driver_even_if_deactivated_or_deleted()
    {
        // L'écran renvoie toujours le chauffeur déjà porté. Relecture du 18/09 :
        // une fiche supprimée (pas de clé étrangère sur tours."DriverId")
        // bloquait toute modification de la tournée par « Chauffeur introuvable ».
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        (await ctx.Tours.FirstAsync(t => t.Id == 1)).DriverId = 77;   // fiche 77 supprimée
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        StatusOf(await Controller(ctx, Admin())
            .UpdateTour(1, new UpdateTourRequest { Name = "Renommée", DriverId = 77 })).Should().Be(200);
        var renamed = await ReloadAsync(ctx, 1);
        renamed.Name.Should().Be("Renommée");
        renamed.DriverId.Should().Be(77);

        // Chauffeur devenu inactif depuis son affectation : idem.
        (await ctx.Tours.FirstAsync(t => t.Id == 1)).DriverId = 2;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        StatusOf(await Controller(ctx, Admin())
            .UpdateTour(1, new UpdateTourRequest { Name = "Encore", DriverId = 2 })).Should().Be(200);

        // Un CHANGEMENT reste contrôlé.
        Message(await Controller(ctx, Admin()).UpdateTour(1, new UpdateTourRequest { DriverId = 78 }))
            .Should().Be("Chauffeur introuvable");
        (await ReloadAsync(ctx, 1)).DriverId.Should().Be(2);
    }

    [Fact]
    public void Update_request_distinguishes_an_absent_driver_from_an_explicit_null()
    {
        // Mêmes réglages de base que MVC (camelCase, insensible à la casse).
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var absent = JsonSerializer.Deserialize<UpdateTourRequest>("{\"name\":\"x\"}", web)!;
        absent.DriverIdSpecified.Should().BeFalse();

        var cleared = JsonSerializer.Deserialize<UpdateTourRequest>("{\"driverId\":null}", web)!;
        cleared.DriverIdSpecified.Should().BeTrue();
        cleared.DriverId.Should().BeNull();

        var chosen = JsonSerializer.Deserialize<UpdateTourRequest>("{\"driverId\":3}", web)!;
        chosen.DriverIdSpecified.Should().BeTrue();
        chosen.DriverId.Should().Be(3);
    }

    [Fact]
    public async Task List_and_detail_return_the_driver_name()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var items = Payload(await Controller(ctx, Admin()).GetTours()).GetProperty("items").EnumerateArray().ToList();
        items.Single(e => e.GetProperty("Id").GetInt32() == 1).GetProperty("driverName").GetString().Should().Be("Ali Ben Salah");

        Payload(await Controller(ctx, Admin()).GetTour(1)).GetProperty("driverName").GetString().Should().Be("Ali Ben Salah");
    }

    // ------------------------------------------- 4a. la modification conserve les estimations

    [Fact]
    public async Task Update_with_routing_recomputes_arrivals_and_keeps_geofence_and_margins()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        var route = new ValhallaRouteResult
        {
            TotalDistanceKm = 140, TotalTimeSeconds = 55 * 60,
            LegTimesSeconds = new() { 30 * 60, 25 * 60 }, EncodedPolyline = "abc"
        };
        var newStart = Start.AddHours(1);

        var result = await Controller(ctx, Admin(), route)
            .UpdateTour(1, new UpdateTourRequest { ScheduledStartTime = newStart, Waypoints = Tour1Waypoints() });

        StatusOf(result).Should().Be(200);
        var wps = Ordered(await ReloadAsync(ctx, 1));
        wps.Select(w => w.EstimatedArrivalTime).Should().Equal(
            newStart, newStart.AddMinutes(30), newStart.AddMinutes(30 + 10 + 25));
        wps.Select(w => w.EstimatedLegMinutes).Should().Equal(0, 30, 25);
        wps[1].GeofenceId.Should().Be(1);
        wps.Select(w => w.DeadlineMarginMinutes).Should().Equal(60, 30, 45);
        wps.Should().OnlyContain(w => w.WaypointStatus == "pending" && !w.IsCompleted);
    }

    [Fact]
    public async Task Update_while_routing_is_down_keeps_the_existing_estimates()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        var newStart = Start.AddHours(1);

        var result = await Controller(ctx, Admin(), route: null)
            .UpdateTour(1, new UpdateTourRequest { ScheduledStartTime = newStart, Waypoints = Tour1Waypoints() });

        StatusOf(result).Should().Be(200);
        var wps = Ordered(await ReloadAsync(ctx, 1));
        wps.Select(w => w.EstimatedArrivalTime).Should().Equal(
            newStart, newStart.AddMinutes(40), newStart.AddMinutes(70));
        wps.Select(w => w.EstimatedLegMinutes).Should().Equal(0, 40, 20);
        wps[1].GeofenceId.Should().Be(1);
        wps.Select(w => w.DeadlineMarginMinutes).Should().Equal(60, 30, 45);
    }

    [Fact]
    public async Task Update_while_routing_is_down_estimates_an_inserted_stop_and_pushes_the_next_ones()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        var waypoints = Tour1Waypoints();
        waypoints.Insert(2, new TourWaypointRequest { Name = "Nouvel arrêt", Latitude = 36.00, Longitude = 10.50, PlannedPauseMinutes = 15 });

        StatusOf(await Controller(ctx, Admin(), route: null)
            .UpdateTour(1, new UpdateTourRequest { Waypoints = waypoints })).Should().Be(200);

        var wps = Ordered(await ReloadAsync(ctx, 1));
        var stops = waypoints.Select(w => new TourPlanning.StopInput(w.Latitude, w.Longitude, w.PlannedPauseMinutes)).ToList();
        var toNew = TourPlanning.FallbackLegSeconds(stops[1], stops[2]);
        var toDest = TourPlanning.FallbackLegSeconds(stops[2], stops[3]);

        // Tronçon Tunis → Arrêt inchangé : ses 40 min sont conservées.
        wps[1].EstimatedArrivalTime.Should().Be(Start.AddMinutes(40));
        // Nouvel arrêt : après la pause de 10 min à l'Arrêt, durée de repli.
        wps[2].EstimatedArrivalTime.Should().BeCloseTo(Start.AddMinutes(50).AddSeconds(toNew), TimeSpan.FromMilliseconds(1));
        // Sousse n'a PAS repris son ancienne heure (70 min) : détour et pause de
        // 15 min du nouvel arrêt compris (relecture du 18/09/2026).
        wps[3].EstimatedArrivalTime.Should().BeCloseTo(Start.AddMinutes(65).AddSeconds(toNew + toDest), TimeSpan.FromMilliseconds(1));
        wps[3].EstimatedArrivalTime.Should().BeAfter(Start.AddMinutes(70));
    }

    [Fact]
    public async Task Create_while_routing_is_down_gives_every_stop_a_deadline_so_a_missed_stop_cannot_lock_the_destination()
    {
        // Relecture du 18/09 : sans heure prévue, une étape manquée ne passait
        // jamais « temps_depasse », restait « pending » et le moniteur ne
        // validait plus la destination — tournée « En cours » pour toujours.
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        var request = NewTour(vehicleId: 1);
        request.Waypoints.Insert(1, new TourWaypointRequest { Name = "Client B", Latitude = StopLat, Longitude = StopLon, PlannedPauseMinutes = 15 });

        var created = await Controller(ctx, Admin(), route: null).CreateTour(request);
        StatusOf(created).Should().Be(201);
        var id = Payload(created).GetProperty("Id").GetInt32();

        ctx.ChangeTracker.Clear();
        var tour = await ctx.Tours.Include(t => t.Waypoints).FirstAsync(t => t.Id == id);
        var wps = Ordered(tour);
        wps.Should().OnlyContain(w => w.EstimatedArrivalTime.HasValue);
        wps[2].EstimatedArrivalTime.Should().BeAfter(wps[1].EstimatedArrivalTime!.Value.AddMinutes(15));

        // Déroulé du moniteur : démarrage à l'heure, Client B jamais détecté.
        TourPlanning.Start(tour, Start);
        var clientB = wps[1];
        var destination = wps[2];
        TourPlanning.HasPendingStopBefore(wps, destination).Should().BeTrue("Client B est encore attendu");

        var deadline = TourPlanning.DeadlineOf(clientB)!.Value;
        TourPlanning.IsOverdue(clientB, deadline).Should().BeFalse();
        TourPlanning.IsOverdue(clientB, deadline.AddMinutes(1)).Should().BeTrue();
        clientB.WaypointStatus = "temps_depasse";   // ce que fait alors le moniteur

        TourPlanning.HasPendingStopBefore(wps, destination).Should().BeFalse(
            "la destination peut de nouveau clôturer la tournée");
    }

    [Fact]
    public async Task Moving_the_start_without_touching_the_stops_shifts_the_arrivals()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        StatusOf(await Controller(ctx, Admin())
            .UpdateTour(1, new UpdateTourRequest { ScheduledStartTime = Start.AddMinutes(30) })).Should().Be(200);

        var tour = await ReloadAsync(ctx, 1);
        Ordered(tour).Select(w => w.EstimatedArrivalTime).Should().Equal(
            Start.AddMinutes(30), Start.AddMinutes(70), Start.AddMinutes(100));
        tour.ScheduledEndTime.Should().Be(Start.AddMinutes(100));
        Ordered(tour)[1].GeofenceId.Should().Be(1);
    }

    // ------------------------------------------- 4b. cohérence IsCompleted / WaypointStatus

    [Fact]
    public async Task Manual_start_marks_the_origin_completed_on_both_fields()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        StatusOf(await Controller(ctx, Admin()).StartTour(1)).Should().Be(200);

        var wps = Ordered(await ReloadAsync(ctx, 1));
        wps[0].IsCompleted.Should().BeTrue();
        wps[0].WaypointStatus.Should().Be("completed");
        wps[0].ActualArrivalTime.Should().NotBeNull();
        wps.Skip(1).Should().OnlyContain(w => w.WaypointStatus == "pending");
        ShouldBeCoherent(wps);
    }

    [Fact]
    public async Task Manual_waypoint_completion_updates_the_status_and_keeps_an_earlier_arrival()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        await Controller(ctx, Admin()).StartTour(1);
        ctx.ChangeTracker.Clear();

        StatusOf(await Controller(ctx, Admin()).CompleteWaypoint(1, 12)).Should().Be(200);
        var first = Ordered(await ReloadAsync(ctx, 1))[1];
        first.WaypointStatus.Should().Be("completed");
        first.IsCompleted.Should().BeTrue();

        // Un second clic ne réécrit pas l'heure d'arrivée.
        StatusOf(await Controller(ctx, Admin()).CompleteWaypoint(1, 12)).Should().Be(200);
        Ordered(await ReloadAsync(ctx, 1))[1].ActualArrivalTime.Should().Be(first.ActualArrivalTime);
    }

    [Fact]
    public async Task Manual_completion_skips_unreached_stops_and_completes_the_destination()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        await Controller(ctx, Admin()).StartTour(1);
        ctx.ChangeTracker.Clear();

        StatusOf(await Controller(ctx, Admin()).CompleteTour(1)).Should().Be(200);

        var tour = await ReloadAsync(ctx, 1);
        tour.Status.Should().Be("completed");
        var wps = Ordered(tour);
        wps.Select(w => w.WaypointStatus).Should().Equal("completed", "skipped", "completed");
        wps[1].IsCompleted.Should().BeFalse("rien ne prouve que le véhicule est passé à l'arrêt");
        ShouldBeCoherent(wps);
    }

    [Fact]
    public async Task Manual_completion_keeps_an_overdue_stop_overdue()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        await Controller(ctx, Admin()).StartTour(1);
        ctx.ChangeTracker.Clear();
        (await ctx.TourWaypoints.FirstAsync(w => w.Id == 12)).WaypointStatus = "temps_depasse";
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await Controller(ctx, Admin()).CompleteTour(1);

        var wps = Ordered(await ReloadAsync(ctx, 1));
        wps[1].WaypointStatus.Should().Be("temps_depasse");
        ShouldBeCoherent(wps);
    }

    // ------------------------------------------- 4c. démarrage manuel en retard

    [Fact]
    public async Task Late_manual_start_shifts_the_remaining_arrivals_by_the_delay()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        var scheduled = DateTime.UtcNow.AddMinutes(-30);
        var tour = await ctx.Tours.Include(t => t.Waypoints).FirstAsync(t => t.Id == 1);
        tour.ScheduledStartTime = scheduled;
        foreach (var w in tour.Waypoints)
            w.EstimatedArrivalTime = scheduled.AddMinutes(w.SequenceOrder switch { 0 => 0, 1 => 40, _ => 70 });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await Controller(ctx, Admin()).StartTour(1);

        Payload(result).GetProperty("estimatesShiftedMinutes").GetInt32().Should().Be(30);
        var started = await ReloadAsync(ctx, 1);
        var delay = started.ActualStartTime!.Value - scheduled;
        var wps = Ordered(started);
        wps[1].EstimatedArrivalTime.Should().BeCloseTo(scheduled.AddMinutes(40) + delay, TimeSpan.FromMilliseconds(1));
        wps[2].EstimatedArrivalTime.Should().BeCloseTo(scheduled.AddMinutes(70) + delay, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Manual_start_within_the_threshold_does_not_shift_the_arrivals()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        var scheduled = DateTime.UtcNow.AddMinutes(-1);
        var tour = await ctx.Tours.Include(t => t.Waypoints).FirstAsync(t => t.Id == 1);
        tour.ScheduledStartTime = scheduled;
        tour.Waypoints.Single(w => w.SequenceOrder == 1).EstimatedArrivalTime = scheduled.AddMinutes(40);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var result = await Controller(ctx, Admin()).StartTour(1);

        Payload(result).GetProperty("estimatesShiftedMinutes").GetInt32().Should().Be(0);
        Ordered(await ReloadAsync(ctx, 1))[1].EstimatedArrivalTime.Should().Be(scheduled.AddMinutes(40));
    }
}
