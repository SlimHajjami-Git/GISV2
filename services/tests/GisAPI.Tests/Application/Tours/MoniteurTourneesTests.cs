using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Hubs;
using GisAPI.Services;
using GisAPI.Services.Tours;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Tours;

/// <summary>
/// Le VRAI cycle de <see cref="TourMonitoringService"/> (RunCycleAsync) rejoué sur une base
/// SQLite : ce qui doit survivre à un redémarrage de l'API (alertes déjà envoyées, début
/// d'une coupure de suivi) ne peut vivre qu'en base, et le choix de la source dépend des
/// vraies trames du boîtier (relecture du 21/09/2026 : F9, F12, F16, F20).
///
/// Le service de notifications est simulé mais écrit, comme le vrai, sa ligne dans le
/// même contexte : c'est elle que l'anti-doublon relit.
/// </summary>
public class MoniteurTourneesTests
{
    private const int CompanyId = 8;
    private const int Admin = 1;
    private const int AncienEnvoyeur = 3;       // a envoyé la tournée, compte désactivé depuis
    private const int Vehicule = 50;
    private const int Boitier = 950;
    private const int Fiche = 40;
    private const double OrigLat = 36.80, OrigLon = 10.18;
    private const double StopLat = 36.40, StopLon = 10.60;
    private const double DestLat = 35.82, DestLon = 10.63;

    public MoniteurTourneesTests() => TourMonitoringService.ForgetInMemoryState();

    private sealed class Banc
    {
        public required TestGisDbContext Ctx { get; init; }
        public required TourMonitoringService Moniteur { get; init; }
        public required Mock<INotificationService> Notifs { get; init; }
        public required Mock<IClientProxy> Diffusion { get; init; }
        public required IHubContext<GpsHub> Hub { get; init; }

        public Task CycleAsync() => Moniteur.RunCycleAsync(Ctx, Hub, Notifs.Object, CancellationToken.None);

        public void Verifier(int userId, string type, Times fois) =>
            Notifs.Verify(n => n.CreateAndSendAsync(CompanyId, userId, type, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()), fois);

        public async Task<Tour> TourneeAsync(int id)
        {
            Ctx.ChangeTracker.Clear();
            return await Ctx.Tours.AsNoTracking().Include(t => t.Waypoints).SingleAsync(t => t.Id == id);
        }
    }

