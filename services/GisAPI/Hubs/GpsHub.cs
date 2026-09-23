using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace GisAPI.Hubs;

/// <summary>
/// Noms des groupes SignalR. Écrits ici UNE fois, parce que le hub (qui inscrit) et
/// <see cref="GpsHubService"/> (qui diffuse) doivent parler du même groupe.
///
/// TROIS groupes, et c'est le cœur du cloisonnement temps réel :
///  • <see cref="Societe"/> — tout le monde. Ne porte AUCUNE donnée véhicule : il ne
///    sert qu'aux messages d'abonnement (suspension / réactivation de la société).
///  • <see cref="Flotte"/> — ceux qui voient TOUT le parc (administrateurs). C'est là
///    que partent « PositionUpdate » et « Alert » pour l'ensemble des véhicules.
///  • <see cref="Vehicule"/> — un groupe PAR VÉHICULE, rejoint automatiquement par un
///    utilisateur restreint pour chacun de ses véhicules. Il y reçoit « PositionUpdate »
///    et « Alert » du SEUL véhicule concerné, sous le même nom d'événement : l'écran de
///    monitoring n'a rien à changer.
/// </summary>
public static class GroupesGps
{
    public static string Societe(int companyId) => $"company_{companyId}";
    public static string Flotte(int companyId) => $"fleet_{companyId}";
    public static string Vehicule(int companyId, int vehicleId) => $"scope_{companyId}_vehicle_{vehicleId}";

    /// <summary>
    /// Destinataires de TOUT message qui porte une donnée de véhicule (position,
    /// alerte, écart de tournée) : le groupe « flotte » — ceux qui voient tout le
    /// parc — PLUS le groupe du seul véhicule concerné, quand il est connu.
    ///
    /// La règle vit ici et nulle part ailleurs : la première passe l'avait écrite
    /// dans <see cref="GpsHubService"/> seulement, si bien que le producteur
    /// « écart de tournée » (<c>TourMonitoringService</c>) a continué d'envoyer la
    /// position du véhicule au groupe SOCIÉTÉ, que tout le monde rejoint.
    /// </summary>
    public static IReadOnlyList<string> Diffusion(int companyId, int? vehicleId)
    {
        var groupes = new List<string>(2) { Flotte(companyId) };
        if (vehicleId.HasValue)
            groupes.Add(Vehicule(companyId, vehicleId.Value));
        return groupes;
    }

    /// <summary>
    /// Destinataires d'un message de TOURNÉE (statut, étape atteinte ou en retard,
    /// ouverture par le chauffeur, source de suivi, écart). Une tournée porte un
    /// VÉHICULE : elle suit exactement la règle des positions — administrateurs, plus
    /// les utilisateurs dont la portée contient ce véhicule.
    ///
    /// Neuf diffusions partaient encore au groupe SOCIÉTÉ, que tout le monde rejoint :
    /// le NOM de la tournée, celui de l'étape et l'heure d'arrivée réelle arrivaient à
    /// chaque locataire. Chez un loueur, « Livraison Kap Pharma — Radès » dit à tous
    /// les clients qui loue quoi et où il va.
    ///
    /// Une tournée SANS véhicule (identifiant nul ou négatif — la colonne est requise,
    /// c'est une défense) ne part qu'aux administrateurs.
    /// </summary>
    public static IReadOnlyList<string> Tournee(Tour tour) =>
        Diffusion(tour.CompanyId, tour.VehicleId > 0 ? tour.VehicleId : null);

    /// <summary>
    /// Abonnement EXPLICITE au flux « VehiclePosition » d'un véhicule
    /// (<see cref="GpsHub.SubscribeToVehicle"/>). Écrit en dur à trois endroits jusqu'ici
    /// (inscription, désinscription, diffusion), et relu par AUCUNE réévaluation.
    /// </summary>
    public static string AbonnementVehicule(int vehicleId) => $"vehicle_{vehicleId}";

    /// <summary>
    /// Abonnement EXPLICITE aux passages d'une géozone
    /// (<see cref="GpsHub.SubscribeToGeofence"/>). Même histoire que
    /// <see cref="AbonnementVehicule"/>.
    /// </summary>
    public static string AbonnementZone(int geofenceId) => $"geofence_{geofenceId}";
}

