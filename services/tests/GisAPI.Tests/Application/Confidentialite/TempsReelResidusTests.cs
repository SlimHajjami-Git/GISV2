using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Hubs;
using GisAPI.Infrastructure.MultiTenancy;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using DriverAppController = global::GisAPI.Controllers.DriverAppController;
using GpsController = global::GisAPI.Controllers.GpsController;
using TenantMiddleware = global::GisAPI.Middleware.TenantMiddleware;

namespace GisAPI.Tests.Application.Confidentialite;

/// <summary>
/// Incident HERTZ — QUATRIÈME passe : les derniers résidus du TEMPS RÉEL.
///
/// HERTZ (société 4) loue 307 véhicules à des clients distincts ; Kap Pharma
/// (utilisateur 58, « Operateur ») ne doit rien voir des 305 véhicules des autres.
/// Trois passes avaient fermé les écrans, les rapports, le hub et ses groupes. Restaient :
///
///   • C1 — les abonnements EXPLICITES (<c>SubscribeToVehicle</c>,
///     <c>SubscribeToGeofence</c>) vivent dans des groupes que la réévaluation
///     périodique de la portée ne relisait pas : un véhicule RETIRÉ continuait
///     d'envoyer chaque trame — et chaque passage de zone — jusqu'à la fermeture de
///     l'onglet ;
///   • C2 — neuf messages de TOURNÉE (nom de la tournée, étape, heure d'arrivée)
///     partaient au groupe SOCIÉTÉ, que tout le monde rejoint ;
///   • C3 — l'historique des commandes boîtier rendait le NOM d'un expéditeur d'une
///     AUTRE société ;
///   • C4 — l'indicateur « en train d'écrire » du chat visait n'importe quel compte,
///     toutes sociétés confondues.
///
/// IDENTITÉ : toujours un VRAI jeton (<see cref="JetonDeProduction"/>), émis par
/// JwtService et validé comme en ligne ; le tenant HTTP est posé par le VRAI
/// <see cref="TenantMiddleware"/>, celui du hub par <see cref="GpsHub.TenantDepuis"/>.
/// Aucun claim n'est fabriqué à la main.
///
/// RÉCEPTION : chaque connexion tient son appartenance SIMULÉE aux groupes (ajouts moins
/// retraits, par le code du hub lui-même). « X reçoit le message » = le message vise au
/// moins un groupe dont X est membre — exactement ce que fait SignalR.
///
/// PIÈGE COUVERT : la portée a TROIS états — <c>null</c> = administrateur, AUCUN filtre
/// (utilisateur 11 de HERTZ, ZÉRO affectation, doit tout voir) ; liste non vide = ses
/// véhicules ; liste VIDE = rien. D'où un jumeau « administrateur » sur chaque point.
/// </summary>
public class TempsReelResidusTests
{
    private const int CompanyId = 4;               // HERTZ
    private const int AutreSociete = 7;

    private const int LocataireUserId = 58;        // Kap Pharma, « Operateur »
    private const int AdminUserId = 11;            // administrateur HERTZ, ZÉRO affectation
    private const int AutreSocieteUserId = 500;    // compte d'une AUTRE société
    private const int ChauffeurUserId = 70;        // compte chauffeur (application mobile)
    private const int Fiche = 40;                  // drivers.id du chauffeur

    private const int VehiculeA = 1;               // loué à Kap Pharma, puis RETIRÉ
    private const int VehiculeB = 2;               // loué à un autre client
    private const int VehiculeC = 3;               // loué à Kap Pharma, reste à lui

    private const int ZoneA = 101;                 // rattachée au seul véhicule A
    private const int ZoneB = 102;                 // rattachée au seul véhicule B (autre client)
    private const int ZoneC = 103;                 // rattachée au seul véhicule C
    private const int ZoneSansLiaison = 104;       // zone de société : la surveillance l'applique à TOUT le parc
    private const int ZoneMixte = 105;             // rattachée à A (Kap Pharma) ET à B (autre client)

    // ───────────────────────── Montage ─────────────────────────