    private static async Task<Banc> BancAsync(bool avecBoitier = true)
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Societes.Add(TestDataBuilder.CreateSociete(id: CompanyId));
        ctx.Roles.Add(new Role { Id = 1, Name = "Administrateur", SocieteId = CompanyId, IsCompanyAdmin = true });
        ctx.Roles.Add(new Role { Id = 2, Name = "Opérateur", SocieteId = CompanyId });
        var admin = TestDataBuilder.CreateUser(id: Admin, companyId: CompanyId, email: "admin@test.com");
        admin.RoleId = 1;
        var ancien = TestDataBuilder.CreateUser(id: AncienEnvoyeur, companyId: CompanyId, email: "parti@test.com");
        ancien.RoleId = 2;
        ancien.Status = "inactive";
        ctx.Users.AddRange(admin, ancien);
        ctx.Vehicles.Add(new Vehicle { Id = Vehicule, Name = "Camion 50", Plate = "150 TU 50", CompanyId = CompanyId, GpsDeviceId = avecBoitier ? Boitier : null });
        if (avecBoitier) ctx.GpsDevices.Add(new GpsDevice { Id = Boitier, DeviceUid = "IMEI950", CompanyId = CompanyId });
        ctx.Drivers.Add(new Driver { Id = Fiche, CompanyId = CompanyId, UserId = 70, FirstName = "Ali", LastName = "B" });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        // Comme le vrai NotificationService : la ligne est écrite dans le MÊME contexte.
        var notifs = new Mock<INotificationService>();
        notifs.Setup(n => n.CreateAndSendAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<Dictionary<string, object>?>(), It.IsAny<CancellationToken>()))
            .Callback((int companyId, int userId, string type, string title, string message, string priority,
                string? refType, int? refId, string? url, Dictionary<string, object>? _, CancellationToken _) =>
            {
                ctx.Notifications.Add(new Notification
                {
                    CompanyId = companyId, UserId = userId, Type = type, Title = title, Message = message,
                    Priority = priority, ReferenceType = refType, ReferenceId = refId, ActionUrl = url
                });
                ctx.SaveChanges();
            })
            .ReturnsAsync(new Notification());

        var proxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(proxy.Object);
        // Depuis le cloisonnement HERTZ, l'écart de tournée — le seul message de ce
        // service qui porte la POSITION du véhicule — vise DEUX groupes (flotte +
        // véhicule) via Clients.Groups. Sans ce montage, l'appel rendait null et le
        // cycle levait une NullReferenceException AVALÉE par la boucle : les tests de
        // suivi se retrouvaient avec un TrackingSource jamais écrit.
        clients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(proxy.Object);
        var hub = new Mock<IHubContext<GpsHub>>();
        hub.Setup(h => h.Clients).Returns(clients.Object);

        var redis = new Mock<IRedisCacheService>();
        redis.Setup(r => r.GetPositionAsync(It.IsAny<string>())).ReturnsAsync((VehiclePositionCache?)null);

        var moniteur = new TourMonitoringService(NullLogger<TourMonitoringService>.Instance,
            Mock.Of<IServiceProvider>(), redis.Object);
        return new Banc { Ctx = ctx, Moniteur = moniteur, Notifs = notifs, Diffusion = proxy, Hub = hub.Object };
    }

    /// <summary>Tournée à trois étapes (origine, client, destination), échéances lointaines.</summary>
    private static Tour Tournee(int id, string statut, DateTime depart, int? fiche = null, DateTime? envoyeeLe = null)
    {
        var tour = new Tour
        {
            Id = id, CompanyId = CompanyId, Name = $"Tournée {id}", VehicleId = Vehicule, DriverId = fiche,
            Status = statut, ScheduledStartTime = depart, SentAt = envoyeeLe, SentByUserId = envoyeeLe.HasValue ? AncienEnvoyeur : (int?)null,
            ActualStartTime = statut == "in_progress" ? depart : null, EstimatedDurationMinutes = 600
        };
        tour.Waypoints.Add(new TourWaypoint { SequenceOrder = 0, Type = "origin", Name = "Dépôt", Latitude = OrigLat, Longitude = OrigLon,
            EstimatedArrivalTime = depart, IsCompleted = statut == "in_progress", WaypointStatus = statut == "in_progress" ? "completed" : "pending",
            ActualArrivalTime = statut == "in_progress" ? depart : null });
        tour.Waypoints.Add(new TourWaypoint { SequenceOrder = 1, Type = "waypoint", Name = "Client A", Latitude = StopLat, Longitude = StopLon,
            EstimatedArrivalTime = depart.AddHours(8), WaypointStatus = "pending" });
        tour.Waypoints.Add(new TourWaypoint { SequenceOrder = 2, Type = "destination", Name = "Sousse", Latitude = DestLat, Longitude = DestLon,
            EstimatedArrivalTime = depart.AddHours(10), WaypointStatus = "pending" });
        return tour;
    }

    private static GpsPosition Trame(DateTime at, double lat, double lon, double vitesse = 0, bool contact = false) =>
        new() { DeviceId = Boitier, RecordedAt = at, Latitude = lat, Longitude = lon, SpeedKph = vitesse, IgnitionOn = contact, IsValid = true };

    // ── F12 : « non démarrée », une seule alerte, même après un redémarrage ─────

    [Fact]
    public async Task L_alerte_tournee_non_demarree_ne_repart_pas_apres_un_redemarrage_de_l_api()
    {
        var b = await BancAsync();
        var now = DateTime.UtcNow;
        b.Ctx.Tours.Add(Tournee(20, "planned", now.AddMinutes(-20), Fiche, envoyeeLe: now.AddHours(-1)));
        await b.Ctx.SaveChangesAsync();
        b.Ctx.ChangeTracker.Clear();

        await b.CycleAsync();
        b.Verifier(Admin, "tour_not_started", Times.Once());

        TourMonitoringService.ForgetInMemoryState();   // déploiement de l'API
        b.Ctx.ChangeTracker.Clear();
        await b.CycleAsync();

        b.Verifier(Admin, "tour_not_started", Times.Once());
        (await b.TourneeAsync(20)).Status.Should().Be("planned", "elle attend le « Je pars » du chauffeur");
    }

    [Fact]
    public async Task Apres_un_renvoi_le_nouveau_retard_est_signale_sans_attendre_un_redemarrage()
    {
        // R9c : le cache ne retenait que l'id de la tournée. Renvoyée (SentAt reposé) avec un
        // départ décalé, toujours pas partie : l'alerte ne repartait qu'après un déploiement.
        var b = await BancAsync();
        var now = DateTime.UtcNow;
        b.Ctx.Tours.Add(Tournee(29, "planned", now.AddMinutes(-20), Fiche, envoyeeLe: now.AddHours(-1)));
        await b.Ctx.SaveChangesAsync();
        b.Ctx.ChangeTracker.Clear();

        await b.CycleAsync();
        b.Verifier(Admin, "tour_not_started", Times.Once());

        // Le gestionnaire appelle le chauffeur, décale le départ et clique « Renvoyer ».
        // Chronologie réelle : première alerte il y a 10 min, renvoi il y a 1 min.
        var premiere = await b.Ctx.Notifications.SingleAsync(n => n.Type == "tour_not_started");
        premiere.CreatedAt = DateTime.UtcNow.AddMinutes(-10);
        var tour = await b.Ctx.Tours.SingleAsync(t => t.Id == 29);
        tour.ScheduledStartTime = now.AddMinutes(-16);
        tour.SentAt = DateTime.UtcNow.AddMinutes(-1);
        await b.Ctx.SaveChangesAsync();
        b.Ctx.ChangeTracker.Clear();

        await b.CycleAsync();
        b.Verifier(Admin, "tour_not_started", Times.Exactly(2));

        // Et pas une troisième pour ce même envoi, redémarrage ou non.
        b.Ctx.ChangeTracker.Clear();
        await b.CycleAsync();
        TourMonitoringService.ForgetInMemoryState();
        b.Ctx.ChangeTracker.Clear();
        await b.CycleAsync();
        b.Verifier(Admin, "tour_not_started", Times.Exactly(2));
    }

    [Fact]
    public async Task L_expediteur_desactive_ne_recoit_plus_les_alertes_de_la_tournee()
    {
        // F9 : tours."SentByUserId" était ajouté à l'audience sans contrôle.
        var b = await BancAsync();
        var now = DateTime.UtcNow;
        b.Ctx.Tours.Add(Tournee(21, "planned", now.AddMinutes(-20), Fiche, envoyeeLe: now.AddHours(-1)));
        await b.Ctx.SaveChangesAsync();
        b.Ctx.ChangeTracker.Clear();

        await b.CycleAsync();

        b.Verifier(Admin, "tour_not_started", Times.Once());
        b.Verifier(AncienEnvoyeur, "tour_not_started", Times.Never());
    }

    // ── F16 : état réel du boîtier, tournées non suivies ───────────────────────

    [Fact]
    public async Task Un_arret_contact_coupe_de_25_min_laisse_le_boitier_source_du_suivi()
    {
        // HERTZ : une trame toutes les 30 min à l'arrêt. Le contact était forcé à « mis »
        // (seuil 3 min) : « sans source » à chaque livraison, puis « Suivi interrompu ».
        var b = await BancAsync();
        var now = DateTime.UtcNow;
        b.Ctx.Tours.Add(Tournee(22, "in_progress", now.AddHours(-1)));
        b.Ctx.GpsPositions.Add(Trame(now.AddMinutes(-25), StopLat + 0.2, StopLon, vitesse: 0, contact: false));
        await b.Ctx.SaveChangesAsync();
        b.Ctx.ChangeTracker.Clear();

        await b.CycleAsync();

        (await b.TourneeAsync(22)).TrackingSource.Should().Be(TrackingSourceSelector.Device);
        b.Verifier(Admin, "tour_tracking_lost", Times.Never());
    }

    private static async Task DerniereCommunicationAsync(Banc b, DateTime? quand)
    {
        var boitier = await b.Ctx.GpsDevices.SingleAsync(d => d.Id == Boitier);
        boitier.LastCommunication = quand;
        await b.Ctx.SaveChangesAsync();
        b.Ctx.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Un_battement_ecreme_a_l_arret_garde_le_boitier_vivant_au_dela_de_35_min_de_trame_stockee()
    {
        // R2c : l'ingest n'écrit qu'une trame toutes les 30 min d'un boîtier arrêté contact
        // coupé ; le battement de 10:29:59 (1 s trop tôt) n'est pas stocké, seule
        // last_communication avance. Dernière trame stockée il y a 50 min : « sans source »,
        // puis « Suivi interrompu » en pleine livraison, alors que le boîtier parlait.
        var b = await BancAsync();
        var now = DateTime.UtcNow;
        var tour = Tournee(30, "in_progress", now.AddHours(-2));
        tour.TrackingSource = TrackingSourceSelector.None;
        tour.TrackingSourceSince = now.AddMinutes(-15);
        b.Ctx.Tours.Add(tour);
        b.Ctx.GpsPositions.Add(Trame(now.AddMinutes(-50), StopLat, StopLon, vitesse: 0, contact: false));
        await b.Ctx.SaveChangesAsync();
        await DerniereCommunicationAsync(b, now.AddMinutes(-20));

        await b.CycleAsync();

        (await b.TourneeAsync(30)).TrackingSource.Should().Be(TrackingSourceSelector.Device);
        b.Verifier(Admin, "tour_tracking_lost", Times.Never());
    }

    [Fact]
    public async Task Une_communication_recente_ne_ranime_pas_un_boitier_reparti_sans_position_valide()
    {
        // Contrôle inverse : une trame contact MIS (sans position valide) a été stockée depuis
        // la dernière trame contact coupé — le véhicule roule, son GPS ne suit plus. Ses
        // communications ne prouvent pas que la tournée est suivie.
        var b = await BancAsync();
        var now = DateTime.UtcNow;
        b.Ctx.Tours.Add(Tournee(31, "in_progress", now.AddHours(-2)));
        b.Ctx.GpsPositions.Add(Trame(now.AddMinutes(-40), StopLat, StopLon, vitesse: 0, contact: false));
        b.Ctx.GpsPositions.Add(new GpsPosition { DeviceId = Boitier, RecordedAt = now.AddMinutes(-20), Latitude = 0, Longitude = 0,
            IgnitionOn = true, IsValid = false });
        await b.Ctx.SaveChangesAsync();
        await DerniereCommunicationAsync(b, now.AddMinutes(-1));

        await b.CycleAsync();

        (await b.TourneeAsync(31)).TrackingSource.Should().Be(TrackingSourceSelector.None);
    }

    [Fact]
    public async Task Une_tournee_partie_il_y_a_plus_de_12_h_ne_declenche_plus_suivi_interrompu()
    {
        // R2c : une tournée classique oubliée « en cours » relançait l'alerte à chaque
        // nouvelle coupure, nuit après nuit.
        var b = await BancAsync();
        var now = DateTime.UtcNow;
        var tour = Tournee(32, "in_progress", now.AddHours(-13));
        tour.TrackingSource = TrackingSourceSelector.None;
        tour.TrackingSourceSince = now.AddMinutes(-15);
        b.Ctx.Tours.Add(tour);
        await b.Ctx.SaveChangesAsync();
        b.Ctx.ChangeTracker.Clear();

        await b.CycleAsync();

        b.Verifier(Admin, "tour_tracking_lost", Times.Never());
        (await b.TourneeAsync(32)).TrackingSource.Should().Be(TrackingSourceSelector.None, "la source reste tenue à jour");
    }

    [Fact]
    public async Task Une_trame_recente_contact_mis_puis_plus_rien_depuis_5_min_bascule_sans_source()
    {
        // Contrôle inverse : le contact RÉEL est lu — mis, 5 min de silence = boîtier muet.
        var b = await BancAsync();
        var now = DateTime.UtcNow;
        b.Ctx.Tours.Add(Tournee(23, "in_progress", now.AddHours(-1)));
        b.Ctx.GpsPositions.Add(Trame(now.AddMinutes(-5), StopLat + 0.2, StopLon, vitesse: 60, contact: true));
        await b.Ctx.SaveChangesAsync();
        b.Ctx.ChangeTracker.Clear();

        await b.CycleAsync();

        (await b.TourneeAsync(23)).TrackingSource.Should().Be(TrackingSourceSelector.None);
    }

    [Theory]
    [InlineData(null)]      // tournée démarrée par ce moniteur
    [InlineData("none")]    // valeur posée avant le correctif
    public async Task Une_tournee_sans_boitier_ni_chauffeur_n_est_pas_suivie_et_ne_declenche_rien(string? sourceAvant)
    {
        // Offre sans GPS, tournée classique : rien n'a jamais dû la suivre.
        var b = await BancAsync(avecBoitier: false);
        var now = DateTime.UtcNow;
        var tour = Tournee(24, "in_progress", now.AddMinutes(-30));
        tour.TrackingSource = sourceAvant;
        tour.TrackingSourceSince = sourceAvant != null ? now.AddMinutes(-25) : null;
        b.Ctx.Tours.Add(tour);
        await b.Ctx.SaveChangesAsync();
        b.Ctx.ChangeTracker.Clear();

        await b.CycleAsync();
        b.Ctx.ChangeTracker.Clear();
        await b.CycleAsync();

        var apres = await b.TourneeAsync(24);
        (apres.TrackingSource, apres.TrackingSourceSince).Should().Be(((string?)null, (DateTime?)null), "« non suivie »");
        b.Verifier(Admin, "tour_tracking_lost", Times.Never());
        b.Diffusion.Verify(p => p.SendCoreAsync("TourTrackingSourceChanged", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task La_coupure_de_suivi_se_compte_en_base_et_n_est_signalee_qu_une_fois_meme_apres_un_redemarrage()
    {
        // Boîtier muet depuis longtemps, source « none » depuis 15 min (persistée) : le
        // compteur en mémoire repartait de zéro à chaque redémarrage et l'alerte suivait.
        var b = await BancAsync();
        var now = DateTime.UtcNow;
        var tour = Tournee(25, "in_progress", now.AddHours(-1));
        tour.TrackingSource = TrackingSourceSelector.None;
        tour.TrackingSourceSince = now.AddMinutes(-15);
        b.Ctx.Tours.Add(tour);
        await b.Ctx.SaveChangesAsync();
        b.Ctx.ChangeTracker.Clear();

        await b.CycleAsync();
        b.Verifier(Admin, "tour_tracking_lost", Times.Once());

        TourMonitoringService.ForgetInMemoryState();
        b.Ctx.ChangeTracker.Clear();
        await b.CycleAsync();

        b.Verifier(Admin, "tour_tracking_lost", Times.Once());
    }

    // ── F20 : confirmation d'une arrivée déclarée ──────────────────────────────

    [Fact]
    public async Task Un_repassage_tardif_ne_confirme_ni_l_origine_ni_une_etape_declaree()
    {
        var b = await BancAsync();
        var now = DateTime.UtcNow;
        var depart = now.AddHours(-3);
        var declaree = now.AddMinutes(-150);
        var tour = Tournee(26, "in_progress", depart, Fiche, envoyeeLe: depart.AddHours(-1));
        var origine = tour.Waypoints.Single(w => w.Type == "origin");
        origine.ArrivalSource = DriverTourRules.SourceDriver;          // « Je pars »
        origine.DriverDepartedAt = depart;
        var etape = tour.Waypoints.Single(w => w.Type == "waypoint");
        DriverTourRules.DeclareArrival(etape, declaree, 40);            // « Je suis arrivé »
        b.Ctx.Tours.Add(tour);
        b.Ctx.GpsPositions.AddRange(
            Trame(now.AddMinutes(-100), StopLat + 0.3, StopLon, vitesse: 70, contact: true),   // en route, loin de tout
            Trame(now.AddMinutes(-30), StopLat, StopLon, vitesse: 0, contact: true),           // repasse chez le client 2 h après
            Trame(now.AddMinutes(-10), OrigLat, OrigLon, vitesse: 0, contact: true));          // retour au dépôt
        await b.Ctx.SaveChangesAsync();
        b.Ctx.ChangeTracker.Clear();

        await b.CycleAsync();

        var apres = await b.TourneeAsync(26);
        var o = apres.Waypoints.Single(w => w.Type == "origin");
        (o.ActualArrivalTime, o.ArrivalSource).Should().Be(((DateTime?)depart, DriverTourRules.SourceDriver),
            "l'heure de départ déclarée n'est pas écrasée par le retour au dépôt");
        var e = apres.Waypoints.Single(w => w.Type == "waypoint");
        (e.ActualArrivalTime, e.ArrivalSource).Should().Be(((DateTime?)declaree, DriverTourRules.SourceDriver),
            "un passage 2 h après la déclaration n'est pas l'arrivée");
    }

    [Fact]
    public async Task Une_arrivee_declaree_est_confirmee_par_une_detection_proche_de_la_declaration()
    {
        var b = await BancAsync();
        var now = DateTime.UtcNow;
        var depart = now.AddHours(-3);
        var declaree = now.AddMinutes(-150);
        var tour = Tournee(27, "in_progress", depart, Fiche, envoyeeLe: depart.AddHours(-1));
        DriverTourRules.DeclareArrival(tour.Waypoints.Single(w => w.Type == "waypoint"), declaree, 40);
        b.Ctx.Tours.Add(tour);
        b.Ctx.GpsPositions.Add(Trame(declaree.AddMinutes(4), StopLat, StopLon, vitesse: 0, contact: true));
        await b.Ctx.SaveChangesAsync();
        b.Ctx.ChangeTracker.Clear();

        await b.CycleAsync();

        var e = (await b.TourneeAsync(27)).Waypoints.Single(w => w.Type == "waypoint");
        e.ArrivalSource.Should().Be(DriverTourRules.SourceDevice);
        e.ActualArrivalTime.Should().BeCloseTo(declaree.AddMinutes(4), TimeSpan.FromSeconds(1), "l'heure détectée fait foi");
    }
}
