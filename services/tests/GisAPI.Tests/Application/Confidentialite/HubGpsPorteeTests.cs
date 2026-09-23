using System.Security.Claims;
using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Hubs;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Confidentialite;

/// <summary>
/// HUB TEMPS RÉEL — le trou le plus large de l'incident HERTZ, et le seul qu'aucun
/// correctif de contrôleur ne pouvait boucher.
///
/// Chaque connexion rejoignait le groupe <c>company_{id}</c>, et CHAQUE trame du parc y
/// était diffusée : Kap Pharma (utilisateur 58, affecté à 2 véhicules sur 307) recevait
/// en continu la position des 305 autres — sans appeler la moindre route REST. Fermer les
/// API n'y changeait rien. <c>SubscribeToVehicle</c> et <c>SubscribeToGeofence</c>
/// acceptaient en prime N'IMPORTE QUEL identifiant, sans vérifier ni la société ni la
/// portée.
///
/// Le cloisonnement se fait maintenant par GROUPE, à la connexion :
///   • <c>company_{id}</c> — tout le monde, mais plus AUCUNE donnée véhicule n'y passe
///     (seuls les messages d'abonnement de la société) ;
///   • <c>fleet_{id}</c> — ceux qui voient tout le parc (administrateurs) ;
///   • <c>scope_{id}_vehicle_{vid}</c> — un groupe par véhicule de la portée, rejoint par
///     les utilisateurs restreints ; ils y reçoivent « PositionUpdate » et « Alert » sous
///     le même nom d'événement, donc l'écran de monitoring ne change pas.
///
/// TROIS états, vérifiés ici : administrateur → groupe flotte ; portée non vide → ses
/// groupes véhicule ; portée VIDE → aucun groupe de données. Le cas piège (utilisateur 11
/// de HERTZ, administrateur SANS aucune affectation) a son test.
/// </summary>
public class HubGpsPorteeTests
{
    private const int CompanyId = 1;
    private const int LocataireUserId = 58;
    private const int AdminUserId = 11;
    private const int SansAffectationUserId = 90;

    private const int VehiculeA = 1;
    private const int VehiculeB = 2;
    private const int ZoneA = 101;
    private const int ZoneB = 102;
    private const int ZoneSansLiaison = 103;

    private sealed record Connexion(bool Aborted, List<string> Groups);

    private static async Task<TestGisDbContext> ParcAsync(bool locataireVoitLesZones = true)
    {
        var ctx = TestDbContextFactory.Create();

        ctx.Users.AddRange(
            new User { Id = LocataireUserId, CompanyId = CompanyId, Email = "kap@pharma.tn", FirstName = "Kap", LastName = "Pharma", CanGeofences = locataireVoitLesZones },
            new User { Id = AdminUserId, CompanyId = CompanyId, Email = "admin@hertz.tn", FirstName = "Admin", LastName = "Hertz", CanGeofences = true },
            new User { Id = SansAffectationUserId, CompanyId = CompanyId, Email = "sans@hertz.tn", FirstName = "Sans", LastName = "Affectation" });

        ctx.Vehicles.AddRange(
            new Vehicle { Id = VehiculeA, Name = "Loué Kap Pharma", Plate = "111 TU 1", CompanyId = CompanyId },
            new Vehicle { Id = VehiculeB, Name = "Loué Carthage", Plate = "222 TU 2", CompanyId = CompanyId },
            new Vehicle { Id = 9, Name = "Autre société", Plate = "999 TU 9", CompanyId = 2 });

        ctx.Geofences.AddRange(
            new Geofence { Id = ZoneA, Name = "Dépôt Tunis", CompanyId = CompanyId, IsActive = true },
            new Geofence { Id = ZoneB, Name = "Dépôt Sousse", CompanyId = CompanyId, IsActive = true },
            new Geofence { Id = ZoneSansLiaison, Name = "Zone sans liaison", CompanyId = CompanyId, IsActive = true });

        ctx.GeofenceVehicles.AddRange(
            new GeofenceVehicle { GeofenceId = ZoneA, VehicleId = VehiculeA },
            new GeofenceVehicle { GeofenceId = ZoneB, VehicleId = VehiculeB });

        ctx.UserVehicles.Add(new UserVehicle { Id = 1, UserId = LocataireUserId, VehicleId = VehiculeA });

        await ctx.SaveChangesAsync();
        return ctx;
    }

