using System.Security.Claims;
using FluentAssertions;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Hubs;
using GisAPI.Infrastructure.MultiTenancy;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Confidentialite;

/// <summary>
/// Incident de confidentialité HERTZ — TROISIÈME passe, celle des PRODUCTEURS et des
/// RESTES du hub. Les deux premières ont cloisonné les écrans, les rapports et le hub
/// lui-même ; la relecture adversariale a trouvé que le cloisonnement s'arrêtait au
/// bord du hub :
///
///   • <c>TourMonitoringService</c> diffusait la POSITION GPS d'un véhicule (écart de
///     tournée) au groupe SOCIÉTÉ, que tout le monde rejoint : la refonte des groupes
///     avait fait passer les positions par un groupe PAR VÉHICULE, ce producteur-là
///     n'avait pas suivi ;
///   • <c>OnDisconnectedAsync</c> ne défaisait que « company_{id} », écrit en dur :
///     asymétrique de <c>OnConnectedAsync</c> ;
///   • <c>SendSubscriptionChangedAsync</c> réécrivait le nom du groupe société à côté
///     de <see cref="GroupesGps.Societe"/> ;
///   • la portée temps réel était figée à la CONNEXION : un véhicule RETIRÉ à un
///     utilisateur continuait de lui être diffusé jusqu'à ce qu'il recharge la page.
///
/// PIÈGE COUVERT PARTOUT ICI — la portée a TROIS états : <c>null</c> = administrateur,
/// AUCUN filtre ; liste non vide = ses véhicules ; liste VIDE = il ne voit RIEN. D'où
/// les tests JUMEAUX sur l'administrateur HERTZ (utilisateur 11), qui n'a AUCUNE
/// affectation et doit continuer à tout voir.
/// </summary>
public class DiffusionTempsReelTests
{
    private const int CompanyId = 4;               // HERTZ
    private const int LocataireUserId = 58;        // Kap Pharma, « Operateur », 2 véhicules sur 307
    private const int AdminUserId = 11;            // administrateur HERTZ, ZÉRO affectation

    private const int VehiculeDuLocataire = 1;
    private const int VehiculeDUnAutreClient = 2;
    private const int VehiculeRendu = 3;           // affecté puis RETIRÉ en cours de session

    // ───────────────────────── Montage ─────────────────────────

    /// <summary>
    /// Parc HERTZ réduit : trois véhicules, le locataire n'a que le véhicule 1 (et,
    /// pour le test de réévaluation, le véhicule 3 qu'on lui reprendra).
    /// </summary>
    private static async Task<TestGisDbContext> ParcAsync(params int[] vehiculesDuLocataire)
    {
        var ctx = TestDbContextFactory.Create();

        ctx.Users.AddRange(
            new User { Id = LocataireUserId, CompanyId = CompanyId, Email = "kap@pharma.tn",
                       FirstName = "Kap", LastName = "Pharma", PasswordHash = "x", Status = "active" },
            new User { Id = AdminUserId, CompanyId = CompanyId, Email = "admin@hertz.tn",
                       FirstName = "Admin", LastName = "Hertz", PasswordHash = "x", Status = "active" });

        ctx.Vehicles.AddRange(
            new Vehicle { Id = VehiculeDuLocataire, Name = "Loué Kap Pharma", Plate = "111 TU 1", CompanyId = CompanyId },
            new Vehicle { Id = VehiculeDUnAutreClient, Name = "Loué Carthage", Plate = "222 TU 2", CompanyId = CompanyId },
            new Vehicle { Id = VehiculeRendu, Name = "Loué puis rendu", Plate = "333 TU 3", CompanyId = CompanyId });

        var id = 1;
        foreach (var vehiculeId in vehiculesDuLocataire)
            ctx.UserVehicles.Add(new UserVehicle { Id = id++, UserId = LocataireUserId, VehicleId = vehiculeId });

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return ctx;
    }

    /// <summary>Tenant reconstruit comme le fait le hub : depuis les rôles du JETON.</summary>
    private static ICurrentTenantService Tenant(int userId, params string[] roles)
    {
        var tenant = new CurrentTenantService();
        tenant.SetTenant(CompanyId, userId, "x@hertz.tn", roles, Array.Empty<string>());
        return tenant;
    }

    private sealed record MouvementsDeGroupes(List<string> Ajouts, List<string> Retraits);