/// <summary>
/// Abonnements EXPLICITES d'une connexion RESTREINTE — ceux de
/// <see cref="GpsHub.SubscribeToVehicle"/> et <see cref="GpsHub.SubscribeToGeofence"/>.
///
/// POURQUOI CE REGISTRE. La réévaluation périodique de la portée ne connaissait que les
/// groupes « scope_{société}_vehicle_{id} ». Les abonnements explicites vivent dans
/// d'AUTRES groupes (<see cref="GroupesGps.AbonnementVehicule"/>,
/// <see cref="GroupesGps.AbonnementZone"/>), que personne ne relisait : un locataire
/// abonné au véhicule A continuait de recevoir chaque trame de A — et chaque passage de
/// zone — après qu'on lui a retiré A, jusqu'à la fermeture de l'onglet. SignalR ne sait
/// pas lister les groupes d'une connexion ; la réévaluation doit donc tenir sa liste.
///
/// Le chemin chaud n'est pas touché : la diffusion vise toujours le même groupe, seul
/// son contenu est tenu à jour.
///
/// ORDRE, qui rend le registre sûr face à la boucle de réévaluation (qui tourne en
/// parallèle des méthodes du hub) :
///  • inscription : groupe D'ABORD, registre ENSUITE ;
///  • retrait (boucle ou désinscription) : registre D'ABORD, groupe ENSUITE.
/// Ainsi une connexion présente dans un groupe y figure toujours au registre, ou va
/// en être retirée par l'opération en cours : aucun entrelacement ne laisse un
/// abonnement dans le groupe SANS trace au registre — ce qui le rendrait éternel.
/// Le pire entrelacement perd un abonnement légitime (on échoue fermé), jamais l'inverse.
/// </summary>
internal sealed class AbonnementsConnexion
{
    private readonly object _verrou = new();
    private readonly HashSet<int> _vehicules = new();
    private readonly HashSet<int> _zones = new();

    public void AjouterVehicule(int vehiculeId) { lock (_verrou) _vehicules.Add(vehiculeId); }
    public bool RetirerVehicule(int vehiculeId) { lock (_verrou) return _vehicules.Remove(vehiculeId); }
    public int[] Vehicules() { lock (_verrou) return _vehicules.ToArray(); }

    public void AjouterZone(int zoneId) { lock (_verrou) _zones.Add(zoneId); }
    public bool RetirerZone(int zoneId) { lock (_verrou) return _zones.Remove(zoneId); }
    public int[] Zones() { lock (_verrou) return _zones.ToArray(); }
}

/// <summary>
/// Diffusion des messages de TOURNÉE : une seule porte pour les producteurs
/// (<c>TourMonitoringService</c>, <c>DriverAppController</c>), qui écrivaient chacun
/// « company_{id} » en dur. Destinataires : <see cref="GroupesGps.Tournee"/>.
/// </summary>
public static class DiffusionTournees
{
    public static Task EnvoyerAsync(
        IHubContext<GpsHub> hubContext, Tour tour, string evenement, object message, CancellationToken ct = default) =>
        hubContext.Clients.Groups(GroupesGps.Tournee(tour)).SendAsync(evenement, message, ct);
}

[Authorize]
public class GpsHub : Hub
{
    private readonly ILogger<GpsHub> _logger;
    private readonly IGisDbContext _context;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IHubContext<GpsHub>? _hubContext;

    /// <summary>
    /// Période de réévaluation de la portée temps réel d'une connexion RESTREINTE.
    /// Voir <see cref="SuivreLaPortee"/> pour le pourquoi.
    /// </summary>
    private static readonly TimeSpan PeriodeReevaluationPortee = TimeSpan.FromSeconds(60);