    private static (GpsHub Hub, List<string> Groupes, Func<bool> Aborted) Hub(
        TestGisDbContext ctx, int userId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("companyId", CompanyId.ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var aborted = false;
        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));
        context.SetupGet(c => c.ConnectionId).Returns("cnx-1");
        context.Setup(c => c.Abort()).Callback(() => aborted = true);

        var groupes = new List<string>();
        var groupManager = new Mock<IGroupManager>();
        groupManager.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, group, _) => groupes.Add(group))
            .Returns(Task.CompletedTask);

        var hub = new GpsHub(NullLogger<GpsHub>.Instance, ctx)
        {
            Context = context.Object,
            Groups = groupManager.Object
        };

        return (hub, groupes, () => aborted);
    }

    // ───────────────── Groupes à la connexion ─────────────────

    [Fact]
    public async Task A_la_connexion_le_locataire_ne_rejoint_que_les_groupes_de_ses_vehicules()
    {
        using var ctx = await ParcAsync();
        var (hub, groupes, _) = Hub(ctx, LocataireUserId, "Operateur");

        await hub.OnConnectedAsync();

        groupes.Should().Contain(GroupesGps.Vehicule(CompanyId, VehiculeA));
        groupes.Should().NotContain(GroupesGps.Vehicule(CompanyId, VehiculeB));
        groupes.Should().NotContain(GroupesGps.Flotte(CompanyId),
            "le groupe flotte reçoit CHAQUE trame du parc : il est réservé à ceux qui voient tout");
        groupes.Should().Contain(GroupesGps.Societe(CompanyId),
            "le groupe société reste, mais plus aucune donnée véhicule n'y passe");
    }

    [Fact]
    public async Task A_la_connexion_l_administrateur_sans_affectation_rejoint_le_groupe_flotte()
    {
        using var ctx = await ParcAsync();
        var (hub, groupes, _) = Hub(ctx, AdminUserId, "company_admin");

        await hub.OnConnectedAsync();

        groupes.Should().Contain(GroupesGps.Flotte(CompanyId),
            "portée null = il voit tout le parc, exactement comme avant le correctif");
        groupes.Should().NotContain(g => g.StartsWith($"scope_{CompanyId}_vehicle_"));
    }

    [Fact]
    public async Task A_la_connexion_un_non_administrateur_sans_affectation_ne_rejoint_aucun_groupe_de_donnees()
    {
        using var ctx = await ParcAsync();
        var (hub, groupes, _) = Hub(ctx, SansAffectationUserId, "Operateur");

        await hub.OnConnectedAsync();

        groupes.Should().NotContain(GroupesGps.Flotte(CompanyId));
        groupes.Should().NotContain(g => g.StartsWith($"scope_{CompanyId}_vehicle_"));
        groupes.Should().Equal(GroupesGps.Societe(CompanyId), $"user_{SansAffectationUserId}");
    }

    // ───────────────── Abonnements explicites ─────────────────

    [Fact]
    public async Task S_abonner_au_vehicule_d_un_autre_locataire_ou_d_une_autre_societe_est_refuse()
    {
        using var ctx = await ParcAsync();
        var (hub, groupes, _) = Hub(ctx, LocataireUserId, "Operateur");

        await hub.SubscribeToVehicle(VehiculeB);
        await hub.SubscribeToVehicle(9);
        groupes.Should().BeEmpty("la méthode acceptait n'importe quel identifiant, depuis la console du navigateur");

        await hub.SubscribeToVehicle(VehiculeA);
        groupes.Should().Equal($"vehicle_{VehiculeA}");
    }

    [Fact]
    public async Task S_abonner_a_un_vehicule_l_administrateur_sans_affectation_garde_la_main()
    {
        using var ctx = await ParcAsync();
        var (hub, groupes, _) = Hub(ctx, AdminUserId, "company_admin");

        await hub.SubscribeToVehicle(VehiculeB);
        groupes.Should().Equal($"vehicle_{VehiculeB}");

        await hub.SubscribeToVehicle(9);
        groupes.Should().Equal(new[] { $"vehicle_{VehiculeB}" },
            "une autre société reste hors d'atteinte, même pour un administrateur de société");
    }

    [Fact]
    public async Task S_abonner_a_une_geozone_suit_la_regle_du_23_09()
    {
        using var ctx = await ParcAsync();
        var (hub, groupes, _) = Hub(ctx, LocataireUserId, "Operateur");

        await hub.SubscribeToGeofence(ZoneB);
        groupes.Should().BeEmpty("une zone rattachée au SEUL véhicule d'un autre client lui reste invisible");

        // Ajustement du 23/09/2026 : une zone SANS liaison est une zone de société,
        // VISIBLE d'un restreint qui a la case Géofences (voir VisibleGeofenceIdsAsync).
        // Mais la SUIVRE EN DIRECT est une autre affaire (GpsHub.ZonesSuiviesEnDirectAsync) :
        // la surveillance l'applique à TOUT le parc, et « GeofenceEvent » part au groupe de
        // la zone pour chaque véhicule qui la franchit — identifiant, nom, position. Voir
        // la zone ne doit pas donner les passages des véhicules des autres.
        await hub.SubscribeToGeofence(ZoneSansLiaison);
        groupes.Should().BeEmpty("ses passages en direct seraient ceux de tout le parc");

        await hub.SubscribeToGeofence(ZoneA);
        groupes.Should().Equal($"geofence_{ZoneA}");
    }

    [Fact]
    public async Task S_abonner_a_une_geozone_sans_la_case_Geofences_est_refuse()
    {
        // Calque exact de Kap Pharma : can_geofences = FALSE.
        using var ctx = await ParcAsync(locataireVoitLesZones: false);
        var (hub, groupes, _) = Hub(ctx, LocataireUserId, "Operateur");

        await hub.SubscribeToGeofence(ZoneA);
        groupes.Should().BeEmpty();

        var (hubAdmin, groupesAdmin, _) = Hub(ctx, AdminUserId, "company_admin");
        await hubAdmin.SubscribeToGeofence(ZoneSansLiaison);
        groupesAdmin.Should().Equal($"geofence_{ZoneSansLiaison}");
    }

    // ───────────────── Diffusion ─────────────────

    [Fact]
    public async Task Une_position_part_au_groupe_flotte_ET_au_groupe_du_seul_vehicule_concerne()
    {
        var (service, groupesUtilises) = Diffusion();

        await service.SendPositionUpdateAsync(CompanyId, VehiculeA, new { });

        groupesUtilises.Should().Equal(GroupesGps.Flotte(CompanyId), GroupesGps.Vehicule(CompanyId, VehiculeA));
        groupesUtilises.Should().NotContain(GroupesGps.Societe(CompanyId),
            "c'est le groupe société qui arrosait les 307 véhicules");
    }

    [Fact]
    public async Task Une_alerte_suit_exactement_la_meme_regle_que_la_position()
    {
        var (service, groupesUtilises) = Diffusion();

        await service.SendAlertAsync(CompanyId, VehiculeB, new { });

        groupesUtilises.Should().Equal(GroupesGps.Flotte(CompanyId), GroupesGps.Vehicule(CompanyId, VehiculeB));
    }

    [Fact]
    public async Task Une_position_sans_vehicule_connu_ne_part_qu_au_groupe_flotte()
    {
        var (service, groupesUtilises) = Diffusion();

        await service.SendPositionUpdateAsync(CompanyId, new { });

        groupesUtilises.Should().Equal(new[] { GroupesGps.Flotte(CompanyId) },
            "sans véhicule, la diffusion ne peut atteindre que les destinataires « tout le parc »");
    }

    private static (GpsHubService Service, List<string> Groupes) Diffusion()
    {
        var groupes = new List<string>();

        var proxy = new Mock<IClientProxy>();
        proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>()))
               .Callback<IReadOnlyList<string>>(g => groupes.AddRange(g))
               .Returns(proxy.Object);
        clients.Setup(c => c.Group(It.IsAny<string>()))
               .Callback<string>(groupes.Add)
               .Returns(proxy.Object);

        var hubContext = new Mock<IHubContext<GpsHub>>();
        hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

        return (new GpsHubService(hubContext.Object), groupes);
    }
}
