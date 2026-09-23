using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Xunit;
using DashboardService = global::GisAPI.Services.DashboardService;

namespace GisAPI.Tests.Services;

/// <summary>
/// Incident de confidentialité HERTZ (loueur, 307 véhicules loués à des clients
/// différents) — SUITE. Le premier correctif a cloisonné /api/dashboard/all, mais
/// la relecture a trouvé deux ROUTES SŒURS du même contrôleur qui reservaient le
/// même symptôme par une autre URL :
///   • <c>GET /api/dashboard/activity</c> refaisait la requête du bloc « Alertes »
///     (gps_alerts + passages de géofence) sans aucune portée, en publiant le NOM
///     DU VÉHICULE. Elle vit sous un commentaire « LEGACY ENDPOINTS » mais le front
///     l'appelle toujours (api.service.ts) ;
///   • <c>GET /api/dashboard/stats</c> comptait véhicules, alertes, entretiens,
///     coûts, trajets et géozones sur la SOCIÉTÉ entière.
///
/// Ce fichier couvre aussi la RÈGLE GÉOZONES du 23/09/2026, décidée par Slim et qui
/// CORRIGE la consigne de la veille (« liste des zones entière, ne cloisonner que les
/// compteurs ») : un client « ne devrait pas voir les géofences, ni recevoir de
/// notifications de ces géofences ou d'autres véhicules que les siens ».
///
/// PIÈGE COUVERT PARTOUT ICI — la portée a TROIS états :
///   <c>null</c> = administrateur, AUCUN filtre ; liste non vide = ses véhicules ;
///   liste VIDE = il ne voit RIEN. Les confondre casse un des deux bouts, d'où les
/// tests jumeaux sur l'administrateur HERTZ, qui n'a AUCUNE affectation et doit
/// continuer à tout voir.
///
/// Les tests appellent les MÉTHODES DE PRODUCTION (<c>StatsVehiclesAsync</c>,
/// <c>StatsCountsAsync</c>, <c>RecentActivityAsync</c>, <c>VisibleGeofencesAsync</c>) :
/// retirer un filtre du service fait échouer ces tests.
/// </summary>
public class DashboardRoutesSoeursScopeTests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;

    /// <summary>Locataire type : non-admin, affecté au seul véhicule 1, permission Géofences cochée.</summary>
    private const int LocataireUserId = 61;
    /// <summary>Administrateur de la société, ZÉRO ligne dans user_vehicles (cas HERTZ).</summary>
    private const int AdminUserId = 62;
    /// <summary>Non-admin sans aucune affectation : portée VIDE, il ne voit rien.</summary>
    private const int SansAffectationUserId = 64;
    /// <summary>Calque de Kap Pharma (users.id = 58) : restreint ET can_geofences = FALSE.</summary>
    private const int SansPermissionZonesUserId = 65;

    /// <summary>Zone rattachée au véhicule du locataire.</summary>
    private const int ZoneDuLocataire = 1;
    /// <summary>Zone rattachée au SEUL véhicule loué à quelqu'un d'autre : jamais visible du locataire.</summary>
    private const int ZoneDUnAutreVehicule = 2;
    /// <summary>
    /// Zone SANS aucune liaison — zone DE SOCIÉTÉ (le cas réel de HERTZ, qui n'a jamais
    /// rattaché ses zones). Depuis l'ajustement du 23/09/2026, visible d'un restreint qui
    /// a la case Géofences ; jamais de celui qui ne l'a pas (Kap Pharma).
    /// </summary>
    private const int ZoneSansLiaison = 3;
    /// <summary>Zone désactivée : jamais listée, même pour l'administrateur.</summary>
    private const int ZoneDesactivee = 4;

    private static readonly DateTime Aujourdhui = new(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DebutDuMois = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime AujourdhuiA(int heure) => Aujourdhui.AddHours(heure);

    // ── Les appels de production, sans copie de la règle ────────────────────────
    private static Task<List<int>?> ScopeIdsAsync(TestGisDbContext ctx, bool isAdmin, int userId) =>
        DashboardService.ScopeIdsAsync(ctx, isAdmin, userId, CancellationToken.None);

    private static Task<List<Vehicle>> StatsVehiclesAsync(TestGisDbContext ctx, List<int>? scopeIds) =>
        DashboardService.StatsVehiclesAsync(ctx, CompanyId, scopeIds, CancellationToken.None);

    private static Task<DashboardService.DashboardStatsCounts> StatsCountsAsync(
        TestGisDbContext ctx, int userId, List<int>? scopeIds) =>
        DashboardService.StatsCountsAsync(ctx, CompanyId, userId, scopeIds, Aujourdhui, DebutDuMois, CancellationToken.None);

    private static Task<List<DashboardService.DashboardActivity>> ActivityAsync(
        TestGisDbContext ctx, int userId, List<int>? scopeIds) =>
        DashboardService.RecentActivityAsync(ctx, CompanyId, userId, scopeIds, 20, CancellationToken.None);

    private static async Task<List<string>> ZonesVisiblesAsync(TestGisDbContext ctx, int userId, List<int>? scopeIds) =>
        (await DashboardService.VisibleGeofencesAsync(ctx, CompanyId, userId, scopeIds, CancellationToken.None))
        .Select(g => g.Name).ToList();

    /// <summary>
    /// Société 1 : trois véhicules, dont le seul véhicule 1 est loué au locataire.
    /// Une alerte, un trajet, une dépense, un entretien et des passages de géofence
    /// pour chacun — de quoi voir immédiatement si un compteur déborde de la portée.
    /// </summary>
    private static async Task SeedAsync(TestGisDbContext ctx)
    {
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Loué Kap", Plate = "111 TU 1", CompanyId = CompanyId, GpsDeviceId = 11 },
            new Vehicle { Id = 2, Name = "Loué Carthage", Plate = "222 TU 2", CompanyId = CompanyId, GpsDeviceId = 12 },
            new Vehicle { Id = 3, Name = "Loué Djerba", Plate = "333 TU 3", CompanyId = CompanyId, GpsDeviceId = 13 },
            new Vehicle { Id = 9, Name = "Etranger", Plate = "999 TU 9", CompanyId = OtherCompanyId, GpsDeviceId = 19 });

        // Les permissions sont lues EN BASE (le jeton ne porte que les rôles).
        ctx.Users.AddRange(
            Utilisateur(LocataireUserId, "locataire@hertz.tn", canGeofences: true),
            Utilisateur(AdminUserId, "admin@hertz.tn", canGeofences: true),
            Utilisateur(SansAffectationUserId, "nouveau@hertz.tn", canGeofences: true),
            Utilisateur(SansPermissionZonesUserId, "kap@hertz.tn", canGeofences: false));

        ctx.GpsAlerts.AddRange(
            new GpsAlert { Id = 1, CompanyId = CompanyId, VehicleId = 1, Type = "overspeed", Severity = "critical",
                           Message = "Alerte vehicule 1", Timestamp = AujourdhuiA(8), Resolved = false },
            new GpsAlert { Id = 2, CompanyId = CompanyId, VehicleId = 2, Type = "overspeed", Severity = "warning",
                           Message = "Alerte vehicule 2", Timestamp = AujourdhuiA(9), Resolved = false },
            new GpsAlert { Id = 3, CompanyId = CompanyId, VehicleId = 3, Type = "overspeed", Severity = "info",
                           Message = "Alerte vehicule 3", Timestamp = AujourdhuiA(10), Resolved = false });

        ctx.Geofences.AddRange(
            new Geofence { Id = ZoneDuLocataire, CompanyId = CompanyId, Name = "Depot Rades", IsActive = true },
            new Geofence { Id = ZoneDUnAutreVehicule, CompanyId = CompanyId, Name = "Chantier Sfax", IsActive = true },
            new Geofence { Id = ZoneSansLiaison, CompanyId = CompanyId, Name = "Zone sans liaison", IsActive = true },
            new Geofence { Id = ZoneDesactivee, CompanyId = CompanyId, Name = "Ancien depot", IsActive = false });

        // Liaisons zone ↔ véhicule : c'est ELLES qui ouvrent une zone à un restreint.
        ctx.GeofenceVehicles.AddRange(
            new GeofenceVehicle { GeofenceId = ZoneDuLocataire, VehicleId = 1 },
            new GeofenceVehicle { GeofenceId = ZoneDUnAutreVehicule, VehicleId = 2 },
            new GeofenceVehicle { GeofenceId = ZoneDesactivee, VehicleId = 1 });

        ctx.GeofenceEvents.AddRange(
            new GeofenceEvent { Id = 1, CompanyId = CompanyId, GeofenceId = ZoneDuLocataire, VehicleId = 1,
                                Type = "entry", Timestamp = AujourdhuiA(7) },
            // Son véhicule dans une zone DE SOCIÉTÉ (sans liaison) : visible depuis le 23/09.
            new GeofenceEvent { Id = 2, CompanyId = CompanyId, GeofenceId = ZoneSansLiaison, VehicleId = 1,
                                Type = "entry", Timestamp = AujourdhuiA(11) },
            new GeofenceEvent { Id = 3, CompanyId = CompanyId, GeofenceId = ZoneDUnAutreVehicule, VehicleId = 2,
                                Type = "entry", Timestamp = AujourdhuiA(12) },
            // Historique : le véhicule du locataire est passé dans une zone aujourd'hui
            // rattachée au SEUL véhicule d'un autre client. Le passage publierait le NOM de
            // cette zone, qu'il n'a pas le droit de connaître : il ne doit pas sortir.
            new GeofenceEvent { Id = 4, CompanyId = CompanyId, GeofenceId = ZoneDUnAutreVehicule, VehicleId = 1,
                                Type = "entry", Timestamp = AujourdhuiA(13) });

        ctx.Trips.AddRange(
            new Trip { Id = 1, CompanyId = CompanyId, VehicleId = 1, Status = "completed",
                       StartTime = AujourdhuiA(6), DistanceKm = 10m },
            new Trip { Id = 2, CompanyId = CompanyId, VehicleId = 2, Status = "completed",
                       StartTime = AujourdhuiA(6), DistanceKm = 90m });

        ctx.VehicleCosts.AddRange(
            new VehicleCost { Id = 1, CompanyId = CompanyId, VehicleId = 1, Type = "fuel",
                              Amount = 100m, Date = AujourdhuiA(5) },
            new VehicleCost { Id = 2, CompanyId = CompanyId, VehicleId = 2, Type = "fuel",
                              Amount = 500m, Date = AujourdhuiA(5) });

        ctx.MaintenanceTemplates.Add(new MaintenanceTemplate { Id = 1, CompanyId = CompanyId, Name = "Vidange", IsActive = true });
        ctx.VehicleMaintenanceSchedules.AddRange(
            new VehicleMaintenanceSchedule { Id = 1, CompanyId = CompanyId, VehicleId = 1, TemplateId = 1, Status = "due" },
            new VehicleMaintenanceSchedule { Id = 2, CompanyId = CompanyId, VehicleId = 2, TemplateId = 1, Status = "overdue" });

        // Le locataire et Kap Pharma n'ont QUE le véhicule 1. L'admin n'a AUCUNE ligne ici.
        ctx.UserVehicles.AddRange(
            new UserVehicle { Id = 1, UserId = LocataireUserId, VehicleId = 1 },
            new UserVehicle { Id = 2, UserId = SansPermissionZonesUserId, VehicleId = 1 });

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
    }

    private static User Utilisateur(int id, string email, bool canGeofences) => new()
    {
        Id = id,
        FirstName = "Compte",
        LastName = email,
        Email = email,
        PasswordHash = "x",
        CompanyId = CompanyId,
        Status = "active",
        CanGeofences = canGeofences
    };

    // ───────────────── 2A : GET /api/dashboard/stats ─────────────────

    [Fact]
    public async Task Stats_un_locataire_ne_compte_que_ses_vehicules()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: LocataireUserId);
        scope.Should().Equal(new[] { 1 });

        var vehicules = await StatsVehiclesAsync(ctx, scope);
        vehicules.Select(v => v.Id).Should().Equal(new[] { 1 },
            "le compteur « parc » de /stats portait sur les 307 véhicules de HERTZ");

        var counts = await StatsCountsAsync(ctx, LocataireUserId, scope);
        counts.UnresolvedAlerts.Should().Be(1);
        counts.AlertsToday.Should().Be(1);
        counts.UpcomingMaintenance.Should().Be(1, "seul l'entretien « due » du véhicule 1");
        counts.OverdueMaintenance.Should().Be(0, "le « overdue » est sur un véhicule loué à quelqu'un d'autre");
        counts.CostsThisMonth.Should().Be(100m, "les 500 du véhicule 2 ne le regardent pas");
        counts.FuelCostsThisMonth.Should().Be(100m);
        counts.TripsToday.Should().Be(1);
        counts.DistanceToday.Should().Be(10m, "les 90 km du véhicule 2 ne le regardent pas");
        counts.ActiveGeofences.Should().Be(2,
            "la zone rattachée à son véhicule et la zone de société sans liaison — jamais celle de l'autre client");
        counts.GeofenceEventsToday.Should().Be(2,
            "ses deux passages dans des zones visibles ; celui dans la zone de l'autre client ne compte pas");
    }

    /// <summary>TEST JUMEAU — rien ne change pour l'administrateur, qui n'a aucune affectation.</summary>
    [Fact]
    public async Task Stats_un_admin_sans_affectation_voit_toujours_tout()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: true, userId: AdminUserId);
        scope.Should().BeNull("un admin n'est jamais restreint par les affectations");

        (await StatsVehiclesAsync(ctx, scope)).Select(v => v.Id).Should().BeEquivalentTo(new[] { 1, 2, 3 });

        var counts = await StatsCountsAsync(ctx, AdminUserId, scope);
        counts.UnresolvedAlerts.Should().Be(3);
        counts.AlertsToday.Should().Be(3);
        counts.UpcomingMaintenance.Should().Be(1);
        counts.OverdueMaintenance.Should().Be(1);
        counts.CostsThisMonth.Should().Be(600m);
        counts.TripsToday.Should().Be(2);
        counts.DistanceToday.Should().Be(100m);
        counts.ActiveGeofences.Should().Be(3, "les 3 zones ACTIVES de la société, la désactivée exclue");
        counts.GeofenceEventsToday.Should().Be(4);
    }

    [Fact]
    public async Task Stats_un_non_admin_sans_affectation_ne_compte_rien()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: SansAffectationUserId);
        scope.Should().NotBeNull().And.BeEmpty("liste vide = aucun véhicule visible, surtout pas tout le parc");

        (await StatsVehiclesAsync(ctx, scope)).Should().BeEmpty();

        var counts = await StatsCountsAsync(ctx, SansAffectationUserId, scope);
        counts.UnresolvedAlerts.Should().Be(0);
        counts.CostsThisMonth.Should().Be(0m);
        counts.TripsToday.Should().Be(0);
        counts.ActiveGeofences.Should().Be(0);
        counts.GeofenceEventsToday.Should().Be(0);
    }

    // ───────────────── 2A : GET /api/dashboard/activity ─────────────────

    [Fact]
    public async Task Activity_un_locataire_ne_voit_que_son_vehicule()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: LocataireUserId);
        var flux = await ActivityAsync(ctx, LocataireUserId, scope);

        flux.Select(a => a.Message).Should().BeEquivalentTo(new[]
        {
            "Alerte vehicule 1",
            "entry - Depot Rades",
            "entry - Zone sans liaison"
        }, "ses alertes, et ses passages dans les seules zones qu'il a le droit de voir");

        flux.Should().NotContain(a => a.VehicleName != "Loué Kap",
            "cette route publie le NOM DU VÉHICULE : c'est exactement le symptôme signalé");
        flux.Should().NotContain(a => a.Message == "entry - Chantier Sfax",
            "le libellé porte le NOM DE LA ZONE de l'autre client, qu'il n'a pas le droit de connaître — même quand c'est SON véhicule qui l'a traversée");
    }

    /// <summary>TEST JUMEAU — l'administrateur garde le flux complet de sa société.</summary>
    [Fact]
    public async Task Activity_un_admin_sans_affectation_voit_tout_le_parc()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: true, userId: AdminUserId);
        var flux = await ActivityAsync(ctx, AdminUserId, scope);

        flux.Should().HaveCount(7, "3 alertes + 4 passages de géofence de la société");
        flux.Select(a => a.Message).Should().Contain("entry - Zone sans liaison");
        flux.Should().BeInDescendingOrder(a => a.Timestamp);
    }

    [Fact]
    public async Task Activity_un_non_admin_sans_affectation_ne_voit_rien()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: SansAffectationUserId);
        (await ActivityAsync(ctx, SansAffectationUserId, scope)).Should().BeEmpty();
    }

    // ───────────────── 2B : la règle géozones du 23/09/2026 ─────────────────

    /// <summary>
    /// Règle ajustée le 23/09/2026 : un restreint AVEC la case Géofences voit les zones
    /// rattachées à ses véhicules UNION les zones rattachées à aucun véhicule (zones de
    /// société, que gèrent au quotidien les Opérateurs de SICOAC). Jamais une zone
    /// rattachée uniquement aux véhicules d'un autre.
    /// </summary>
    [Fact]
    public async Task Zones_un_restreint_voit_les_siennes_et_celles_de_societe_jamais_celles_d_un_autre()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: LocataireUserId);
        var zones = await ZonesVisiblesAsync(ctx, LocataireUserId, scope);

        zones.Should().Equal(new[] { "Depot Rades", "Zone sans liaison" });
        zones.Should().NotContain("Chantier Sfax", "elle est rattachée au SEUL véhicule d'un autre client");
        zones.Should().NotContain("Ancien depot", "zone désactivée");
    }

    /// <summary>
    /// Une zone partagée (son véhicule ET celui d'un autre) reste visible : il suffit
    /// qu'UN de ses véhicules y soit rattaché. Et elle ne devient pas « de société ».
    /// </summary>
    [Fact]
    public async Task Zones_une_zone_partagee_avec_un_autre_reste_visible()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);
        ctx.GeofenceVehicles.Add(new GeofenceVehicle { GeofenceId = ZoneDUnAutreVehicule, VehicleId = 1 });
        await ctx.SaveChangesAsync();

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: LocataireUserId);
        (await ZonesVisiblesAsync(ctx, LocataireUserId, scope))
            .Should().BeEquivalentTo(new[] { "Depot Rades", "Chantier Sfax", "Zone sans liaison" });
    }

    /// <summary>
    /// Calque de Kap Pharma (users.id = 58, can_geofences = FALSE) : pas de zone du
    /// tout, même celle qui est rattachée à son véhicule.
    /// </summary>
    [Fact]
    public async Task Zones_sans_la_permission_geofences_aucune_zone()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: SansPermissionZonesUserId);
        scope.Should().Equal(new[] { 1 }, "il a bien un véhicule, et la zone 1 lui est bien rattachée");

        (await ZonesVisiblesAsync(ctx, SansPermissionZonesUserId, scope)).Should().BeEmpty(
            "la case Géofences est décochée : aucune zone, même rattachée");

        var counts = await StatsCountsAsync(ctx, SansPermissionZonesUserId, scope);
        counts.ActiveGeofences.Should().Be(0);
        counts.GeofenceEventsToday.Should().Be(0);

        (await ActivityAsync(ctx, SansPermissionZonesUserId, scope))
            .Should().NotContain(a => a.Type == "geofence",
                "il ne doit recevoir aucune notification de géofence");
    }

    /// <summary>TEST JUMEAU — la règle ne change RIEN pour un administrateur.</summary>
    [Fact]
    public async Task Zones_un_admin_voit_toutes_les_zones_actives()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: true, userId: AdminUserId);
        var zones = await ZonesVisiblesAsync(ctx, AdminUserId, scope);

        zones.Should().BeEquivalentTo(new[] { "Depot Rades", "Chantier Sfax", "Zone sans liaison" },
            "y compris celles qui ne sont rattachées à aucun véhicule");
    }

    [Fact]
    public async Task Zones_un_restreint_sans_affectation_ne_voit_aucune_zone()
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var scope = await ScopeIdsAsync(ctx, isAdmin: false, userId: SansAffectationUserId);
        (await ZonesVisiblesAsync(ctx, SansAffectationUserId, scope)).Should().BeEmpty();
    }
}