    /// <param name="scopeFactory">
    /// Nécessaire à la réévaluation périodique : la portée DI du hub meurt avec
    /// l'appel, donc <see cref="_context"/> n'est pas utilisable après. Optionnel
    /// pour que les tests unitaires puissent construire le hub à la main sans
    /// démarrer de boucle ; en production le conteneur le fournit toujours.
    /// </param>
    /// <param name="hubContext">
    /// Idem : <c>Hub.Groups</c> appartient à l'instance de hub, détruite à la fin
    /// de l'appel. Seul le gestionnaire de groupes de l'<see cref="IHubContext{THub}"/>
    /// survit à la connexion.
    /// </param>
    public GpsHub(
        ILogger<GpsHub> logger,
        IGisDbContext context,
        IServiceScopeFactory? scopeFactory = null,
        IHubContext<GpsHub>? hubContext = null)
    {
        _logger = logger;
        _context = context;
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Portée de l'appelant reconstruite depuis SON JETON. Le <c>TenantMiddleware</c> ne
    /// tourne que sur les requêtes HTTP : sur une trame WebSocket, le
    /// <c>ICurrentTenantService</c> de la portée DI du hub est VIERGE. On repose donc la
    /// même identité à partir des claims, puis on réutilise <see cref="VehicleScope"/> —
    /// la règle n'est pas réécrite ici.
    /// </summary>
    private ICurrentTenantService TenantDuJeton() => TenantDepuis(Context.User);

    /// <summary>
    /// La reconstruction elle-même, sans état : interne pour que les tests la rejouent
    /// sur un VRAI jeton plutôt que de fabriquer un tenant à la main.
    /// </summary>
    internal static ICurrentTenantService TenantDepuis(ClaimsPrincipal? utilisateur)
    {
        var tenant = new CurrentTenantService();

        var userIdClaim = utilisateur?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? utilisateur?.FindFirst("sub")?.Value;
        var companyIdClaim = utilisateur?.FindFirst("companyId")?.Value;

        if (int.TryParse(userIdClaim, out var userId) && int.TryParse(companyIdClaim, out var companyId))
        {
            tenant.SetTenant(
                companyId,
                userId,
                utilisateur?.FindFirst(ClaimTypes.Email)?.Value ?? "",
                utilisateur?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray() ?? Array.Empty<string>(),
                utilisateur?.FindAll("permission").Select(c => c.Value).ToArray() ?? Array.Empty<string>(),
                utilisateur?.FindFirst(JwtClaims.AccountType)?.Value);
        }

        return tenant;
    }

    /// <summary>Clé du registre <see cref="AbonnementsConnexion"/> dans <c>Context.Items</c>.</summary>
    private static readonly object CleAbonnements = new();

    /// <summary>
    /// Registre des abonnements explicites de CETTE connexion, ou <c>null</c> pour un
    /// administrateur (aucune réévaluation : sa portée est tout le parc).
    /// <c>Context.Items</c> vit aussi longtemps que la connexion et survit aux instances
    /// de hub, détruites à la fin de chaque appel.
    /// </summary>
    private AbonnementsConnexion? AbonnementsDeLaConnexion() =>
        Context.Items is { } items && items.TryGetValue(CleAbonnements, out var registre)
            ? registre as AbonnementsConnexion
            : null;

    /// <summary>
    /// Véhicules visibles par l'appelant. TROIS états : <c>null</c> = administrateur,
    /// il voit TOUT le parc ; liste non vide = ses véhicules ; liste VIDE = il ne voit RIEN.
    /// </summary>
    /// <remarks>
    /// <paramref name="ct"/> est explicite parce qu'à la DÉCONNEXION le jeton
    /// <c>Context.ConnectionAborted</c> est DÉJÀ annulé : s'en servir ferait lever
    /// la requête et sauterait tout le nettoyage.
    /// </remarks>
    private Task<List<int>?> PorteeVehiculesAsync(ICurrentTenantService tenant, CancellationToken? ct = null) =>
        VehicleScope.AccessibleVehicleIdsAsync(_context, tenant, ct ?? Context.ConnectionAborted);

    public override async Task OnConnectedAsync()
    {
        var companyId = Context.User?.FindFirst("companyId")?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Compte chauffeur (migration 050) : jamais sur le hub de la flotte — le groupe
        // company_{id} diffuse les positions de tous les véhicules, et SubscribeToVehicle
        // ne vérifie pas l'appartenance. Défense en profondeur : PermissionMiddleware refuse
        // déjà /hubs/* et /api/hubs/* à un jeton « chauffeur ». Mais un salarié converti
        // garde un jeton SANS ce claim jusqu'à son expiration (24 h) : la ligne en base
        // tranche aussi, lue une fois à la connexion (pas à chaque message).
        if (Context.User?.FindFirst(JwtClaims.AccountType)?.Value == UserAccountTypes.Driver
            || await IsDriverAccountInDatabaseAsync(userId))
        {
            _logger.LogWarning("Hub GPS refusé à un compte chauffeur ({ConnectionId})", Context.ConnectionId);
            Context.Abort();
            return;
        }

        if (!string.IsNullOrEmpty(companyId) && int.TryParse(companyId, out var societeId))
        {
            // Groupe société : abonnement seulement, aucune donnée véhicule n'y passe.
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupesGps.Societe(societeId));

            // LE trou le plus large de l'incident HERTZ : le flux temps réel partait du
            // groupe société, si bien qu'un locataire affecté à 2 véhicules sur 307
            // recevait en continu la position de TOUT le parc — sans jamais appeler la
            // moindre route REST. Fermer les API ne suffisait donc pas.
            //
            // TROIS états : administrateur => groupe « flotte » (il reçoit tout, comme
            // avant) ; utilisateur restreint => un groupe par véhicule de SA portée ;
            // portée VIDE => aucun groupe de données, il ne reçoit RIEN.
            var portee = await PorteeVehiculesAsync(TenantDuJeton());

            if (portee is null)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, GroupesGps.Flotte(societeId));
                _logger.LogInformation("Client {ConnectionId} joined fleet group {CompanyId}",
                    Context.ConnectionId, societeId);
            }
            else
            {
                foreach (var vehiculeId in portee)
                    await Groups.AddToGroupAsync(Context.ConnectionId, GroupesGps.Vehicule(societeId, vehiculeId));

                _logger.LogInformation(
                    "Client {ConnectionId} joined {Count} scoped vehicle groups (company {CompanyId})",
                    Context.ConnectionId, portee.Count, societeId);

                // Registre des abonnements EXPLICITES de la connexion, relu par la
                // réévaluation (voir AbonnementsConnexion). Posé ici, avant toute
                // méthode du hub : SignalR n'en dispatche aucune tant que
                // OnConnectedAsync n'a pas rendu la main. Items n'est jamais null en
                // production ; un HubCallerContext simulé peut le laisser à null.
                var abonnements = new AbonnementsConnexion();
                if (Context.Items is { } items) items[CleAbonnements] = abonnements;

                // La portée était figée ICI, à la connexion, et nulle part relue :
                // un véhicule RETIRÉ à l'utilisateur continuait de lui être diffusé
                // jusqu'à ce qu'il recharge la page — un onglet de supervision reste
                // ouvert des heures. C'est le dernier résidu de la fuite HERTZ.
                SuivreLaPortee(societeId, portee, abonnements);
            }
        }

        if (!string.IsNullOrEmpty(userId))
        {
            // Add user to personal notification group
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            _logger.LogDebug("Client {ConnectionId} joined user group {UserId}", 
                Context.ConnectionId, userId);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Réévaluation PÉRIODIQUE de la portée d'une connexion restreinte, tant qu'elle
    /// vit. Une boucle par connexion restreinte, réveillée toutes les
    /// <see cref="PeriodeReevaluationPortee"/> ; elle meurt avec la connexion
    /// (<c>ConnectionAborted</c>), sans état statique à purger.
    ///
    /// POURQUOI CE MÉCANISME ET PAS UN AUTRE. Une invalidation POUSSÉE depuis l'écran
    /// des affectations serait immédiate, mais il faudrait un registre connexions ↔
    /// utilisateur et un appel depuis chaque site qui touche <c>user_vehicles</c> :
    /// un site oublié = une fuite silencieuse, exactement le défaut qu'on répare.
    /// Ici la boucle relit la SOURCE (<see cref="VehicleScope"/>) : rien à oublier.
    ///
    /// CE QUE ÇA COÛTE. Une requête indexée sur <c>user_vehicles</c> par minute et par
    /// connexion restreinte, hors du chemin chaud : la diffusion des trames n'est pas
    /// touchée. Les administrateurs (portée <c>null</c>) n'ouvrent AUCUNE boucle —
    /// leur portée ne dépend que des rôles du jeton, qui ne bougent pas en session.
    ///
    /// CE QUE ÇA NE FERME PAS : la minute qui sépare deux passes, et un changement de
    /// RÔLE (admin ↔ restreint), qui vit dans le jeton et n'est repris qu'au
    /// renouvellement de celui-ci.
    /// </summary>
    private void SuivreLaPortee(int societeId, List<int> porteeInitiale, AbonnementsConnexion abonnements)
    {
        // Hub construit à la main (tests unitaires) : pas de boucle. Le cloisonnement
        // par groupes reste entier, seule la réévaluation est absente.
        if (_scopeFactory is null || _hubContext is null) return;

        var scopeFactory = _scopeFactory;
        var groupes = _hubContext.Groups;
        var logger = _logger;
        var connectionId = Context.ConnectionId;
        var tenant = TenantDuJeton();
        var arret = Context.ConnectionAborted;
        var portee = new HashSet<int>(porteeInitiale);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!arret.IsCancellationRequested)
                {
                    await Task.Delay(PeriodeReevaluationPortee, arret);

                    using var scope = scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<IGisDbContext>();

                    var (ajoutes, retires) = await ReevaluerPorteeAsync(
                        context, tenant, groupes, connectionId, societeId, portee, arret, abonnements);

                    if (ajoutes.Count > 0 || retires.Count > 0)
                    {
                        logger.LogInformation(
                            "Portée temps réel réévaluée pour {ConnectionId} : +{Ajoutes} / -{Retires} véhicule(s)",
                            connectionId, ajoutes.Count, retires.Count);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Déconnexion : fin normale de la boucle.
            }
            catch (Exception ex)
            {
                // Une boucle morte NE DOIT PAS passer inaperçue : elle rouvre la
                // fenêtre d'exposition pour toute la durée de la connexion.
                logger.LogError(ex,
                    "Réévaluation de la portée temps réel interrompue pour {ConnectionId}", connectionId);
            }
        }, arret);
    }

    /// <summary>
    /// UNE passe de réévaluation : relit la portée, retire d'abord les groupes des
    /// véhicules PERDUS (c'est le cas qui fuit), ajoute ensuite ceux des véhicules
    /// gagnés, et met <paramref name="porteeCourante"/> à jour.
    ///
    /// Une portée <c>null</c> (« voit tout le parc ») est ici IMPOSSIBLE — la boucle
    /// n'est ouverte que pour les restreints, et le rôle vient du jeton. On refuse
    /// donc d'en déduire quoi que ce soit : surtout pas d'inscrire la connexion au
    /// groupe flotte, ce qui fabriquerait la fuite qu'on répare.
    ///
    /// Avec <paramref name="abonnements"/> (la boucle le passe toujours), la même passe
    /// défait aussi les abonnements EXPLICITES sortis de la portée — voir
    /// <see cref="ElaguerAbonnementsAsync"/>. Le retour ne décrit que les groupes
    /// cloisonnés.
    /// </summary>
    internal static async Task<(IReadOnlyList<int> Ajoutes, IReadOnlyList<int> Retires)> ReevaluerPorteeAsync(
        IGisDbContext context,
        ICurrentTenantService tenant,
        IGroupManager groupes,
        string connectionId,
        int societeId,
        HashSet<int> porteeCourante,
        CancellationToken ct,
        AbonnementsConnexion? abonnements = null)
    {
        var nouvelle = await VehicleScope.AccessibleVehicleIdsAsync(context, tenant, ct);
        if (nouvelle is null) return (Array.Empty<int>(), Array.Empty<int>());

        var cible = new HashSet<int>(nouvelle);
        var retires = porteeCourante.Except(cible).ToList();
        var ajoutes = cible.Except(porteeCourante).ToList();

        foreach (var vehiculeId in retires)
            await groupes.RemoveFromGroupAsync(connectionId, GroupesGps.Vehicule(societeId, vehiculeId), ct);

        foreach (var vehiculeId in ajoutes)
            await groupes.AddToGroupAsync(connectionId, GroupesGps.Vehicule(societeId, vehiculeId), ct);

        porteeCourante.Clear();
        foreach (var vehiculeId in cible) porteeCourante.Add(vehiculeId);

        if (abonnements is not null)
            await ElaguerAbonnementsAsync(context, tenant, groupes, connectionId, societeId, cible, abonnements, ct);

        return (ajoutes, retires);
    }

    /// <summary>
    /// Défait les abonnements EXPLICITES qui ne sont plus couverts par la portée :
    ///  • un véhicule suivi par <see cref="SubscribeToVehicle"/> et sorti de la portée —
    ///    le groupe « vehicle_{id} » reçoit CHAQUE trame (BroadcastPositionCommandHandler) ;
    ///  • une géozone suivie par <see cref="SubscribeToGeofence"/> qui ne remplit plus la
    ///    règle de <see cref="ZonesSuiviesEnDirectAsync"/> (véhicule rattaché retiré à
    ///    l'utilisateur, véhicule d'un AUTRE client rattaché à la zone, case Géofences
    ///    décochée, dernière liaison supprimée) — le groupe « geofence_{id} » reçoit
    ///    chaque franchissement, avec le véhicule et sa position.
    ///
    /// On compare au registre de la connexion, pas au seul diff de la passe : un
    /// abonnement pris ENTRE deux passes sur un véhicule ajouté puis retiré dans la
    /// même minute n'apparaît dans aucun diff, mais il est au registre.
    ///
    /// Coût : rien pour les véhicules (comparaison en mémoire) ; pour les zones, la
    /// requête de visibilité n'est faite QUE si la connexion suit au moins une zone —
    /// ce que l'écran ne fait jamais aujourd'hui.
    /// </summary>
    internal static async Task<(IReadOnlyList<int> Vehicules, IReadOnlyList<int> Zones)> ElaguerAbonnementsAsync(
        IGisDbContext context,
        ICurrentTenantService tenant,
        IGroupManager groupes,
        string connectionId,
        int societeId,
        IReadOnlySet<int> portee,
        AbonnementsConnexion abonnements,
        CancellationToken ct)
    {
        var vehiculesRetires = new List<int>();
        foreach (var vehiculeId in abonnements.Vehicules())
        {
            if (portee.Contains(vehiculeId)) continue;

            // Registre D'ABORD, groupe ENSUITE (voir AbonnementsConnexion).
            abonnements.RetirerVehicule(vehiculeId);
            await groupes.RemoveFromGroupAsync(connectionId, GroupesGps.AbonnementVehicule(vehiculeId), ct);
            vehiculesRetires.Add(vehiculeId);
        }

        var zonesRetirees = new List<int>();
        var zones = abonnements.Zones();
        if (zones.Length > 0)
        {
            // Même règle que l'abonnement, relue à la source.
            var suivables = await ZonesSuiviesEnDirectAsync(
                context, societeId, tenant.UserId ?? 0, portee, zones, ct);

            foreach (var zoneId in zones)
            {
                if (suivables.Contains(zoneId)) continue;

                abonnements.RetirerZone(zoneId);
                await groupes.RemoveFromGroupAsync(connectionId, GroupesGps.AbonnementZone(zoneId), ct);
                zonesRetirees.Add(zoneId);
            }
        }

        return (vehiculesRetires, zonesRetirees);
    }

    /// <summary>
    /// Parmi <paramref name="zones"/>, celles dont un utilisateur RESTREINT peut suivre
    /// les passages EN DIRECT : il a la case Géofences, la zone est rattachée à au moins
    /// un véhicule, et TOUS ses véhicules rattachés sont dans sa portée.
    ///
    /// POURQUOI PLUS STRICT QUE LA VISIBILITÉ (<c>DashboardService.VisibleGeofenceIdsAsync</c>).
    /// Voir une zone, c'est voir son nom et son tracé ; ses PASSAGES restent bornés aux
    /// véhicules de la portée par chaque écran. Le message « GeofenceEvent », lui, part
    /// au groupe de la zone pour CHAQUE véhicule qui la franchit — identifiant, nom et
    /// position compris — et la diffusion ne connaît que la zone
    /// (<c>IGpsHubService.SendGeofenceEventAsync(geofenceId, …)</c>) : impossible d'y
    /// trier par véhicule. Une zone rattachée à A (Kap Pharma) ET à B (un autre client),
    /// ou une zone sans liaison — que la surveillance applique à TOUT le parc —
    /// donnerait donc en direct les passages des véhicules des autres. Seule une zone
    /// dont tous les déclencheurs possibles sont à lui est sûre à suivre.
    ///
    /// Les administrateurs (portée <c>null</c>) ne passent pas par ici : tout le parc.
    /// Case lue EN BASE, bornée à la société (le jeton ne porte pas les cases de module).
    /// </summary>
    internal static async Task<HashSet<int>> ZonesSuiviesEnDirectAsync(
        IGisDbContext context,
        int societeId,
        int userId,
        IReadOnlyCollection<int> portee,
        IReadOnlyCollection<int> zones,
        CancellationToken ct)
    {
        if (zones.Count == 0 || portee.Count == 0) return new HashSet<int>();

        var caseGeofences = await context.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.CompanyId == societeId)
            .Select(u => u.CanGeofences)
            .FirstOrDefaultAsync(ct);
        if (!caseGeofences) return new HashSet<int>();

        List<int> ids = zones.ToList();
        var liaisons = await context.GeofenceVehicles.AsNoTracking()
            .Where(gv => ids.Contains(gv.GeofenceId) && gv.Geofence!.CompanyId == societeId)
            .Select(gv => new { gv.GeofenceId, gv.VehicleId })
            .ToListAsync(ct);

        // Une zone SANS liaison n'apparaît pas ici : elle n'est donc jamais retenue.
        return liaisons
            .GroupBy(l => l.GeofenceId)
            .Where(zone => zone.All(l => portee.Contains(l.VehicleId)))
            .Select(zone => zone.Key)
            .ToHashSet();
    }

    private async Task<bool> IsDriverAccountInDatabaseAsync(string? userId)
    {
        if (!int.TryParse(userId, out var id)) return false;
        var accountType = await _context.Users
            .IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.AccountType)
            .FirstOrDefaultAsync(Context.ConnectionAborted);
        return accountType == UserAccountTypes.Driver;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var companyId = Context.User?.FindFirst("companyId")?.Value;
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Symétrique exact de OnConnectedAsync, et par le MÊME helper. Cette méthode
        // n'avait pas suivi la refonte des groupes : elle ne défaisait que
        // « company_{id} », écrit en dur — donc jamais le groupe flotte ni les groupes
        // par véhicule — et sur la chaîne BRUTE du claim, là où la connexion, elle,
        // exige un entier. SignalR purge de toute façon les groupes d'une connexion
        // fermée : ce retrait explicite ne sert qu'à garder les deux moitiés lisibles
        // côte à côte, et à tracer la sortie. Les abonnements EXPLICITES (véhicule,
        // géozone) sont laissés à cette purge : ils ne naissent pas à la connexion.
        if (!string.IsNullOrEmpty(companyId) && int.TryParse(companyId, out var societeId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupesGps.Societe(societeId));

            var portee = await PorteeVehiculesAsync(TenantDuJeton(), CancellationToken.None);

            if (portee is null)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupesGps.Flotte(societeId));
            }
            else
            {
                foreach (var vehiculeId in portee)
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupesGps.Vehicule(societeId, vehiculeId));
            }

            _logger.LogInformation("Client {ConnectionId} left company group {CompanyId}",
                Context.ConnectionId, societeId);
        }

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
        }

        await base.OnDisconnectedAsync(exception);
    }

    // Subscribe to specific vehicle updates
    public async Task SubscribeToVehicle(int vehicleId)
    {
        // Cette méthode acceptait N'IMPORTE QUEL identifiant, sans vérifier ni la société
        // ni la portée : il suffisait d'un appel depuis la console du navigateur pour
        // s'abonner au flux d'un véhicule loué à un autre client — ou d'une autre société.
        var tenant = TenantDuJeton();
        var companyId = tenant.CompanyId ?? 0;

        var memeSociete = await _context.Vehicles
            .AsNoTracking()
            .AnyAsync(v => v.Id == vehicleId && v.CompanyId == companyId, Context.ConnectionAborted);

        if (!memeSociete || !await VehicleScope.CanAccessVehicleAsync(_context, tenant, vehicleId, Context.ConnectionAborted))
        {
            // Silence volontaire : on n'inscrit pas et on ne dit pas que le véhicule existe.
            _logger.LogWarning("Abonnement refusé au véhicule {VehicleId} ({ConnectionId})",
                vehicleId, Context.ConnectionId);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupesGps.AbonnementVehicule(vehicleId));

        // Groupe D'ABORD, registre ENSUITE (voir AbonnementsConnexion) : sans cette
        // trace, la réévaluation ne saurait pas défaire l'abonnement quand le véhicule
        // sort de la portée, et la connexion en recevrait chaque trame jusqu'à la
        // fermeture de l'onglet.
        AbonnementsDeLaConnexion()?.AjouterVehicule(vehicleId);

        _logger.LogDebug("Client {ConnectionId} subscribed to vehicle {VehicleId}",
            Context.ConnectionId, vehicleId);
    }

    public async Task UnsubscribeFromVehicle(int vehicleId)
    {
        AbonnementsDeLaConnexion()?.RetirerVehicule(vehicleId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupesGps.AbonnementVehicule(vehicleId));
    }

    // Subscribe to geofence events
    public async Task SubscribeToGeofence(int geofenceId)
    {
        // Même défaut que SubscribeToVehicle : n'importe quel identifiant était accepté.
        // Pour un restreint, la règle est ZonesSuiviesEnDirectAsync — plus stricte que
        // la VISIBILITÉ d'une zone (DashboardService.VisibleGeofenceIdsAsync), voir là.
        var tenant = TenantDuJeton();
        var companyId = tenant.CompanyId ?? 0;

        var memeSociete = await _context.Geofences
            .AsNoTracking()
            .AnyAsync(g => g.Id == geofenceId && g.CompanyId == companyId, Context.ConnectionAborted);

        if (!memeSociete)
        {
            _logger.LogWarning("Abonnement refusé à la géozone {GeofenceId} ({ConnectionId})",
                geofenceId, Context.ConnectionId);
            return;
        }

        var portee = await PorteeVehiculesAsync(tenant);
        if (portee is not null)
        {
            var suivables = await ZonesSuiviesEnDirectAsync(
                _context, companyId, tenant.UserId ?? 0, portee, new[] { geofenceId }, Context.ConnectionAborted);

            if (!suivables.Contains(geofenceId))
            {
                _logger.LogWarning("Abonnement refusé à la géozone {GeofenceId} ({ConnectionId})",
                    geofenceId, Context.ConnectionId);
                return;
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupesGps.AbonnementZone(geofenceId));

        // Même ordre et même raison que SubscribeToVehicle.
        AbonnementsDeLaConnexion()?.AjouterZone(geofenceId);
    }

    public async Task UnsubscribeFromGeofence(int geofenceId)
    {
        AbonnementsDeLaConnexion()?.RetirerZone(geofenceId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupesGps.AbonnementZone(geofenceId));
    }

    /// <summary>
    /// Chat : prévient un utilisateur que l'appelant est en train d'écrire.
    ///
    /// Acceptait N'IMPORTE QUEL destinataire, toutes sociétés confondues : depuis la
    /// console du navigateur, un compte de n'importe quel client pouvait sonder les
    /// identifiants d'une autre société et y pousser des messages. Le filtre société
    /// de <c>GisDbContext</c> ne protège rien ici — sur une trame WebSocket le tenant
    /// de la portée DI est VIERGE — d'où la société écrite dans la requête.
    /// </summary>
    public async Task ChatTyping(int receiverId)
    {
        var tenant = TenantDuJeton();
        if (tenant.UserId is not int emetteurId || tenant.CompanyId is not int societeId) return;

        var memeSociete = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == receiverId && u.CompanyId == societeId, Context.ConnectionAborted);

        if (!memeSociete)
        {
            // Silence volontaire, comme les abonnements : on ne dit pas si le compte existe.
            _logger.LogWarning("Indicateur de saisie refusé vers l'utilisateur {ReceiverId} ({ConnectionId})",
                receiverId, Context.ConnectionId);
            return;
        }

        await Clients.Group($"user_{receiverId}").SendAsync("ChatTyping", new { SenderId = emetteurId });
    }
}