    /// <summary>HERTZ réduit : Kap Pharma a A et C, un autre client a B.</summary>
    private static async Task<TestGisDbContext> ParcAsync(bool locataireVoitLesZones = true)
    {
        var ctx = TestDbContextFactory.Create();

        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Roles.Add(new Role { Id = 1, Name = "Administrateur", SocieteId = CompanyId, IsCompanyAdmin = true });
        ctx.Roles.Add(new Role { Id = 2, Name = "Operateur", SocieteId = CompanyId });

        ctx.Users.AddRange(
            new User { Id = LocataireUserId, CompanyId = CompanyId, RoleId = 2, Email = "kap@pharma.tn", FirstName = "Kap", LastName = "Pharma",
                       PasswordHash = "x", Status = "active", CanGeofences = locataireVoitLesZones },
            new User { Id = AdminUserId, CompanyId = CompanyId, RoleId = 1, Email = "admin@hertz.tn", FirstName = "Admin", LastName = "Hertz",
                       PasswordHash = "x", Status = "active", CanGeofences = true },
            new User { Id = AutreSocieteUserId, CompanyId = AutreSociete, Email = "slim@plateforme.tn", FirstName = "Slim", LastName = "Plateforme",
                       PasswordHash = "x", Status = "active" },
            new User { Id = ChauffeurUserId, CompanyId = CompanyId, Email = "chauffeur@hertz.tn", FirstName = "Ali", LastName = "Chauffeur",
                       PasswordHash = "x", Status = "active" });

        ctx.Vehicles.AddRange(
            new Vehicle { Id = VehiculeA, Name = "Loué Kap Pharma", Plate = "111 TU 1", CompanyId = CompanyId },
            new Vehicle { Id = VehiculeB, Name = "Loué Carthage", Plate = "222 TU 2", CompanyId = CompanyId },
            new Vehicle { Id = VehiculeC, Name = "Loué Kap Pharma bis", Plate = "333 TU 3", CompanyId = CompanyId });

        ctx.Geofences.AddRange(
            new Geofence { Id = ZoneA, Name = "Dépôt Radès", CompanyId = CompanyId, IsActive = true },
            new Geofence { Id = ZoneB, Name = "Client Carthage", CompanyId = CompanyId, IsActive = true },
            new Geofence { Id = ZoneC, Name = "Dépôt Sousse", CompanyId = CompanyId, IsActive = true },
            new Geofence { Id = ZoneSansLiaison, Name = "Zone de tout le parc", CompanyId = CompanyId, IsActive = true },
            new Geofence { Id = ZoneMixte, Name = "Aéroport", CompanyId = CompanyId, IsActive = true });

        ctx.GeofenceVehicles.AddRange(
            new GeofenceVehicle { GeofenceId = ZoneA, VehicleId = VehiculeA },
            new GeofenceVehicle { GeofenceId = ZoneB, VehicleId = VehiculeB },
            new GeofenceVehicle { GeofenceId = ZoneC, VehicleId = VehiculeC },
            new GeofenceVehicle { GeofenceId = ZoneMixte, VehicleId = VehiculeA },
            new GeofenceVehicle { GeofenceId = ZoneMixte, VehicleId = VehiculeB });

        ctx.UserVehicles.AddRange(
            new UserVehicle { Id = 1, UserId = LocataireUserId, VehicleId = VehiculeA },
            new UserVehicle { Id = 2, UserId = LocataireUserId, VehicleId = VehiculeC });

        ctx.Drivers.Add(new Driver { Id = Fiche, CompanyId = CompanyId, UserId = ChauffeurUserId, FirstName = "Ali", LastName = "Chauffeur" });

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    private static ClaimsPrincipal JetonLocataire() =>
        JetonDeProduction.Principal(LocataireUserId, CompanyId, "Operateur");

    private static ClaimsPrincipal JetonAdmin() =>
        JetonDeProduction.Principal(AdminUserId, CompanyId, "Administrateur", estAdminSociete: true);

    /// <summary>Tenant d'une requête HTTP, posé par le VRAI TenantMiddleware depuis le jeton.</summary>
    private static async Task<(ICurrentTenantService Tenant, HttpContext Http)> RequeteAsync(ClaimsPrincipal jeton)
    {
        var tenant = new CurrentTenantService();
        var http = new DefaultHttpContext { User = jeton };
        await new TenantMiddleware(_ => Task.CompletedTask).InvokeAsync(http, tenant);
        tenant.UserId.Should().NotBeNull("le jeton doit suffire à identifier l'appelant, sinon le test ne prouve rien");
        return (tenant, http);
    }

    /// <summary>Un message SignalR tel qu'il part : événement, groupes visés, contenu.</summary>
    private sealed record Envoi(string Evenement, IReadOnlyList<string> Groupes, object? Message)
    {
        public int? TourId => Message?.GetType().GetProperty("tourId")?.GetValue(Message) as int?;
    }

    /// <summary>Capture de tout ce qui part par un <see cref="IHubContext{THub}"/> (Group ET Groups).</summary>
    private sealed class Emetteur
    {
        public List<Envoi> Envois { get; } = new();
        public IHubContext<GpsHub> HubContext { get; }

        public Emetteur()
        {
            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.Group(It.IsAny<string>()))
                   .Returns((string groupe) => Proxy(new[] { groupe }));
            clients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>()))
                   .Returns((IReadOnlyList<string> groupes) => Proxy(groupes.ToArray()));