    private static (IGroupManager Groupes, MouvementsDeGroupes Mouvements) GestionnaireDeGroupes()
    {
        var mouvements = new MouvementsDeGroupes(new List<string>(), new List<string>());
        var manager = new Mock<IGroupManager>();

        manager.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Callback<string, string, CancellationToken>((_, groupe, _) => mouvements.Ajouts.Add(groupe))
               .Returns(Task.CompletedTask);

        manager.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Callback<string, string, CancellationToken>((_, groupe, _) => mouvements.Retraits.Add(groupe))
               .Returns(Task.CompletedTask);

        return (manager.Object, mouvements);
    }

    private static (GpsHub Hub, MouvementsDeGroupes Mouvements) Hub(
        TestGisDbContext ctx, int userId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("companyId", CompanyId.ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var context = new Mock<HubCallerContext>();
        context.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")));
        context.SetupGet(c => c.ConnectionId).Returns("cnx-1");

        var (groupes, mouvements) = GestionnaireDeGroupes();

        // Hub construit à la main : sans IServiceScopeFactory ni IHubContext, aucune
        // boucle de réévaluation ne démarre (elle est testée à part, sur sa méthode).
        var hub = new GpsHub(NullLogger<GpsHub>.Instance, ctx)
        {
            Context = context.Object,
            Groups = groupes
        };

        return (hub, mouvements);
    }

    /// <summary>Capture les groupes visés par une diffusion (Group et Groups).</summary>
    private static (IHubContext<GpsHub> HubContext, List<string> Groupes) Diffusion()
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

        return (hubContext.Object, groupes);
    }

    // ───────── C1 : l'écart de tournée, dernier producteur de position ─────────

    [Fact]
    public async Task Un_ecart_de_tournee_ne_part_plus_au_groupe_societe()
    {
        var (hubContext, groupes) = Diffusion();
        var tour = new Tour { Id = 77, CompanyId = CompanyId, VehicleId = VehiculeDUnAutreClient, Name = "Sousse → Sfax" };

        await TourMonitoringService.DiffuserEcartDeTourneeAsync(
            hubContext, tour, latitude: 36.8, longitude: 10.18, distFromSegment: 1200, ct: CancellationToken.None);

        groupes.Should().Equal(
            GroupesGps.Flotte(CompanyId),
            GroupesGps.Vehicule(CompanyId, VehiculeDUnAutreClient));

        groupes.Should().NotContain(GroupesGps.Societe(CompanyId),
            "ce message porte vehicleLatitude/vehicleLongitude : au groupe société, il donnait "
            + "la position d'un véhicule loué à un autre client à toute la société");
    }

    [Fact]
    public void L_ecart_de_tournee_emprunte_le_MEME_helper_que_les_positions_et_les_alertes()
    {
        // Le défaut d'origine n'est pas « un mauvais nom de groupe » mais « un nom de
        // groupe réécrit ailleurs ». Une seule règle, partagée : si elle change, elle
        // change pour les trois producteurs à la fois.
        GroupesGps.Diffusion(CompanyId, VehiculeDuLocataire).Should().Equal(
            GroupesGps.Flotte(CompanyId),
            GroupesGps.Vehicule(CompanyId, VehiculeDuLocataire));

        GroupesGps.Diffusion(CompanyId, null).Should().Equal(
            new[] { GroupesGps.Flotte(CompanyId) },
            "sans véhicule connu, la diffusion ne peut atteindre que « tout le parc »");
    }

    // ───────── C2 : la déconnexion, symétrique de la connexion ─────────

    [Fact]
    public async Task A_la_deconnexion_un_restreint_quitte_la_societe_ET_les_groupes_de_ses_vehicules()
    {
        using var ctx = await ParcAsync(VehiculeDuLocataire);
        var (hub, mouvements) = Hub(ctx, LocataireUserId, "Operateur");

        await hub.OnDisconnectedAsync(null);

        mouvements.Retraits.Should().BeEquivalentTo(new[]
        {
            GroupesGps.Societe(CompanyId),
            GroupesGps.Vehicule(CompanyId, VehiculeDuLocataire),
            $"user_{LocataireUserId}"
        }, "la méthode ne défaisait que « company_{id} », écrit en dur : les groupes par "
         + "véhicule créés à la connexion n'étaient jamais nommés ici");

        mouvements.Retraits.Should().NotContain(GroupesGps.Flotte(CompanyId),
            "un restreint n'a jamais rejoint le groupe flotte");
    }