// Implementation of the Application layer interface
public class GpsHubService : GisAPI.Application.Common.Interfaces.IGpsHubService
{
    private readonly IHubContext<GpsHub> _hubContext;

    public GpsHubService(IHubContext<GpsHub> hubContext)
    {
        _hubContext = hubContext;
    }

    /// <summary>
    /// Diffusion SANS véhicule connu : elle ne peut donc atteindre que les destinataires
    /// « tout le parc » (administrateurs). Seule la route de test
    /// <c>POST /api/gps/test/broadcast</c> passe encore par là — le chemin réel des trames
    /// utilise la surcharge à trois arguments.
    /// </summary>
    public async Task SendPositionUpdateAsync(int companyId, object position)
    {
        await _hubContext.Clients.Group(GroupesGps.Flotte(companyId))
            .SendAsync("PositionUpdate", position);
    }

    /// <summary>
    /// Diffusion d'une position : au groupe « flotte » (ceux qui voient tout le parc) ET
    /// au groupe du SEUL véhicule concerné, que les utilisateurs restreints rejoignent à
    /// la connexion. Même nom d'événement des deux côtés : l'écran ne change pas.
    /// </summary>
    public async Task SendPositionUpdateAsync(int companyId, int? vehicleId, object position)
    {
        await _hubContext.Clients.Groups(GroupesGps.Diffusion(companyId, vehicleId))
            .SendAsync("PositionUpdate", position);
    }

