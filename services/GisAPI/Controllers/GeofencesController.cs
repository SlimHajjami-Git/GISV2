using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MediatR;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Services;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GeofencesController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly IPublisher _publisher;
    private readonly ICurrentTenantService _tenantService;

    public GeofencesController(GisDbContext context, IPublisher publisher, ICurrentTenantService tenantService)
    {
        _context = context;
        _publisher = publisher;
        _tenantService = tenantService;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    /// <summary>
    /// Identifiant de l'appelant, lu comme dans les HUIT autres contrôleurs.
    ///
    /// DÉFAUT CORRIGÉ LE 23/09/2026 : ce helper lisait un claim « userId » que
    /// <c>JwtService</c> n'émet PAS. Le jeton porte <c>sub</c> (que la validation
    /// JWT remappe vers <see cref="ClaimTypes.NameIdentifier"/>, aucun
    /// <c>MapInboundClaims = false</c> dans Program.cs), <c>email</c>, <c>name</c>,
    /// <c>companyId</c>, <c>roleId</c>, <c>acct</c>, <c>jti</c>, les rôles et
    /// éventuellement <c>impersonated_by</c> — et aucun <c>IClaimsTransformation</c>
    /// n'en ajoute. Le helper rendait donc TOUJOURS 0 : la portée géozones lisait
    /// <c>users.can_geofences</c> pour l'utilisateur 0, ne trouvait rien, retombait
    /// sur false et rendait une liste VIDE. En production, TOUT non-administrateur de
    /// TOUTE société aurait vu zéro géozone, 404 sur chacune, zéro passage et zéro
    /// statistique, même avec can_geofences = TRUE et des liaisons correctes.
    /// Fail-closed, donc pas une fuite — mais le module entier était détruit.
    /// </summary>
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    /// <summary>
    /// Véhicules visibles par l'appelant. TROIS états : <c>null</c> = administrateur,
    /// AUCUN filtre ; liste non vide = ses véhicules ; liste VIDE = il ne voit RIEN.
    /// </summary>
    private Task<List<int>?> PorteeVehiculesAsync() =>
        VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, HttpContext.RequestAborted);

    /// <summary>
    /// Géozones visibles par l'appelant, RÈGLE DU 23/09/2026 décidée par Slim, écrite une
    /// seule fois dans <see cref="DashboardService.VisibleGeofenceIdsAsync"/> :
    ///   • administrateur = toutes les zones de sa société (<c>null</c>, aucun filtre) ;
    ///   • restreint AVEC la case Géofences (<c>users.can_geofences</c>) = zones rattachées
    ///     à l'un de SES véhicules (<c>geofence_vehicles</c>) UNION zones rattachées à
    ///     AUCUN véhicule (zones de société, gérées par ceux qui ont la case) ;
    ///   • restreint SANS la case = AUCUNE zone, même rattachée (Kap Pharma).
    /// Une zone rattachée UNIQUEMENT aux véhicules d'autres utilisateurs reste invisible.
    ///
    /// Ces routes sont la FACE REST de ce que le tableau de bord a cloisonné : le
    /// passe-droit <c>IsSharedReferenceRead</c> du <c>PermissionMiddleware</c> laisse tout
    /// GET sous « /api/geofences » passer SANS la case Géofences, si bien que Kap Pharma
    /// (can_geofences = FALSE) lisait ici les zones, leurs passages et leurs statistiques.
    /// </summary>
    private async Task<List<int>?> PorteeGeozonesAsync(int companyId, List<int>? porteeVehicules) =>
        await DashboardService.VisibleGeofenceIdsAsync(
            _context, companyId, GetUserId(), porteeVehicules, HttpContext.RequestAborted);

    // ==================== RÈGLE D'ÉCRITURE ====================
    //
    // RÈGLE DU 23/09/2026, CORRIGÉE LE JOUR MÊME. La passe précédente avait réservé
    // TOUTE écriture sur les zones (création, modification, suppression, bascule
    // actif/inactif, liaison des véhicules, groupes) aux seuls ADMINISTRATEURS. C'était
    // une règle INVENTÉE, pas une règle métier — et elle aurait cassé la production du
    // jour au lendemain. Faits relevés en production le 23/09 (lecture seule) :
    // 17 comptes NON administrateurs ont la case Géofences et/ou Entretien et/ou
    // Paramètres ; SICOAC en a 7 (rôle « Operateur », 11 véhicules affectés sur 11 pour
    // six d'entre eux, 5 sur 11 pour le septième), PARENIN 2 (13 véhicules sur 87),
    // EXALTIS 1 (5 sur 8), BELIVE plusieurs. Ce sont eux qui gèrent les zones de leur
    // société au quotidien : la règle « administrateur seulement » les en coupait tous.
    //
    // La case Géofences (users.can_geofences) SIGNIFIE « gère les zones ». Règle :
    //   • administrateur : tout, comme avant ;
    //   • restreint SANS la case : PermissionMiddleware le bloque déjà à l'entrée sur
    //     toute écriture ; ici, par défense en profondeur, il ne VOIT aucune zone
    //     (404 sur chacune) et ne peut rien créer (403) ;
    //   • restreint AVEC la case : il CRÉE une zone, et MODIFIE / SUPPRIME / BASCULE /
    //     rattache toute zone qu'il VOIT (règle de lecture ci-dessus). Une zone qu'il ne
    //     voit pas reste en 404 — même réponse qu'une zone inexistante, pour ne jamais
    //     révéler qu'elle existe.
    // Une zone neuve n'est rattachée à rien : c'est une zone de société, donc visible de
    // son créateur (voir la règle de lecture) — elle ne disparaît plus sous ses yeux.
    //
    // Ce que la règle protège quand même chez un LOUEUR (HERTZ) : un restreint ne rattache
    // que SES véhicules et ne défait jamais la liaison d'un véhicule hors de sa portée
    // (voir AssignVehicles) ; un corps de requête ne peut pas glisser de liaison ni de
    // groupe étranger (voir CreateGeofence et RefusGroupeAsync).

    /// <summary>
    /// L'appelant GÈRE-t-il les zones (création, écriture sur celles qu'il voit) ?
    /// Administrateur : oui. Restreint : seulement avec la case Géofences, et jamais avec
    /// une portée VIDE — la liste vide veut dire « il ne voit rien », donc il ne gère rien.
    /// </summary>
    private async Task<bool> GereLesZonesAsync(int companyId, List<int>? porteeVehicules)
    {
        if (porteeVehicules is null) return true;
        if (porteeVehicules.Count == 0) return false;
        return await DashboardService.CanManageGeofencesAsync(
            _context, companyId, GetUserId(), HttpContext.RequestAborted);
    }

    /// <summary>
    /// ÉCRITURE sur une zone existante (modifier, supprimer, basculer, rattacher des
    /// véhicules). Rend le résultat d'erreur à renvoyer, ou <c>null</c> si c'est permis :
    /// permis dès que l'appelant VOIT la zone (la règle de lecture exige déjà la case
    /// Géofences), 404 sinon.
    /// </summary>
    private async Task<ActionResult?> RefusEcritureZoneAsync(int companyId, int zoneId, List<int>? porteeVehicules)
    {
        if (porteeVehicules is null) return null;

        var porteeZones = await PorteeGeozonesAsync(companyId, porteeVehicules);
        return porteeZones is null || porteeZones.Contains(zoneId) ? null : NotFound();
    }

    /// <summary>
    /// Groupes de zones visibles par l'appelant, ou <c>null</c> pour un administrateur.
    /// Même logique que les zones : un groupe est visible s'il contient au moins une zone
    /// visible, OU s'il est VIDE (conteneur de société, comme une zone sans liaison) — et
    /// ce dernier cas seulement pour un gestionnaire de zones, sinon un groupe créé
    /// disparaîtrait sous les yeux de son créateur. Sans la case : aucun groupe.
    /// </summary>
    private async Task<List<int>?> PorteeGroupesAsync(int companyId, List<int>? porteeVehicules)
    {
        if (porteeVehicules is null) return null;
        if (!await GereLesZonesAsync(companyId, porteeVehicules)) return new List<int>();

        List<int> zoneIds = await PorteeGeozonesAsync(companyId, porteeVehicules) ?? new List<int>();
        return await _context.GeofenceGroups
            .AsNoTracking()
            .Where(g => g.CompanyId == companyId
                && (!g.Geofences.Any() || g.Geofences.Any(z => zoneIds.Contains(z.Id))))
            .Select(g => g.Id)
            .ToListAsync(HttpContext.RequestAborted);
    }

    /// <summary>
    /// Le groupe demandé pour une zone (création ou changement de groupe) doit être un
    /// groupe de la société de l'appelant — et, pour un restreint, un groupe qu'il VOIT.
    /// Sans ce contrôle, écrire <c>GroupId</c> suffisait à lire le NOM d'un groupe caché
    /// (la liste le publie dans <c>GroupName</c>), voire celui d'une autre société.
    /// Inexistant et invisible rendent la même réponse : pas d'oracle d'existence.
    /// </summary>
    private async Task<ActionResult?> RefusGroupeAsync(int companyId, int? groupId, List<int>? porteeVehicules)
    {
        if (groupId is null) return null;
        var groupeId = groupId.Value;

        var existe = await _context.GeofenceGroups
            .AsNoTracking()
            .AnyAsync(g => g.Id == groupeId && g.CompanyId == companyId, HttpContext.RequestAborted);
        if (existe)
        {
            var porteeGroupes = await PorteeGroupesAsync(companyId, porteeVehicules);
            if (porteeGroupes is null || porteeGroupes.Contains(groupeId)) return null;
        }

        return BadRequest(new { message = "Groupe de zones introuvable." });
    }

    /// <summary>
    /// Plafond des listes de passages. Au-delà, ce n'est plus une liste : l'écran en
    /// affiche au plus 100 (historique d'une zone), 50 ailleurs.
    /// </summary>
    internal const int LimitePassagesMax = 200;

    /// <summary>
    /// Borne le paramètre <c>limit</c> reçu de la query string, comme sur
    /// <c>/api/dashboard/activity</c> : il arrivait BRUT jusqu'au <c>Take()</c>.
    /// <c>?limit=-1</c> produisait un <c>LIMIT -1</c> refusé par PostgreSQL (une 500 à la
    /// portée de n'importe quel compte connecté) et <c>?limit=1000000</c> matérialisait
    /// un million de passages avec leur plaque et leur adresse.
    /// </summary>
    internal static int BornerLimitePassages(int limit) => Math.Clamp(limit, 1, LimitePassagesMax);

    /// <summary>
    /// Projection UNIQUE d'une zone vers ce que l'API publie, partagée par la liste et par
    /// le détail : la liste masquait déjà les véhicules hors portée, le détail rendait
    /// l'ENTITÉ BRUTE avec la liste entière des liaisons (route sœur). Une seule
    /// projection, un seul filtre — les deux routes ne peuvent plus diverger.
    /// <paramref name="tousVehicules"/> = administrateur (aucun filtre) ; sinon seuls les
    /// véhicules de <paramref name="vehiculesVisibles"/> sont nommés.
    /// </summary>
    private static Expression<Func<Geofence, GeofenceDto>> VersDto(bool tousVehicules, List<int> vehiculesVisibles) =>
        g => new GeofenceDto
        {
            Id = g.Id,
            Name = g.Name,
            Description = g.Description,
            Type = g.Type,
            Color = g.Color,
            IconName = g.IconName,
            Coordinates = g.Coordinates,
            CenterLat = g.CenterLat,
            CenterLng = g.CenterLng,
            Radius = g.Radius,
            AlertOnEntry = g.AlertOnEntry,
            AlertOnExit = g.AlertOnExit,
            AutoStopOnEntry = g.AutoStopOnEntry,
            AlertSpeedLimit = g.AlertSpeedLimit,
            NotificationCooldownMinutes = g.NotificationCooldownMinutes,
            MaxStayDurationMinutes = g.MaxStayDurationMinutes,
            ActiveStartTime = g.ActiveStartTime,
            ActiveEndTime = g.ActiveEndTime,
            ActiveDays = g.ActiveDays,
            IsActive = g.IsActive,
            GroupId = g.GroupId,
            GroupName = g.Group != null ? g.Group.Name : null,
            AssignedVehicleIds = g.AssignedVehicles
                .Where(v => tousVehicules || vehiculesVisibles.Contains(v.VehicleId))
                .Select(v => v.VehicleId).ToList(),
            AssignedVehicleNames = g.AssignedVehicles
                .Where(v => tousVehicules || vehiculesVisibles.Contains(v.VehicleId))
                .Select(v => v.Vehicle!.Plate ?? v.Vehicle.Name).ToList(),
            CreatedAt = g.CreatedAt,
            UpdatedAt = g.UpdatedAt
        };

    // ==================== GEOFENCES ====================

    [HttpGet]
    public async Task<ActionResult<List<GeofenceDto>>> GetGeofences([FromQuery] int? groupId = null, [FromQuery] bool? isActive = null)
    {
        var companyId = GetCompanyId();

        var query = _context.Geofences
            .AsNoTracking()
            .Where(g => g.CompanyId == companyId);

        var porteeVehicules = await PorteeVehiculesAsync();
        var porteeZones = await PorteeGeozonesAsync(companyId, porteeVehicules);
        if (porteeZones is not null)
        {
            List<int> zoneIds = porteeZones;
            query = query.Where(g => zoneIds.Contains(g.Id));
        }

        // La liste publie la PLAQUE de chaque véhicule rattaché à la zone : pour un
        // utilisateur restreint, on ne nomme que les siens — une zone partagée entre
        // deux locataires ne doit pas révéler la flotte de l'autre. Projection commune
        // avec GET {id} : voir VersDto.
        //
        // TRADUCTION : les tests de confidentialité tournent sur le fournisseur InMemory,
        // qui évalue tout côté client et ne peut donc pas lever « could not be
        // translated ». La traduction PostgreSQL de cette projection (et de la règle de
        // visibilité) est vérifiée par GeozonesTraductionSqlTests, qui exécute CETTE
        // méthode contre le fournisseur Npgsql sans ouvrir de connexion :
        //   • restreint : la liste des véhicules devient UN paramètre tableau
        //     (« = ANY (…) »), pas une constante par locataire ;
        //   • administrateur : le booléen capturé replie le filtre, la jointure est nue.
        // Si une montée de version d'EF ou de Npgsql casse cette traduction, ce test
        // tombe — plus besoin d'attendre la production.
        var tousVehicules = porteeVehicules is null;
        List<int> vehiculesVisibles = porteeVehicules ?? new List<int>();

        if (groupId.HasValue)
            query = query.Where(g => g.GroupId == groupId);

        if (isActive.HasValue)
            query = query.Where(g => g.IsActive == isActive);

        var geofences = await query
            .OrderBy(g => g.Name)
            .Select(VersDto(tousVehicules, vehiculesVisibles))
            .ToListAsync();

        return Ok(geofences);
    }

    /// <summary>
    /// Détail d'une zone.
    ///
    /// FUITE FERMÉE LE 23/09/2026 : la route rendait l'ENTITÉ BRUTE avec
    /// <c>.Include(g => g.AssignedVehicles)</c>, c'est-à-dire la liste ENTIÈRE des
    /// véhicules rattachés — y compris ceux des autres locataires — alors que la route
    /// sœur GET /api/geofences ne nomme que ceux de la portée. Elle passe désormais par la
    /// MÊME projection (<see cref="VersDto"/>) : aucun véhicule hors portée n'en sort.
    /// Les écrans qui l'appellent (fenêtre de passage du suivi et du bandeau) n'en lisent
    /// que le nom, le type et la géométrie, que la projection porte à l'identique.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<GeofenceDto>> GetGeofence(int id)
    {
        var companyId = GetCompanyId();

        // Zone hors de la visibilité de l'appelant : même 404 qu'une zone inexistante.
        var porteeVehicules = await PorteeVehiculesAsync();
        var porteeZones = await PorteeGeozonesAsync(companyId, porteeVehicules);
        if (porteeZones is not null && !porteeZones.Contains(id))
            return NotFound();

        var tousVehicules = porteeVehicules is null;
        List<int> vehiculesVisibles = porteeVehicules ?? new List<int>();

        var geofence = await _context.Geofences
            .AsNoTracking()
            .Where(g => g.Id == id && g.CompanyId == companyId)
            .Select(VersDto(tousVehicules, vehiculesVisibles))
            .FirstOrDefaultAsync();

        if (geofence == null)
            return NotFound();

        return Ok(geofence);
    }

    [HttpPost]
    public async Task<ActionResult<Geofence>> CreateGeofence([FromBody] Geofence geofence)
    {
        var companyId = GetCompanyId();
        var porteeVehicules = await PorteeVehiculesAsync();

        // Un gestionnaire de zones (case Géofences) crée des zones, administrateur ou non.
        if (!await GereLesZonesAsync(companyId, porteeVehicules)) return Forbid();
        if (await RefusGroupeAsync(companyId, geofence.GroupId, porteeVehicules) is { } refusGroupe) return refusGroupe;

        // Le corps est l'ENTITÉ : on n'en retient que les champs de la zone. Sans cela,
        // un « assignedVehicles » glissé dans le JSON créait des liaisons vers N'IMPORTE
        // QUEL véhicule — y compris celui d'un autre locataire ou d'une autre société —
        // en contournant tous les contrôles d'AssignVehicles. Les liaisons passent
        // UNIQUEMENT par POST {id}/vehicles.
        geofence.Id = 0;
        geofence.CompanyId = companyId;
        geofence.CreatedAt = DateTime.UtcNow;
        geofence.AssignedVehicles = new List<GeofenceVehicle>();
        geofence.Events = new List<GeofenceEvent>();
        geofence.Group = null;
        geofence.Societe = null;

        _context.Geofences.Add(geofence);
        await _context.SaveChangesAsync();

        // Notify company admins
        var actorId = GetUserId();
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId);
        if (actor != null && companyId > 0)
        {
            _ = _publisher.Publish(new AdminActionNotificationEvent(
                companyId, actorId, actor.FullName,
                "geofence_created", geofence.Name, geofence.Id, "geofence"
            ));
        }

        return CreatedAtAction(nameof(GetGeofence), new { id = geofence.Id }, geofence);
    }

    /// <summary>
    /// Modification d'une zone, bascule actif/inactif comprise (l'écran envoie la zone
    /// ENTIÈRE avec <c>isActive</c> inversé : tous les champs ci-dessous sont recopiés).
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateGeofence(int id, [FromBody] Geofence updated)
    {
        var companyId = GetCompanyId();
        var porteeVehicules = await PorteeVehiculesAsync();

        if (await RefusEcritureZoneAsync(companyId, id, porteeVehicules) is { } refus) return refus;

        var geofence = await _context.Geofences
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (geofence == null)
            return NotFound();

        // Le groupe n'est contrôlé que s'il CHANGE : une zone visible appartient forcément
        // à un groupe visible (voir PorteeGroupesAsync), son groupe actuel passe donc.
        if (updated.GroupId != geofence.GroupId
            && await RefusGroupeAsync(companyId, updated.GroupId, porteeVehicules) is { } refusGroupe)
            return refusGroupe;

        geofence.Name = updated.Name;
        geofence.Description = updated.Description;
        geofence.Type = updated.Type;
        geofence.Color = updated.Color;
        geofence.Coordinates = updated.Coordinates;
        geofence.CenterLat = updated.CenterLat;
        geofence.CenterLng = updated.CenterLng;
        geofence.Radius = updated.Radius;
        geofence.AlertOnEntry = updated.AlertOnEntry;
        geofence.AlertOnExit = updated.AlertOnExit;
        geofence.AutoStopOnEntry = updated.AutoStopOnEntry;
        geofence.AlertSpeedLimit = updated.AlertSpeedLimit;
        geofence.NotificationCooldownMinutes = updated.NotificationCooldownMinutes;
        geofence.MaxStayDurationMinutes = updated.MaxStayDurationMinutes;
        geofence.ActiveStartTime = updated.ActiveStartTime;
        geofence.ActiveEndTime = updated.ActiveEndTime;
        geofence.ActiveDays = updated.ActiveDays;
        geofence.GroupId = updated.GroupId;
        geofence.IsActive = updated.IsActive;
        geofence.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteGeofence(int id)
    {
        var companyId = GetCompanyId();

        if (await RefusEcritureZoneAsync(companyId, id, await PorteeVehiculesAsync()) is { } refus) return refus;

        var geofence = await _context.Geofences
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (geofence == null)
            return NotFound();

        _context.Geofences.Remove(geofence);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}/vehicles")]
    public async Task<ActionResult> AssignVehicles(int id, [FromBody] int[] vehicleIds)
    {
        var companyId = GetCompanyId();
        var porteeVehicules = await PorteeVehiculesAsync();

        if (await RefusEcritureZoneAsync(companyId, id, porteeVehicules) is { } refus) return refus;

        var geofence = await _context.Geofences
            .Include(g => g.AssignedVehicles)
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (geofence == null)
            return NotFound();

        // La liaison n'était contrôlée NULLE PART : l'appel acceptait n'importe quel
        // identifiant de véhicule, y compris celui d'une AUTRE société — de quoi faire
        // entrer le véhicule d'autrui dans une zone et déclencher ses alertes. On ne
        // retient donc que les véhicules de la société de l'appelant, et un identifiant
        // étranger est un refus franc, pas un silence.
        //
        // Un restreint (règle du 23/09/2026) ne rattache en plus que les véhicules de SA
        // portée. Inexistant, d'une autre société ou d'un autre locataire : même réponse,
        // pour que l'appel ne serve pas à sonder le parc.
        var demandes = (vehicleIds ?? Array.Empty<int>()).Distinct().ToList();
        var autorises = _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId && demandes.Contains(v.Id));
        if (porteeVehicules is not null)
        {
            List<int> portee = porteeVehicules;
            autorises = autorises.Where(v => portee.Contains(v.Id));
        }
        var duParc = await autorises.Select(v => v.Id).ToListAsync();

        if (duParc.Count != demandes.Count)
            return BadRequest(new
            {
                message = porteeVehicules is null
                    ? "Un ou plusieurs véhicules n'appartiennent pas à votre société."
                    : "Un ou plusieurs véhicules sont introuvables ou hors de votre périmètre."
            });

        // Remplacement de la liste — mais seulement de la PART de l'appelant. L'écran d'un
        // restreint ne reçoit que SES véhicules (projection VersDto) et renvoie donc
        // seulement ceux-là : vider toute la liste aurait silencieusement défait la
        // liaison du véhicule d'un autre locataire à chaque enregistrement, et retiré ce
        // véhicule de la surveillance de la zone. Administrateur : toute la liste.
        var retenus = duParc.ToHashSet();
        var aRetirer = geofence.AssignedVehicles
            .Where(gv => (porteeVehicules is null || porteeVehicules.Contains(gv.VehicleId))
                         && !retenus.Contains(gv.VehicleId))
            .ToList();
        foreach (var liaison in aRetirer)
            geofence.AssignedVehicles.Remove(liaison);

        var dejaLies = geofence.AssignedVehicles.Select(gv => gv.VehicleId).ToHashSet();
        foreach (var vehicleId in duParc.Where(v => !dejaLies.Contains(v)))
        {
            geofence.AssignedVehicles.Add(new GeofenceVehicle
            {
                GeofenceId = id,
                VehicleId = vehicleId
            });
        }

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id}/events")]
    public async Task<ActionResult<List<GeofenceEventDto>>> GetGeofenceEvents(
        int id, 
        [FromQuery] int limit = 50,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var companyId = GetCompanyId();
        limit = BornerLimitePassages(limit);

        var geofence = await _context.Geofences
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (geofence == null)
            return NotFound();

        // DEUX filtres, et les deux comptent : la zone doit être visible (règle du
        // 23/09), et un passage est l'entrée ou la sortie d'un VÉHICULE — la ligne
        // publie sa plaque, sa position et son adresse.
        var porteeVehicules = await PorteeVehiculesAsync();
        var porteeZones = await PorteeGeozonesAsync(companyId, porteeVehicules);
        if (porteeZones is not null && !porteeZones.Contains(id))
            return NotFound();

        var query = _context.GeofenceEvents.Where(e => e.GeofenceId == id);

        if (porteeVehicules is not null)
        {
            List<int> vehiculeIds = porteeVehicules;
            query = query.Where(e => vehiculeIds.Contains(e.VehicleId));
        }

        if (!string.IsNullOrEmpty(type))
            query = query.Where(e => e.Type == type);

        if (from.HasValue)
            query = query.Where(e => e.Timestamp >= from.Value);

        if (to.HasValue)
            query = query.Where(e => e.Timestamp <= to.Value);

        var events = await query
            .Include(e => e.Vehicle)
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .Select(e => new GeofenceEventDto
            {
                Id = e.Id,
                GeofenceId = e.GeofenceId,
                VehicleId = e.VehicleId,
                VehicleName = e.Vehicle != null ? (e.Vehicle.Plate ?? e.Vehicle.Name) : null,
                Type = e.Type,
                Latitude = e.Latitude,
                Longitude = e.Longitude,
                Address = e.Address,
                Speed = e.Speed,
                DurationInsideSeconds = e.DurationInsideSeconds,
                IsNotified = e.IsNotified,
                Timestamp = e.Timestamp
            })
            .ToListAsync();

        return Ok(events);
    }

    // ==================== ALL GEOFENCE EVENTS ====================

    [HttpGet("events")]
    public async Task<ActionResult<List<GeofenceEventDto>>> GetAllEvents(
        [FromQuery] int? geofenceId = null,
        [FromQuery] int? vehicleId = null,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int limit = 50)
    {
        var companyId = GetCompanyId();
        limit = BornerLimitePassages(limit);

        // Get geofence IDs for this company
        var geofenceIds = await _context.Geofences
            .Where(g => g.CompanyId == companyId)
            .Select(g => g.Id)
            .ToListAsync();

        // Zones visibles (règle du 23/09) ET véhicules de la portée : la liste des
        // passages porte le nom de la zone ET la plaque du véhicule.
        var porteeVehicules = await PorteeVehiculesAsync();
        var porteeZones = await PorteeGeozonesAsync(companyId, porteeVehicules);
        if (porteeZones is not null)
        {
            List<int> zoneIds = porteeZones;
            geofenceIds = geofenceIds.Where(g => zoneIds.Contains(g)).ToList();
        }

        var query = _context.GeofenceEvents
            .Where(e => geofenceIds.Contains(e.GeofenceId));

        if (porteeVehicules is not null)
        {
            List<int> vehiculeIds = porteeVehicules;
            query = query.Where(e => vehiculeIds.Contains(e.VehicleId));
        }

        if (geofenceId.HasValue)
            query = query.Where(e => e.GeofenceId == geofenceId.Value);

        if (vehicleId.HasValue)
            query = query.Where(e => e.VehicleId == vehicleId.Value);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(e => e.Type == type);

        if (startDate.HasValue)
            query = query.Where(e => e.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(e => e.Timestamp <= endDate.Value);

        var events = await query
            .Include(e => e.Vehicle)
            .Include(e => e.Geofence)
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .Select(e => new GeofenceEventDto
            {
                Id = e.Id,
                GeofenceId = e.GeofenceId,
                GeofenceName = e.Geofence != null ? e.Geofence.Name : null,
                VehicleId = e.VehicleId,
                VehicleName = e.Vehicle != null ? (e.Vehicle.Plate ?? e.Vehicle.Name) : null,
                Type = e.Type,
                Latitude = e.Latitude,
                Longitude = e.Longitude,
                Address = e.Address,
                Speed = e.Speed,
                DurationInsideSeconds = e.DurationInsideSeconds,
                IsNotified = e.IsNotified,
                Timestamp = e.Timestamp
            })
            .ToListAsync();

        return Ok(events);
    }

    // ==================== GEOFENCE GROUPS ====================

    [HttpGet("groups")]
    public async Task<ActionResult<List<GeofenceGroup>>> GetGroups()
    {
        var companyId = GetCompanyId();

        // FUITE FERMÉE LE 23/09/2026 : « /api/geofences/groups » embarque les ZONES du
        // groupe (Include), entités complètes avec leur nom et leur géométrie. La liste
        // des zones était donc lisible ici en entier alors que « /api/geofences » venait
        // d'être cloisonnée — même donnée, autre URL, et toujours sans la case Géofences
        // (passe-droit IsSharedReferenceRead sur les GET). Deux filtres : on ne garde que
        // les groupes visibles (voir PorteeGroupesAsync : au moins une zone visible, ou
        // groupe VIDE pour un gestionnaire de zones), et dans chaque groupe que les zones
        // visibles. Portée nulle = administrateur, rien ne change.
        var porteeVehicules = await PorteeVehiculesAsync();
        var porteeGroupes = await PorteeGroupesAsync(companyId, porteeVehicules);

        var query = _context.GeofenceGroups
            .AsNoTracking()
            .Where(g => g.CompanyId == companyId);

        List<GeofenceGroup> groups;
        if (porteeGroupes is null)
        {
            groups = await query.Include(g => g.Geofences).OrderBy(g => g.Name).ToListAsync();
        }
        else
        {
            // Liste VIDE = aucun groupe, surtout pas « tous ».
            List<int> groupeIds = porteeGroupes;
            List<int> zoneIds = await PorteeGeozonesAsync(companyId, porteeVehicules) ?? new List<int>();
            groups = await query
                .Where(g => groupeIds.Contains(g.Id))
                .Include(g => g.Geofences.Where(z => zoneIds.Contains(z.Id)))
                .OrderBy(g => g.Name)
                .ToListAsync();
        }

        return Ok(groups);
    }

    /// <summary>
    /// Écriture sur un groupe existant : permise dès que l'appelant VOIT le groupe (même
    /// règle que les zones), 404 sinon — jamais de révélation d'existence.
    /// </summary>
    private async Task<ActionResult?> RefusEcritureGroupeAsync(int companyId, int groupeId)
    {
        var porteeGroupes = await PorteeGroupesAsync(companyId, await PorteeVehiculesAsync());
        return porteeGroupes is null || porteeGroupes.Contains(groupeId) ? null : NotFound();
    }

    [HttpPost("groups")]
    public async Task<ActionResult<GeofenceGroup>> CreateGroup([FromBody] GeofenceGroup group)
    {
        var companyId = GetCompanyId();

        if (!await GereLesZonesAsync(companyId, await PorteeVehiculesAsync())) return Forbid();

        // Même garde que pour une zone : le corps est l'ENTITÉ, on n'en garde que les
        // champs du groupe — des « geofences » glissées dans le JSON créeraient des zones
        // par la bande.
        group.Id = 0;
        group.CompanyId = companyId;
        group.CreatedAt = DateTime.UtcNow;
        group.Geofences = new List<Geofence>();
        group.Societe = null;

        _context.GeofenceGroups.Add(group);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetGroups), new { id = group.Id }, group);
    }

    [HttpPut("groups/{id}")]
    public async Task<ActionResult> UpdateGroup(int id, [FromBody] GeofenceGroup updated)
    {
        var companyId = GetCompanyId();

        if (await RefusEcritureGroupeAsync(companyId, id) is { } refus) return refus;

        var group = await _context.GeofenceGroups
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (group == null)
            return NotFound();

        group.Name = updated.Name;
        group.Description = updated.Description;
        group.Color = updated.Color;
        group.IconName = updated.IconName;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("groups/{id}")]
    public async Task<ActionResult> DeleteGroup(int id)
    {
        var companyId = GetCompanyId();

        if (await RefusEcritureGroupeAsync(companyId, id) is { } refus) return refus;

        var group = await _context.GeofenceGroups
            .FirstOrDefaultAsync(g => g.Id == id && g.CompanyId == companyId);

        if (group == null)
            return NotFound();

        _context.GeofenceGroups.Remove(group);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ==================== STATISTICS ====================

    [HttpGet("stats")]
    public async Task<ActionResult> GetGeofenceStats([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var companyId = GetCompanyId();
        from ??= DateTime.UtcNow.AddDays(-30);
        to ??= DateTime.UtcNow;

        var geofenceIds = await _context.Geofences
            .Where(g => g.CompanyId == companyId)
            .Select(g => g.Id)
            .ToListAsync();

        // Mêmes deux filtres que la liste des passages : zones visibles et véhicules
        // de la portée. Sinon les compteurs (entrées, sorties, excès de vitesse) sont
        // ceux de tout le parc, sous une autre URL que le tableau de bord.
        var porteeVehicules = await PorteeVehiculesAsync();
        var porteeZones = await PorteeGeozonesAsync(companyId, porteeVehicules);
        if (porteeZones is not null)
        {
            List<int> zoneIds = porteeZones;
            geofenceIds = geofenceIds.Where(g => zoneIds.Contains(g)).ToList();
        }

        var eventsQuery = _context.GeofenceEvents
            .Where(e => geofenceIds.Contains(e.GeofenceId) && e.Timestamp >= from && e.Timestamp <= to);

        if (porteeVehicules is not null)
        {
            List<int> vehiculeIds = porteeVehicules;
            eventsQuery = eventsQuery.Where(e => vehiculeIds.Contains(e.VehicleId));
        }

        var events = await eventsQuery
            .GroupBy(e => e.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalGeofences = geofenceIds.Count;
        var toutesZones = porteeZones is null;
        List<int> zonesVisibles = geofenceIds;
        var activeGeofences = await _context.Geofences
            .Where(g => g.CompanyId == companyId && g.IsActive)
            .CountAsync(g => toutesZones || zonesVisibles.Contains(g.Id));

        return Ok(new
        {
            TotalGeofences = totalGeofences,
            ActiveGeofences = activeGeofences,
            TotalEntries = events.FirstOrDefault(e => e.Type == "entry")?.Count ?? 0,
            TotalExits = events.FirstOrDefault(e => e.Type == "exit")?.Count ?? 0,
            TotalSpeedViolations = events.FirstOrDefault(e => e.Type == "speed_violation")?.Count ?? 0,
            TotalOverstays = events.FirstOrDefault(e => e.Type == "overstay")?.Count ?? 0,
            Period = new { From = from, To = to }
        });
    }
}

// ==================== DTOs ====================

public class GeofenceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = "polygon";
    public string Color { get; set; } = "#3b82f6";
    public string? IconName { get; set; }
    public GeofencePoint[]? Coordinates { get; set; }
    public double? CenterLat { get; set; }
    public double? CenterLng { get; set; }
    public double? Radius { get; set; }
    public bool AlertOnEntry { get; set; }
    public bool AlertOnExit { get; set; }
    public bool AutoStopOnEntry { get; set; }
    public int? AlertSpeedLimit { get; set; }
    public int NotificationCooldownMinutes { get; set; }
    public int? MaxStayDurationMinutes { get; set; }
    public TimeSpan? ActiveStartTime { get; set; }
    public TimeSpan? ActiveEndTime { get; set; }
    public string[]? ActiveDays { get; set; }
    public bool IsActive { get; set; }
    public int? GroupId { get; set; }
    public string? GroupName { get; set; }
    public List<int> AssignedVehicleIds { get; set; } = new();
    public List<string> AssignedVehicleNames { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class GeofenceEventDto
{
    public int Id { get; set; }
    public int GeofenceId { get; set; }
    public string? GeofenceName { get; set; }
    public int VehicleId { get; set; }
    public string? VehicleName { get; set; }
    public string Type { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
    public double? Speed { get; set; }
    public int? DurationInsideSeconds { get; set; }
    public bool IsNotified { get; set; }
    public DateTime Timestamp { get; set; }
}