    /// <summary>TEST JUMEAU — l'administrateur sans affectation quitte le groupe FLOTTE.</summary>
    [Fact]
    public async Task A_la_deconnexion_un_administrateur_sans_affectation_quitte_le_groupe_flotte()
    {
        using var ctx = await ParcAsync();
        var (hub, mouvements) = Hub(ctx, AdminUserId, "company_admin");

        await hub.OnDisconnectedAsync(null);

        mouvements.Retraits.Should().BeEquivalentTo(new[]
        {
            GroupesGps.Societe(CompanyId),
            GroupesGps.Flotte(CompanyId),
            $"user_{AdminUserId}"
        });

        mouvements.Retraits.Should().NotContain(g => g.StartsWith($"scope_{CompanyId}_vehicle_"),
            "portée null : il n'a jamais rejoint le moindre groupe par véhicule");
    }

    // ───────── C3 : le message d'abonnement, seul habitant du groupe société ─────────

    /// <summary>
    /// HONNÊTETÉ DU TEST : le correctif C3 ne change RIEN au comportement — le nom
    /// produit en dur était le même que celui du helper. Ce test ne détecte donc pas
    /// la régression d'hier, il VERROUILLE le contrat : si demain le groupe société
    /// est renommé dans <see cref="GroupesGps"/>, un site qui l'aurait réécrit à la
    /// main tomberait ici.
    /// </summary>
    [Fact]
    public async Task Le_message_d_abonnement_vise_le_groupe_societe_par_le_helper()
    {
        var (hubContext, groupes) = Diffusion();
        var service = new GpsHubService(hubContext);

        await service.SendSubscriptionChangedAsync(CompanyId, "suspended");

        groupes.Should().Equal(new[] { GroupesGps.Societe(CompanyId) },
            "suspension/réactivation ne porte aucune donnée de véhicule : c'est le seul "
            + "message qui a encore sa place dans le groupe société");
        groupes.Should().NotContain(GroupesGps.Flotte(CompanyId));
    }

    // ───────── C4 : la portée n'est plus figée à la connexion ─────────

    [Fact]
    public async Task Une_affectation_RETIREE_en_cours_de_session_coupe_la_diffusion_sans_reconnexion()
    {
        // Connexion : le locataire a les véhicules 1 et 3, il est dans leurs deux groupes.
        using var ctx = await ParcAsync(VehiculeDuLocataire, VehiculeRendu);
        var portee = new HashSet<int> { VehiculeDuLocataire, VehiculeRendu };
        var (groupes, mouvements) = GestionnaireDeGroupes();

        // Le véhicule 3 est rendu : la location s'arrête, l'affectation est retirée.
        ctx.UserVehicles.Remove(ctx.UserVehicles.Single(uv => uv.VehicleId == VehiculeRendu));
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var (ajoutes, retires) = await GpsHub.ReevaluerPorteeAsync(
            ctx, Tenant(LocataireUserId, "Operateur"), groupes, "cnx-1", CompanyId, portee, CancellationToken.None);

        retires.Should().Equal(new[] { VehiculeRendu });
        ajoutes.Should().BeEmpty();

        mouvements.Retraits.Should().Equal(new[] { GroupesGps.Vehicule(CompanyId, VehiculeRendu) },
            "sans cette passe, le véhicule rendu continuait de lui être diffusé jusqu'au "
            + "rechargement de la page — un onglet de supervision reste ouvert des heures");
        mouvements.Ajouts.Should().BeEmpty();

        portee.Should().Equal(new[] { VehiculeDuLocataire });
    }

    [Fact]
    public async Task Une_affectation_AJOUTEE_en_cours_de_session_ouvre_la_diffusion_sans_reconnexion()
    {
        using var ctx = await ParcAsync(VehiculeDuLocataire);
        var portee = new HashSet<int> { VehiculeDuLocataire };
        var (groupes, mouvements) = GestionnaireDeGroupes();

        ctx.UserVehicles.Add(new UserVehicle { Id = 50, UserId = LocataireUserId, VehicleId = VehiculeRendu });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var (ajoutes, retires) = await GpsHub.ReevaluerPorteeAsync(
            ctx, Tenant(LocataireUserId, "Operateur"), groupes, "cnx-1", CompanyId, portee, CancellationToken.None);

        ajoutes.Should().Equal(new[] { VehiculeRendu });
        retires.Should().BeEmpty();
        mouvements.Ajouts.Should().Equal(new[] { GroupesGps.Vehicule(CompanyId, VehiculeRendu) });
        portee.Should().BeEquivalentTo(new[] { VehiculeDuLocataire, VehiculeRendu });
    }