    /// <summary>
    /// Abonnés explicites d'un véhicule. Appelé sur CHAQUE trame : le groupe visé est
    /// inchangé, c'est son contenu que la réévaluation de la portée tient à jour
    /// (<see cref="GpsHub.ElaguerAbonnementsAsync"/>).
    /// </summary>
    public async Task SendVehiclePositionAsync(int vehicleId, object position)
    {
        await _hubContext.Clients.Group(GroupesGps.AbonnementVehicule(vehicleId))
            .SendAsync("VehiclePosition", position);
    }

    /// <summary>
    /// Une alerte porte la plaque et la position du véhicule : elle suit exactement la
    /// même règle de diffusion que la position.
    /// </summary>
    public async Task SendAlertAsync(int companyId, int? vehicleId, object alert)
    {
        await _hubContext.Clients.Groups(GroupesGps.Diffusion(companyId, vehicleId))
            .SendAsync("Alert", alert);
    }

    public async Task SendGeofenceEventAsync(int geofenceId, object geofenceEvent)
    {
        await _hubContext.Clients.Group(GroupesGps.AbonnementZone(geofenceId))
            .SendAsync("GeofenceEvent", geofenceEvent);
    }

    /// <summary>
    /// Suspension / réactivation de la société : le SEUL message qui a encore sa place
    /// dans le groupe société, parce qu'il ne porte aucune donnée de véhicule.
    /// Le nom du groupe était réécrit en dur ici, à côté de
    /// <see cref="GroupesGps.Societe"/> : deux définitions pour un seul groupe, donc
    /// une divergence en attente. Une seule source de nommage.
    /// </summary>
    public async Task SendSubscriptionChangedAsync(int companyId, string status)
    {
        await _hubContext.Clients.Group(GroupesGps.Societe(companyId))
            .SendAsync("SubscriptionChanged", new { status });
    }
}