            var hub = new Mock<IHubContext<GpsHub>>();
            hub.SetupGet(h => h.Clients).Returns(clients.Object);
            HubContext = hub.Object;
        }

        public IClientProxy Proxy(string[] groupes)
        {
            var proxy = new Mock<IClientProxy>();
            proxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                 .Callback((string evenement, object?[] args, CancellationToken _) =>
                     Envois.Add(new Envoi(evenement, groupes, args.FirstOrDefault())))
                 .Returns(Task.CompletedTask);
            return proxy.Object;
        }
    }

    /// <summary>
    /// Une connexion au hub, avec son appartenance aux groupes TENUE par le code du hub
    /// (ajouts moins retraits) et son <c>Context.Items</c> réel, où vit le registre des
    /// abonnements explicites.
    /// </summary>
    private sealed class Connexion
    {
        public string Id { get; }
        public ClaimsPrincipal Jeton { get; }
        public HashSet<string> Groupes { get; } = new();
        public List<string> Retraits { get; } = new();
        public Dictionary<object, object?> Items { get; } = new();
        public IGroupManager Gestionnaire { get; }
        public Emetteur VersLesClients { get; } = new();

        public Connexion(string id, ClaimsPrincipal jeton)
        {
            Id = id;
            Jeton = jeton;

            var gestionnaire = new Mock<IGroupManager>();
            gestionnaire.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Callback((string _, string groupe, CancellationToken _) => Groupes.Add(groupe))
                        .Returns(Task.CompletedTask);
            gestionnaire.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .Callback((string _, string groupe, CancellationToken _) => { Groupes.Remove(groupe); Retraits.Add(groupe); })
                        .Returns(Task.CompletedTask);
            Gestionnaire = gestionnaire.Object;
        }

        public static Connexion Locataire() => new("cnx-58", JetonLocataire());
        public static Connexion Administrateur() => new("cnx-11", JetonAdmin());

        /// <summary>Une instance de hub pour CET appel — comme SignalR, une par invocation.</summary>
        public GpsHub Hub(IGisDbContext ctx)
        {
            var contexte = new Mock<HubCallerContext>();
            contexte.SetupGet(c => c.User).Returns(Jeton);
            contexte.SetupGet(c => c.ConnectionId).Returns(Id);
            contexte.SetupGet(c => c.Items).Returns(Items);

            var clients = new Mock<IHubCallerClients>();
            clients.Setup(c => c.Group(It.IsAny<string>()))
                   .Returns((string groupe) => VersLesClients.Proxy(new[] { groupe }));

            // Sans IServiceScopeFactory ni IHubContext : aucune boucle ne démarre, la
            // passe de réévaluation est appelée à la main, comme la boucle l'appelle.
            return new GpsHub(NullLogger<GpsHub>.Instance, ctx)
            {
                Context = contexte.Object,
                Groups = Gestionnaire,
                Clients = clients.Object
            };
        }

        public AbonnementsConnexion? Registre => Items.Values.OfType<AbonnementsConnexion>().SingleOrDefault();

        public bool Recoit(Envoi envoi) => envoi.Groupes.Any(Groupes.Contains);
    }

    /// <summary>
    /// Tout ce qui, en production, porte une donnée du véhicule : position (chaque trame),
    /// flux « VehiclePosition » des abonnés, alerte, passage de zone, message de tournée.
    /// Les cinq producteurs réels, par le VRAI <see cref="GpsHubService"/>.
    /// </summary>
    private static async Task<List<Envoi>> DiffusionsDuVehiculeAsync(int vehiculeId, int? zoneId)
    {
        var emetteur = new Emetteur();
        var service = new GpsHubService(emetteur.HubContext);
        var trame = new { vehicleId = vehiculeId, latitude = 36.8, longitude = 10.2 };

        await service.SendPositionUpdateAsync(CompanyId, vehiculeId, trame);
        await service.SendVehiclePositionAsync(vehiculeId, trame);
        await service.SendAlertAsync(CompanyId, vehiculeId, new { vehicleId = vehiculeId, type = "overspeed" });
        if (zoneId.HasValue)
            await service.SendGeofenceEventAsync(zoneId.Value, new { geofenceId = zoneId.Value, vehicleId = vehiculeId, eventType = "entry" });
        await DiffusionTournees.EnvoyerAsync(emetteur.HubContext,
            new Tour { Id = 900 + vehiculeId, CompanyId = CompanyId, VehicleId = vehiculeId, Name = "Livraison Kap Pharma — Radès" },
            "TourStatusChanged", new { tourId = 900 + vehiculeId, tourName = "Livraison Kap Pharma — Radès" });

        emetteur.Envois.Should().HaveCount(zoneId.HasValue ? 5 : 4);
        return emetteur.Envois;
    }

    private static async Task RetirerAffectationAsync(TestGisDbContext ctx, int userId, int vehiculeId)
    {
        ctx.UserVehicles.Remove(await ctx.UserVehicles.SingleAsync(uv => uv.UserId == userId && uv.VehicleId == vehiculeId));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
    }

    // ───────── C1 : les abonnements explicites suivent la portée ─────────

    [Fact]
    public async Task C1_abonne_au_vehicule_A_puis_A_retire_la_reevaluation_ne_laisse_plus_rien_passer_de_A()
    {
        using var ctx = await ParcAsync();
        var locataire = Connexion.Locataire();

        await locataire.Hub(ctx).OnConnectedAsync();
        await locataire.Hub(ctx).SubscribeToVehicle(VehiculeA);
        await locataire.Hub(ctx).SubscribeToGeofence(ZoneA);
        await locataire.Hub(ctx).SubscribeToVehicle(VehiculeC);
        await locataire.Hub(ctx).SubscribeToGeofence(ZoneC);

        locataire.Registre.Should().NotBeNull("le registre naît à la connexion de tout compte restreint");
        locataire.Registre!.Vehicules().Should().BeEquivalentTo(new[] { VehiculeA, VehiculeC });
        locataire.Registre.Zones().Should().BeEquivalentTo(new[] { ZoneA, ZoneC });

        // Contrôle du montage : AVANT le retrait, chaque diffusion de A l'atteint. Sans
        // ce contrôle, un test « il ne reçoit plus rien » passerait aussi à vide.
        (await DiffusionsDuVehiculeAsync(VehiculeA, ZoneA)).Should().OnlyContain(e => locataire.Recoit(e));

        // Fin de location : l'administrateur retire A à Kap Pharma. L'onglet reste ouvert.
        await RetirerAffectationAsync(ctx, LocataireUserId, VehiculeA);

        // La passe périodique, telle que la boucle l'appelle : portée connue à la
        // connexion, tenant reconstruit depuis le jeton, registre de la connexion.
        var portee = new HashSet<int> { VehiculeA, VehiculeC };
        var (_, retires) = await GpsHub.ReevaluerPorteeAsync(
            ctx, GpsHub.TenantDepuis(locataire.Jeton), locataire.Gestionnaire, locataire.Id,
            CompanyId, portee, CancellationToken.None, locataire.Registre);

        retires.Should().Equal(VehiculeA);

        var apres = await DiffusionsDuVehiculeAsync(VehiculeA, ZoneA);
        foreach (var envoi in apres)
        {
            locataire.Recoit(envoi).Should().BeFalse(
                $"« {envoi.Evenement} » de A vise {string.Join(", ", envoi.Groupes)} : avant ce correctif, "
                + "les groupes vehicle_{{id}} et geofence_{{id}} n'étaient jamais relus, et la connexion "
                + "recevait chaque trame de A jusqu'à la fermeture de l'onglet");
        }

        locataire.Groupes.Should().NotContain(new[]
        {
            GroupesGps.Vehicule(CompanyId, VehiculeA),
            GroupesGps.AbonnementVehicule(VehiculeA),
            GroupesGps.AbonnementZone(ZoneA)
        });
        locataire.Registre.Vehicules().Should().Equal(VehiculeC);
        locataire.Registre.Zones().Should().Equal(ZoneC);

        // Et ce qui reste à lui continue de passer : la passe retire, elle ne rase pas.
        (await DiffusionsDuVehiculeAsync(VehiculeC, ZoneC)).Should().OnlyContain(e => locataire.Recoit(e));
    }

    [Fact]
    public async Task C1_une_zone_devenue_invisible_est_retiree_meme_si_les_vehicules_ne_bougent_pas()
    {
        // Case « Géofences » décochée en cours de session : aucun véhicule ne change,
        // donc aucun diff de portée — c'est le registre qui porte la réévaluation.
        using var ctx = await ParcAsync();
        var locataire = Connexion.Locataire();

        await locataire.Hub(ctx).OnConnectedAsync();
        await locataire.Hub(ctx).SubscribeToVehicle(VehiculeA);
        await locataire.Hub(ctx).SubscribeToGeofence(ZoneA);

        var compte = await ctx.Users.SingleAsync(u => u.Id == LocataireUserId);
        compte.CanGeofences = false;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var (ajoutes, retires) = await GpsHub.ReevaluerPorteeAsync(
            ctx, GpsHub.TenantDepuis(locataire.Jeton), locataire.Gestionnaire, locataire.Id,
            CompanyId, new HashSet<int> { VehiculeA, VehiculeC }, CancellationToken.None, locataire.Registre);

        ajoutes.Should().BeEmpty();
        retires.Should().BeEmpty();
        locataire.Retraits.Should().Equal(new[] { GroupesGps.AbonnementZone(ZoneA) },
            "seule la zone sort : le véhicule A est toujours à lui");
        locataire.Groupes.Should().Contain(GroupesGps.AbonnementVehicule(VehiculeA));
    }

    /// <summary>
    /// Le passage de zone part au groupe de la zone pour CHAQUE véhicule qui la franchit,
    /// avec son identifiant, son nom et sa position — et la diffusion ne connaît que la
    /// zone. Une zone que peut déclencher le véhicule d'un AUTRE client ne se suit donc
    /// pas en direct, même si elle est VISIBLE du locataire (zone de société, ou zone
    /// partagée avec son propre véhicule).
    /// </summary>
    [Fact]
    public async Task C1_une_zone_que_le_vehicule_d_un_autre_client_peut_declencher_ne_se_suit_pas_en_direct()
    {
        using var ctx = await ParcAsync();
        var locataire = Connexion.Locataire();

        await locataire.Hub(ctx).OnConnectedAsync();
        await locataire.Hub(ctx).SubscribeToVehicle(VehiculeB);
        await locataire.Hub(ctx).SubscribeToGeofence(ZoneB);
        await locataire.Hub(ctx).SubscribeToGeofence(ZoneMixte);
        await locataire.Hub(ctx).SubscribeToGeofence(ZoneSansLiaison);

        locataire.Registre!.Vehicules().Should().BeEmpty("un abonnement refusé n'entre pas au registre");
        locataire.Registre.Zones().Should().BeEmpty();
        locataire.Groupes.Should().NotContain(new[]
        {
            GroupesGps.AbonnementVehicule(VehiculeB),
            GroupesGps.AbonnementZone(ZoneB),
            GroupesGps.AbonnementZone(ZoneMixte),
            GroupesGps.AbonnementZone(ZoneSansLiaison)
        });

        // Le passage de B dans la zone partagée ne l'atteint donc pas.
        (await DiffusionsDuVehiculeAsync(VehiculeB, ZoneMixte)).Should().NotContain(e => locataire.Recoit(e));
    }

    [Fact]
    public async Task C1_une_zone_suivie_qu_on_rattache_au_vehicule_d_un_autre_client_est_retiree()
    {
        // Ses véhicules ne bougent pas : c'est la ZONE qui change de périmètre.
        using var ctx = await ParcAsync();
        var locataire = Connexion.Locataire();

        await locataire.Hub(ctx).OnConnectedAsync();
        await locataire.Hub(ctx).SubscribeToGeofence(ZoneA);
        locataire.Groupes.Should().Contain(GroupesGps.AbonnementZone(ZoneA));

        ctx.GeofenceVehicles.Add(new GeofenceVehicle { GeofenceId = ZoneA, VehicleId = VehiculeB });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await GpsHub.ReevaluerPorteeAsync(
            ctx, GpsHub.TenantDepuis(locataire.Jeton), locataire.Gestionnaire, locataire.Id,
            CompanyId, new HashSet<int> { VehiculeA, VehiculeC }, CancellationToken.None, locataire.Registre);

        locataire.Retraits.Should().Equal(new[] { GroupesGps.AbonnementZone(ZoneA) },
            "B peut désormais déclencher la zone : ses passages partiraient au locataire");
        (await DiffusionsDuVehiculeAsync(VehiculeB, ZoneA)).Should().NotContain(e => locataire.Recoit(e));
    }

    [Fact]
    public async Task C1_une_desinscription_sort_du_registre_et_du_groupe()
    {
        using var ctx = await ParcAsync();
        var locataire = Connexion.Locataire();

        await locataire.Hub(ctx).OnConnectedAsync();
        await locataire.Hub(ctx).SubscribeToVehicle(VehiculeA);
        await locataire.Hub(ctx).UnsubscribeFromVehicle(VehiculeA);

        locataire.Registre!.Vehicules().Should().BeEmpty();
        locataire.Groupes.Should().NotContain(GroupesGps.AbonnementVehicule(VehiculeA));
    }

    /// <summary>
    /// TEST JUMEAU — l'administrateur HERTZ, ZÉRO affectation. Portée <c>null</c> : aucune
    /// boucle, donc aucun registre ; et même si on lui en passait un, la passe ne touche à
    /// RIEN — surtout pas prendre ce « null » pour une portée vide et tout lui retirer.
    /// </summary>
    [Fact]
    public async Task C1_jumeau_l_administrateur_sans_affectation_garde_ses_abonnements()
    {
        using var ctx = await ParcAsync();
        var admin = Connexion.Administrateur();

        await admin.Hub(ctx).OnConnectedAsync();
        await admin.Hub(ctx).SubscribeToVehicle(VehiculeB);
        await admin.Hub(ctx).SubscribeToGeofence(ZoneSansLiaison);

        admin.Registre.Should().BeNull("portée null : pas de réévaluation, rien à tenir");

        var registre = new AbonnementsConnexion();
        registre.AjouterVehicule(VehiculeB);
        registre.AjouterZone(ZoneSansLiaison);
        await GpsHub.ReevaluerPorteeAsync(
            ctx, GpsHub.TenantDepuis(admin.Jeton), admin.Gestionnaire, admin.Id,
            CompanyId, new HashSet<int>(), CancellationToken.None, registre);

        admin.Retraits.Should().BeEmpty();
        (await DiffusionsDuVehiculeAsync(VehiculeB, ZoneSansLiaison)).Should().OnlyContain(e => admin.Recoit(e),
            "il voit tout le parc, exactement comme avant");
    }

    // ───────── C2 : les messages de tournée ne partent plus au groupe société ─────────

    private static async Task<(Connexion Locataire, Connexion Admin)> ConnexionsAsync(TestGisDbContext ctx)
    {
        var locataire = Connexion.Locataire();
        var admin = Connexion.Administrateur();
        await locataire.Hub(ctx).OnConnectedAsync();
        await admin.Hub(ctx).OnConnectedAsync();
        return (locataire, admin);
    }

    private static void TourneeHorsPorteeNAtteintQueLAdministrateur(
        IEnumerable<Envoi> envois, Connexion locataire, Connexion admin, string contexte)
    {
        var liste = envois.ToList();
        liste.Should().NotBeEmpty($"{contexte} : le scénario doit produire au moins un message, sinon il ne prouve rien");

        foreach (var envoi in liste)
        {
            locataire.Recoit(envoi).Should().BeFalse(
                $"{contexte} : « {envoi.Evenement} » d'une tournée du véhicule B vise {string.Join(", ", envoi.Groupes)} — "
                + "le locataire n'a pas B, il ne doit apprendre ni le nom de la tournée ni ses étapes");
            admin.Recoit(envoi).Should().BeTrue($"{contexte} : l'administrateur voit tout le parc");
            envoi.Groupes.Should().NotContain(GroupesGps.Societe(CompanyId));
        }
    }

    [Fact]
    public async Task C2_une_tournee_hors_portee_n_atteint_pas_le_locataire_l_administrateur_la_recoit()
    {
        using var ctx = await ParcAsync();
        var (locataire, admin) = await ConnexionsAsync(ctx);
        var emetteur = new Emetteur();

        await DiffusionTournees.EnvoyerAsync(emetteur.HubContext,
            new Tour { Id = 1, CompanyId = CompanyId, VehicleId = VehiculeB, Name = "Livraison Carthage — Radès" },
            "TourStatusChanged", new { tourId = 1, tourName = "Livraison Carthage — Radès" });
        TourneeHorsPorteeNAtteintQueLAdministrateur(emetteur.Envois, locataire, admin, "porte commune");

        // Et sa propre tournée lui parvient toujours.
        var siennes = new Emetteur();
        await DiffusionTournees.EnvoyerAsync(siennes.HubContext,
            new Tour { Id = 2, CompanyId = CompanyId, VehicleId = VehiculeA, Name = "Livraison Kap Pharma" },
            "TourStatusChanged", new { tourId = 2 });
        siennes.Envois.Should().ContainSingle().Which.Should().Match<Envoi>(e => locataire.Recoit(e) && admin.Recoit(e));
    }

    [Fact]
    public async Task C2_une_tournee_sans_vehicule_ne_part_qu_aux_administrateurs()
    {
        using var ctx = await ParcAsync();
        var (locataire, admin) = await ConnexionsAsync(ctx);

        var groupes = GroupesGps.Tournee(new Tour { Id = 3, CompanyId = CompanyId, VehicleId = 0 });

        groupes.Should().Equal(GroupesGps.Flotte(CompanyId));
        groupes.Any(locataire.Groupes.Contains).Should().BeFalse();
        groupes.Any(admin.Groupes.Contains).Should().BeTrue();
    }

    /// <summary>
    /// Le VRAI cycle du moniteur : deux tournées planifiées arrivent à leur heure et
    /// démarrent toutes seules (« TourStatusChanged », avec le NOM de la tournée).
    /// </summary>
    [Fact]
    public async Task C2_le_moniteur_de_tournees_ne_diffuse_plus_au_groupe_societe()
    {
        TourMonitoringService.ForgetInMemoryState();
        using var ctx = await ParcAsync();
        var (locataire, admin) = await ConnexionsAsync(ctx);

        var depart = DateTime.UtcNow.AddMinutes(-10);
        ctx.Tours.AddRange(TourneePlanifiee(7101, VehiculeB, "Livraison Carthage — Radès", depart),
                           TourneePlanifiee(7102, VehiculeA, "Livraison Kap Pharma — Radès", depart));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var emetteur = new Emetteur();
        var redis = new Mock<IRedisCacheService>();
        redis.Setup(r => r.GetPositionAsync(It.IsAny<string>())).ReturnsAsync((VehiclePositionCache?)null);
        var moniteur = new TourMonitoringService(NullLogger<TourMonitoringService>.Instance, Mock.Of<IServiceProvider>(), redis.Object);

        await moniteur.RunCycleAsync(ctx, emetteur.HubContext, Mock.Of<INotificationService>(), CancellationToken.None);

        TourneeHorsPorteeNAtteintQueLAdministrateur(
            emetteur.Envois.Where(e => e.TourId == 7101), locataire, admin, "TourMonitoringService");

        var siennes = emetteur.Envois.Where(e => e.TourId == 7102).ToList();
        siennes.Should().NotBeEmpty();
        siennes.Should().OnlyContain(e => locataire.Recoit(e), "sa tournée, sur son véhicule, lui parvient toujours");
    }

    private static Tour TourneePlanifiee(int id, int vehiculeId, string nom, DateTime depart, int? fiche = null)
    {
        var tour = new Tour
        {
            Id = id, CompanyId = CompanyId, Name = nom, VehicleId = vehiculeId, DriverId = fiche,
            Status = "planned", ScheduledStartTime = depart, SentAt = fiche.HasValue ? depart.AddHours(-1) : null,
            EstimatedDurationMinutes = 600
        };
        tour.Waypoints.Add(new TourWaypoint { Id = id * 10, SequenceOrder = 0, Type = "origin", Name = "Dépôt",
            Latitude = 36.80, Longitude = 10.18, EstimatedArrivalTime = depart, WaypointStatus = "pending" });
        tour.Waypoints.Add(new TourWaypoint { Id = id * 10 + 1, SequenceOrder = 1, Type = "destination", Name = "Radès",
            Latitude = 36.77, Longitude = 10.27, EstimatedArrivalTime = depart.AddHours(10), WaypointStatus = "pending" });
        return tour;
    }

    /// <summary>
    /// Le VRAI contrôleur de l'application chauffeur : « ouverte sur le téléphone » puis
    /// « Je pars », sur une tournée du véhicule B.
    /// </summary>
    [Fact]
    public async Task C2_l_application_chauffeur_ne_diffuse_plus_au_groupe_societe()
    {
        using var ctx = await ParcAsync();
        var (locataire, admin) = await ConnexionsAsync(ctx);

        var tour = TourneePlanifiee(7201, VehiculeB, "Livraison Carthage — Radès", DateTime.UtcNow.AddMinutes(-5), Fiche);
        ctx.Tours.Add(tour);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var emetteur = new Emetteur();
        var (tenant, http) = await RequeteAsync(JetonDeProduction.Principal(ChauffeurUserId, CompanyId, "Chauffeur"));
        var telephone = new DriverAppController(ctx, tenant, Mock.Of<IRedisCacheService>(),
            Mock.Of<INotificationService>(), emetteur.HubContext, NullLogger<DriverAppController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        (await telephone.Opened(7201)).Should().BeOfType<NoContentResult>();
        ctx.ChangeTracker.Clear();
        await telephone.Depart(7201, 72010, null);

        emetteur.Envois.Select(e => e.Evenement).Should().Contain(new[] { "TourOpened", "TourStatusChanged" });
        TourneeHorsPorteeNAtteintQueLAdministrateur(emetteur.Envois, locataire, admin, "DriverAppController");
    }

    // ───────── C3 : l'historique des commandes boîtier ne nomme plus un tiers ─────────

    /// <summary>Vrai GisDbContext en mémoire : la route filtre DANS le contrôleur.</summary>
    private sealed class ContexteControleur : GisDbContext
    {
        public ContexteControleur(ICurrentTenantService tenant)
            : base(new DbContextOptionsBuilder<GisDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString(), b => b.EnableNullChecks(false))
                    .Options,
                tenant) { }

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
    public async Task C3_l_historique_des_commandes_ne_rend_plus_le_nom_d_un_expediteur_d_une_autre_societe()
    {
        const int Boitier = 21;
        var (tenant, http) = await RequeteAsync(JetonAdmin());
        using var ctx = new ContexteControleur(tenant);

        ctx.Users.AddRange(
            new User { Id = AdminUserId, CompanyId = CompanyId, Email = "admin@hertz.tn", FirstName = "Admin", LastName = "Hertz" },
            new User { Id = AutreSocieteUserId, CompanyId = AutreSociete, Email = "slim@plateforme.tn", FirstName = "Slim", LastName = "Plateforme" });
        ctx.GpsDevices.Add(new GpsDevice { Id = Boitier, DeviceUid = "IMEI-21", CompanyId = CompanyId, Status = "assigned" });
        ctx.DeviceCommands.AddRange(
            new DeviceCommand { Id = 1, DeviceId = Boitier, UserId = AdminUserId, CommandType = "GO", CommandText = "AJ+GO#9999", Status = "sent", CompanyId = CompanyId, CreatedAt = new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc) },
            new DeviceCommand { Id = 2, DeviceId = Boitier, UserId = AutreSocieteUserId, CommandType = "GO", CommandText = "AJ+GO#9999", Status = "sent", CompanyId = CompanyId, CreatedAt = new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc) },
            new DeviceCommand { Id = 3, DeviceId = Boitier, UserId = 0, CommandType = "GO", CommandText = "AJ+GO#9999", Status = "sent", CompanyId = CompanyId, CreatedAt = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc) });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        (await ctx.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == AutreSocieteUserId)).CompanyId
            .Should().Be(AutreSociete, "contrôle du montage : l'expéditeur est bien d'une autre société");

        var gps = new GpsController(ctx, null!, null!, Mock.Of<IRedisCacheService>(), null!, tenant)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };

        var resultat = (await gps.GetDeviceCommands(Boitier)).Should().BeOfType<OkObjectResult>().Subject;
        var noms = JsonDocument.Parse(JsonSerializer.Serialize(resultat.Value)).RootElement.EnumerateArray()
            .ToDictionary(c => c.GetProperty("Id").GetInt32(), c => c.GetProperty("userName").GetString());

        noms.Should().HaveCount(3, "l'historique garde TOUTES les commandes — seul le nom est borné");
        noms[1].Should().Be("Admin Hertz", "un expéditeur de la société garde son nom");
        noms[2].Should().Be("Système",
            "IgnoreQueryFilters levait aussi le filtre société : le nom d'un compte d'une AUTRE société sortait tel quel");
        noms[3].Should().Be("Système");
        noms.Values.Should().NotContain(n => n!.Contains("Plateforme"));
    }

    // ───────── C4 : « en train d'écrire » borné à la société ─────────

    [Fact]
    public async Task C4_l_indicateur_de_saisie_ne_sort_pas_de_la_societe()
    {
        using var ctx = await ParcAsync();
        var locataire = Connexion.Locataire();

        await locataire.Hub(ctx).ChatTyping(AutreSocieteUserId);
        await locataire.Hub(ctx).ChatTyping(9_999);
        locataire.VersLesClients.Envois.Should().BeEmpty(
            "le destinataire était accepté toutes sociétés confondues — et le filtre société de "
            + "GisDbContext ne protège rien sur une trame WebSocket, le tenant y est vierge");

        await locataire.Hub(ctx).ChatTyping(AdminUserId);
        var envoi = locataire.VersLesClients.Envois.Should().ContainSingle().Subject;
        envoi.Evenement.Should().Be("ChatTyping");
        envoi.Groupes.Should().Equal($"user_{AdminUserId}");
        envoi.Message!.GetType().GetProperty("SenderId")!.GetValue(envoi.Message).Should().Be(LocataireUserId,
            "l'émetteur vient du jeton (« sub » remappé), pas d'un paramètre");
    }

    /// <summary>TEST JUMEAU — l'administrateur n'a pas plus de droit hors de sa société.</summary>
    [Fact]
    public async Task C4_jumeau_l_administrateur_ne_sort_pas_non_plus_de_sa_societe()
    {
        using var ctx = await ParcAsync();
        var admin = Connexion.Administrateur();

        await admin.Hub(ctx).ChatTyping(AutreSocieteUserId);
        admin.VersLesClients.Envois.Should().BeEmpty();

        await admin.Hub(ctx).ChatTyping(LocataireUserId);
        admin.VersLesClients.Envois.Should().ContainSingle()
            .Which.Groupes.Should().Equal($"user_{LocataireUserId}");
    }
}