    [Fact]
    public async Task Une_portee_devenue_VIDE_retire_tous_les_groupes_et_n_en_ouvre_aucun()
    {
        // Liste VIDE ≠ null : il ne voit plus RIEN, il ne bascule pas sur tout le parc.
        using var ctx = await ParcAsync(VehiculeDuLocataire);
        var portee = new HashSet<int> { VehiculeDuLocataire };
        var (groupes, mouvements) = GestionnaireDeGroupes();

        ctx.UserVehicles.RemoveRange(ctx.UserVehicles.ToList());
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        await GpsHub.ReevaluerPorteeAsync(
            ctx, Tenant(LocataireUserId, "Operateur"), groupes, "cnx-1", CompanyId, portee, CancellationToken.None);

        mouvements.Retraits.Should().Equal(new[] { GroupesGps.Vehicule(CompanyId, VehiculeDuLocataire) });
        mouvements.Ajouts.Should().BeEmpty("surtout pas le groupe flotte");
        portee.Should().BeEmpty();
    }

    /// <summary>
    /// TEST JUMEAU — l'administrateur. Sa portée est <c>null</c> : la passe ne doit
    /// toucher à RIEN, et surtout pas fabriquer une inscription au groupe flotte à
    /// partir d'un « null » pris pour une liste vide.
    /// </summary>
    [Fact]
    public async Task La_reevaluation_ne_touche_a_rien_pour_un_administrateur()
    {
        using var ctx = await ParcAsync();
        var portee = new HashSet<int>();
        var (groupes, mouvements) = GestionnaireDeGroupes();

        var (ajoutes, retires) = await GpsHub.ReevaluerPorteeAsync(
            ctx, Tenant(AdminUserId, "company_admin"), groupes, "cnx-1", CompanyId, portee, CancellationToken.None);

        ajoutes.Should().BeEmpty();
        retires.Should().BeEmpty();
        mouvements.Ajouts.Should().BeEmpty();
        mouvements.Retraits.Should().BeEmpty();
    }

    // ───────── C5 : la limite de /api/dashboard/activity ─────────

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(20, 20)]
    [InlineData(200, 200)]
    [InlineData(1_000_000, 200)]
    [InlineData(int.MinValue, 1)]
    public void La_limite_de_l_activite_recente_est_bornee(int recu, int attendu)
    {
        DashboardService.BornerLimiteActivite(recu).Should().Be(attendu);
    }

    [Fact]
    public async Task Une_limite_negative_ne_fait_plus_de_LIMIT_negatif()
    {
        // `?limit=-1` descendait brut jusqu'aux trois Take() du service : PostgreSQL
        // refuse « LIMIT must not be negative » — une 500 à la portée de n'importe qui.
        using var ctx = await ParcAsync(VehiculeDuLocataire);

        ctx.GpsAlerts.AddRange(
            new GpsAlert { Id = 1, CompanyId = CompanyId, VehicleId = VehiculeDuLocataire, Type = "overspeed",
                           Severity = "warning", Message = "Alerte 1", Timestamp = new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc) },
            new GpsAlert { Id = 2, CompanyId = CompanyId, VehicleId = VehiculeDuLocataire, Type = "overspeed",
                           Severity = "warning", Message = "Alerte 2", Timestamp = new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc) });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var flux = await DashboardService.RecentActivityAsync(
            ctx, CompanyId, LocataireUserId, new List<int> { VehiculeDuLocataire }, -1, CancellationToken.None);

        flux.Should().HaveCount(1, "une limite négative est ramenée au minimum, pas laissée passer");
    }

    /// <summary>
    /// HONNÊTETÉ DU TEST : il ne détecte pas le défaut de volume (ce serait un test de
    /// performance). Il garde le plafond de NE PAS devenir une porte dérobée : borner
    /// le nombre de lignes ne doit pas élargir la portée d'un cran.
    /// </summary>
    [Fact]
    public async Task Une_limite_demesuree_est_plafonnee_sans_changer_le_cloisonnement()
    {
        using var ctx = await ParcAsync(VehiculeDuLocataire);

        ctx.GpsAlerts.AddRange(
            new GpsAlert { Id = 1, CompanyId = CompanyId, VehicleId = VehiculeDuLocataire, Type = "overspeed",
                           Severity = "warning", Message = "La sienne", Timestamp = new DateTime(2026, 9, 23, 8, 0, 0, DateTimeKind.Utc) },
            new GpsAlert { Id = 2, CompanyId = CompanyId, VehicleId = VehiculeDUnAutreClient, Type = "overspeed",
                           Severity = "warning", Message = "Celle d'un autre client", Timestamp = new DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc) });
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();

        var flux = await DashboardService.RecentActivityAsync(
            ctx, CompanyId, LocataireUserId, new List<int> { VehiculeDuLocataire }, 1_000_000, CancellationToken.None);

        flux.Select(a => a.Message).Should().Equal(new[] { "La sienne" },
            "le plafond borne le volume, il ne rouvre pas la portée");
    }
}
