using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using System.Text.RegularExpressions;

namespace GisAPI.Controllers;

/// <summary>
/// Outil d'installation : « ce boîtier remonte-t-il ? », par IMEI, par MAT ou par plaque.
///
/// <para><b>Cette classe ne portait AUCUN attribut <c>[Authorize]</c></b>, et l'application
/// ne déclare aucune <c>FallbackPolicy</c> (Program.cs ne fait qu'enchaîner
/// <c>UseAuthentication()</c> / <c>UseAuthorization()</c>) : la route répondait 200 avec un
/// corps JSON complet SANS le moindre jeton. <c>PermissionMiddleware</c> ne rattrapait rien
/// — il rend la main immédiatement quand la requête n'est pas authentifiée. La requête
/// levait en plus les filtres multi-tenant (<c>IgnoreQueryFilters()</c>) sur GpsDevices ET
/// sur Vehicles : elle traversait donc TOUTES les sociétés. Avec une plaque — qui se lit
/// dans la rue — n'importe qui obtenait la dernière position, le contact, les coordonnées,
/// le compteur, le carburant et l'heure de dernière trame de n'importe quel véhicule de
/// n'importe quel client. Le commentaire XML affirmait « requires authentication » : c'est
/// précisément ce mensonge qui a masqué le trou.</para>
///
/// <para>Fermeture en trois temps : authentification exigée, filtres multi-tenant rétablis
/// (la société de l'appelant), puis la PORTÉE VÉHICULE commune à tous les écrans. La portée
/// a TROIS états : <c>null</c> = administrateur, aucun filtre ; liste non vide = ses
/// véhicules ; liste VIDE = il ne voit RIEN. L'administrateur sans aucune affectation
/// (utilisateur 11 de HERTZ) continue donc de tout voir.</para>
///
/// <para><b>Tranché par Slim le 28/09/2026 — DEUX routes, deux réponses.</b> L'écran servait
/// aux installateurs sur le terrain, et leur imposer un compte n'avait pas de sens. La
/// classe expose donc :</para>
/// <list type="bullet">
/// <item><c>GET lookup</c> — AUTHENTIFIÉE, inchangée : réponse complète (position, plaque,
/// contact, carburant, compteur), bornée à la société de l'appelant et à sa portée
/// véhicule. C'est l'outil interne.</item>
/// <item><c>GET status</c> — PUBLIQUE, par IMEI ou par MATRICULE DE BOÎTIER, et réduite à
/// « cet appareil est-il enregistré et envoie-t-il des trames ». Aucune donnée qui
/// rattache un boîtier à un client. La PLAQUE du véhicule n'y est jamais acceptée : c'est
/// elle, lisible dans la rue, qui faisait le danger. Voir <see cref="StatutPublic"/> pour
/// le raisonnement complet.</item>
/// </list>
/// <para>Ce découpage en deux actions distinctes est volontaire : une seule action qui
/// choisirait son contenu selon l'authentification finirait par laisser fuir la réponse
/// complète au premier oubli de branche. Ici, la route publique n'interroge même pas la
/// table des véhicules.</para>
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeviceCheckController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeviceCheckController(GisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    private static string MaskImei(string imei) =>
        imei.Length > 4 ? new string('*', imei.Length - 4) + imei[^4..] : imei;

    /// <summary>
    /// Réponse unique pour « rien à montrer » : identifiant inconnu, boîtier d'une autre
    /// société, ou véhicule hors du périmètre de l'appelant. Le refus ne doit JAMAIS se
    /// distinguer de l'absence, sinon la route devient un oracle d'existence de plaques.
    /// </summary>
    private ActionResult Introuvable() =>
        Ok(new { found = false, message = "MAT/IMEI introuvable." });

    /// <summary>
    /// Au-delà de ce silence, on considère que le boîtier ne remonte plus. Une seule
    /// définition pour les deux routes : la publique et l'authentifiée ne doivent pas
    /// pouvoir répondre « connecté » et « déconnecté » du même boîtier à la même seconde.
    /// </summary>
    public const double SilenceAvantDeconnexionMinutes = 40;

    /// <summary>
    /// IMEI strict : 15 chiffres, rien d'autre. La route PUBLIQUE n'accepte que cette clé,
    /// jamais une plaque ni une MAT. C'est la plaque — qui se lit dans la rue — qui faisait
    /// de l'ancienne route ouverte un traceur de véhicules d'autrui ; un IMEI, lui, se lit
    /// sur l'étiquette du boîtier que l'installateur tient en main.
    /// </summary>
    private static readonly Regex ImeiStrict = new(@"^\d{15}$", RegexOptions.Compiled);

    /// <summary>
    /// Matricule de boîtier : lettres et chiffres collés, rien d'autre. Relevé sur la
    /// production le 29/09/2026 : 439 des 440 matricules sont de cette forme (type
    /// « NR08G1075 », 10 caractères au plus), le dernier est un IMEI à 15 chiffres, et
    /// AUCUN ne contient d'espace.
    ///
    /// <para><b>C'est l'absence d'espace qui protège.</b> Une plaque tunisienne s'écrit
    /// « 233 TU 5102 », avec des espaces : elle ne franchit pas ce filtre. Et même saisie
    /// collée, elle ne trouverait rien, puisque cette route n'interroge QUE la table des
    /// boîtiers — jamais celle des véhicules. La plaque, qui se lit dans la rue, reste
    /// donc hors de portée d'un visiteur anonyme, ce qui était tout l'objet de la
    /// fermeture du 23/09.</para>
    /// </summary>
    private static readonly Regex MatriculeStrict = new(@"^[A-Za-z0-9]{4,20}$", RegexOptions.Compiled);

    /// <summary>
    /// « Ce boîtier remonte-t-il ? » SANS authentification, pour l'installateur sur le
    /// terrain (décision de Slim du 28/09/2026 : la page redevient publique, mais réduite à
    /// ce que l'installation exige).
    ///
    /// <para><b>Ce que cette route ne dit JAMAIS</b>, et c'est précisément ce qui la rend
    /// publiable : aucune position, aucune plaque, aucun nom de véhicule ni de société,
    /// aucun contact moteur, aucun carburant, aucun compteur. Elle répond à une seule
    /// question — cet appareil est-il enregistré, et envoie-t-il des trames. Connaître un
    /// IMEI ne permet donc plus de suivre un véhicule, ce qui était le trou de l'ancienne
    /// route ouverte. La route authentifiée <c>lookup</c> garde, elle, la réponse complète,
    /// le cloisonnement par société et la portée véhicule de l'appelant.</para>
    ///
    /// <para><b>Deux clés depuis le 29/09/2026 : l'IMEI ET le matricule du boîtier.</b>
    /// Les techniciens ont rapporté que sur le terrain ils lisent le MATRICULE de
    /// l'étiquette, pas l'IMEI — et le champ, restreint aux chiffres, leur présentait un
    /// pavé numérique sur lequel un matricule alphanumérique est intaisissable. Les deux
    /// clés sont sur l'appareil qu'ils tiennent en main, donc le risque est le même ;
    /// c'est la PLAQUE, lisible dans la rue, qui reste exclue. Un matricule pouvant être
    /// porté par deux boîtiers (2 cas sur 438 en production), l'ambiguïté est DITE et non
    /// tranchée au hasard : annoncer « ça remonte » d'après le boîtier d'un autre véhicule
    /// ferait repartir le technicien sur une pose qui ne marche pas.</para>
    ///
    /// <para>Elle lève les filtres multi-tenant, et il ne peut en être autrement :
    /// l'appelant n'a pas de société. C'est sans conséquence ici puisque rien dans la
    /// réponse ne rattache le boîtier à un client. Le seul reste est qu'elle dit si un IMEI
    /// est connu : d'où le plafond par adresse IP posé dans <c>RateLimitPolicies</c>, sans
    /// lequel on balaierait l'espace des IMEI pour cartographier le parc.</para>
    /// </summary>
    [AllowAnonymous]
    [HttpGet("status")]
    public async Task<ActionResult> StatutPublic([FromQuery] string? q = null, [FromQuery] string? imei = null)
    {
        // Deux noms pour la même chose : « q » depuis le 29/09/2026, « imei » gardé pour
        // qu'un navigateur n'ayant pas encore rechargé son JavaScript continue de marcher
        // pendant la bascule. Sans cet alias, le technicien qui a la page ouverte au moment
        // du déploiement reçoit un refus de format sans comprendre pourquoi.
        var saisie = (q ?? imei ?? string.Empty).Trim();

        var estImei = ImeiStrict.IsMatch(saisie);
        var estMatricule = MatriculeStrict.IsMatch(saisie);

        if (!estImei && !estMatricule)
            return BadRequest(new
            {
                error = "Saisissez l'IMEI (15 chiffres) ou le matricule du boîtier, "
                      + "sans espace. La plaque du véhicule n'est pas acceptée ici."
            });

        // Projection explicite : on ne charge QUE les trois champs nécessaires. Aucun
        // accès à Vehicles, donc aucune plaque ni société ne peut fuir par inadvertance,
        // même si quelqu'un ajoute un champ à la réponse plus tard.
        //
        // IgnoreQueryFilters est ici un choix de COHÉRENCE, pas un contournement : le filtre
        // de GpsDevices laisse déjà tout passer quand l'appelant n'a pas de société, donc
        // sans jeton il ne change rien. Mais si un utilisateur connecté appelait cette route
        // directement, le filtre s'appliquerait et le même IMEI répondrait « trouvé » à l'un
        // et « inconnu » à l'autre. L'expliciter garantit une réponse identique pour tous.
        // Le filtre de cette entité est purement multi-locataire (aucune suppression
        // logique), donc le lever n'exhume rien d'effacé.
        // Le matricule se compare sans tenir compte de la casse : il est écrit en
        // majuscules en base, et un technicien qui le recopie de l'étiquette tape souvent
        // en minuscules. L'IMEI, lui, n'a que des chiffres : la casse n'a pas de sens.
        var saisieMinuscule = saisie.ToLowerInvariant();

        // Une recherche par IMEI ne regarde QUE device_uid, qui est unique et indexé : elle
        // garde son index, et ne peut pas être déclarée « ambiguë » parce qu'un matricule
        // vaudrait l'IMEI d'un autre boîtier. Une recherche par matricule ne regarde que
        // mat. Deux résultats au plus suffisent à détecter l'ambiguïté et bornent la
        // lecture : sur la production, 2 matricules sur 438 sont portés par deux boîtiers.
        var correspondances = await _context.GpsDevices.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(d => estImei
                ? d.DeviceUid == saisie
                : d.Mat != null && d.Mat.ToLower() == saisieMinuscule)
            .OrderBy(d => d.Id)
            .Select(d => new { d.Id, d.DeviceUid, d.SignalStrength })
            .Take(2)
            .ToListAsync(HttpContext.RequestAborted);

        if (correspondances.Count == 0)
            return Ok(new
            {
                found = false,
                message = estImei ? "IMEI inconnu." : "Matricule inconnu."
            });

        // Ambiguïté DITE, jamais tranchée au hasard : répondre « ce boîtier remonte » en
        // ayant regardé celui d'un autre véhicule enverrait le technicien repartir alors
        // que sa pose ne fonctionne pas. L'IMEI, lui, est unique.
        if (correspondances.Count > 1)
            return Ok(new
            {
                found = true,
                ambiguous = true,
                message = "Plusieurs boîtiers portent ce matricule. "
                        + "Cherchez par IMEI pour lever le doute."
            });

        var boitier = correspondances[0];

        // L'IMEI n'est RENVOYÉ QUE si l'appelant l'a lui-même saisi — auquel cas on ne lui
        // apprend rien. Cherché par matricule, il n'est pas renvoyé du tout.
        //
        // Pourquoi c'est important, et pourquoi ce n'était pas évident : les matricules
        // suivent un gabarit très étroit — 423 des 440 de la production s'écrivent
        // « NR08G » suivi de quatre chiffres, soit 10 000 possibilités, contre 10^15 pour
        // un IMEI. Le plafond de débit borne la vitesse, pas le volume : le parc entier se
        // balaie en moins d'une heure. Renvoyer les 4 derniers chiffres de l'IMEI à chaque
        // touche permettait alors de le RECONSTITUER pour presque tout le parc, puisque
        // 406 boîtiers partagent le même préfixe constructeur. Or l'IMEI est l'identifiant
        // que l'ingestion GPS accepte : le laisser se déduire d'un matricule devinable
        // revenait à publier la clé d'entrée des trames.
        var imeiAEcho = estImei ? MaskImei(boitier.DeviceUid) : null;

        // TRI SUR recorded_at, JAMAIS SUR id — mesuré sur la production le 28/09/2026.
        // « ORDER BY id DESC LIMIT 1 » remonte l'index de la clé primaire depuis la trame la
        // plus récente de TOUTE la flotte en filtrant device_id au passage : pour un boîtier
        // qui n'a jamais émis, il n'y a rien à trouver et le parcours va jusqu'au bout des
        // 30 Go — la requête a DÉPASSÉ 120 secondes. Or « le boîtier ne remonte pas » est
        // précisément le cas que l'installateur vient vérifier, et cette route est publique :
        // c'était un moyen de saturer la base avec l'IMEI d'un boîtier neuf.
        // « ORDER BY recorded_at DESC » colle à ix_gps_positions_device_time (device_id,
        // recorded_at) : 0,072 ms et 4 blocs lus sur le même boîtier.
        var derniere = await _context.GpsPositions.AsNoTracking()
            .Where(p => p.DeviceId == boitier.Id)
            .OrderByDescending(p => p.RecordedAt)
            .Select(p => new { p.RecordedAt, p.Satellites })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (derniere == null)
            return Ok(new
            {
                found = true,
                reporting = false,
                imei = imeiAEcho,
                message = "Boîtier enregistré, mais aucune trame reçue à ce jour."
            });

        var minutes = (DateTime.UtcNow - derniere.RecordedAt).TotalMinutes;

        return Ok(new
        {
            found = true,
            reporting = minutes <= SilenceAvantDeconnexionMinutes,
            imei = imeiAEcho,
            lastFrameAt = derniere.RecordedAt,
            minutesSinceLastFrame = Math.Round(minutes, 1),
            satellites = derniere.Satellites,
            signalStrength = boitier.SignalStrength
        });
    }

    /// <summary>
    /// Recherche d'un boîtier par IMEI, par MAT ou par plaque, dans la SOCIÉTÉ DE L'APPELANT
    /// et dans SON périmètre de véhicules. Rend l'état de connexion, le contact, les
    /// coordonnées, le compteur, le carburant et l'heure de dernière trame.
    /// </summary>
    [HttpGet("lookup")]
    public async Task<ActionResult> Lookup([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Veuillez fournir un IMEI ou une MAT." });

        var query = q.Trim();

        // Plus aucun IgnoreQueryFilters ici : les filtres globaux de GisDbContext bornent
        // GpsDevices et Vehicles à la société de l'appelant (et ne s'effacent que pour
        // l'administrateur système, qui a la vue plate-forme par définition).
        var device = await _context.GpsDevices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DeviceUid == query);

        Vehicle? vehicle = null;

        if (device != null)
        {
            vehicle = await _context.Vehicles.AsNoTracking()
                .FirstOrDefaultAsync(v => v.GpsDeviceId == device.Id);
        }
        else
        {
            // Try by MAT on device
            device = await _context.GpsDevices.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Mat != null && d.Mat.ToLower() == query.ToLower());

            if (device != null)
            {
                vehicle = await _context.Vehicles.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.GpsDeviceId == device.Id);
            }
            else
            {
                // Try by vehicle plate
                vehicle = await _context.Vehicles.AsNoTracking()
                    .Include(v => v.GpsDevice)
                    .FirstOrDefaultAsync(v => v.Plate != null && v.Plate.ToLower() == query.ToLower());

                if (vehicle?.GpsDevice != null)
                    device = vehicle.GpsDevice;
            }
        }

        if (device == null && vehicle == null)
            return Introuvable();

        // Portée véhicule. Un boîtier en stock, rattaché à AUCUN véhicule, n'entre dans la
        // portée de personne : seul un appelant qui voit tout le parc le consulte.
        var dansLaPortee = vehicle != null
            ? await VehicleScope.CanAccessVehicleAsync(_context, _tenantService, vehicle.Id, HttpContext.RequestAborted)
            : VehicleScope.SeesWholeFleet(_tenantService);

        if (!dansLaPortee)
            return Introuvable();

        if (device == null)
            return Ok(new
            {
                found = true,
                hasGps = false,
                message = "Véhicule trouvé mais aucun boîtier GPS associé.",
                plate = vehicle?.Plate,
                vehicleName = vehicle?.Name
            });

        // Get last GPS position
        // GpsPositions ne porte AUCUN filtre multi-tenant (les trames sont indexées par
        // boîtier, pas par société) : le cloisonnement vient du boîtier résolu ci-dessus,
        // déjà borné à la société et à la portée de l'appelant.
        // Même correction que sur la route publique, et pour la même raison mesurée le
        // 28/09/2026 : trié sur « id » cette requête a dépassé 120 secondes pour un boîtier
        // sans aucune trame — l'écran restait suspendu sur un boîtier fraîchement posé, soit
        // le cas le plus fréquent d'un technicien. Trié sur recorded_at, elle rend en moins
        // d'une milliseconde grâce à ix_gps_positions_device_time.
        var lastPosition = await _context.GpsPositions.AsNoTracking()
            .Where(p => p.DeviceId == device.Id)
            .OrderByDescending(p => p.RecordedAt)
            .FirstOrDefaultAsync();

        if (lastPosition == null)
            return Ok(new
            {
                found = true,
                hasGps = true,
                connected = false,
                message = "Aucune trame trouvée pour ce boîtier.",
                imei = MaskImei(device.DeviceUid),
                mat = device.Mat,
                plate = vehicle?.Plate,
                vehicleName = vehicle?.Name,
                firmwareVersion = device.FirmwareVersion,
                model = device.Model,
                status = device.Status
            });

        // Fuel conversion (same logic as GpsController / monitoring)
        int? fuelPercent = null;
        if (lastPosition.FuelRaw.HasValue && lastPosition.FuelRaw.Value > 0)
        {
            var fuelMode = device.FuelSensorMode ?? "raw_255";
            var tankCapacity = vehicle?.FuelTankCapacity ?? 60;
            var raw = lastPosition.FuelRaw.Value;
            fuelPercent = fuelMode switch
            {
                "percent" => raw,
                "raw_255" => (int)Math.Round(raw / 255.0 * 100.0),
                "liters" => tankCapacity > 0 ? (int)Math.Round(raw * 100.0 / tankCapacity) : raw,
                "half_liter" => tankCapacity > 0 ? (int)Math.Round(raw * 0.5 * 100.0 / tankCapacity) : (int)Math.Round(raw * 0.5),
                _ => raw
            };
            if (fuelPercent > 100) fuelPercent = 100;
            if (fuelPercent < 0) fuelPercent = 0;
        }

        // Connection status: au-delà de SilenceAvantDeconnexionMinutes, le boîtier est
        // considéré muet (affiché en gris). Même seuil que la route publique : les deux ne
        // doivent pas se contredire sur le même boîtier.
        var lastFrameTime = lastPosition.RecordedAt;
        var minutesSinceLastFrame = (DateTime.UtcNow - lastFrameTime).TotalMinutes;
        var isStale = minutesSinceLastFrame > SilenceAvantDeconnexionMinutes;

        // Calypso 7 — bug technicien : "Odomètre = 0 km" sur /device-check
        // pour un véhicule fraîchement installé alors que /monitoring affiche
        // bien le km. Cause : la logique était INVERSÉE par rapport à
        // GetVehiclesWithPositionsQueryHandler. Pour le firmware "L" (NEMS L),
        // c'est précisément le boîtier qui rapporte odometer_km via FMS — il
        // FAUT préférer position.OdometerKm. Pour les autres firmwares qui
        // n'ont pas la donnée FMS, on retombe sur vehicle.Mileage.
        //
        // Pour un vieux véhicule, vehicle.Mileage avait fini par être synchronisé
        // à une vraie valeur, donc l'inversion était invisible. Pour un nouveau
        // véhicule (Mileage=0 par défaut), elle affichait 0 km systématiquement.
        //
        // On filtre aussi 1048574 (sentinelle "capteur non initialisé") comme
        // dans le handler /monitoring.
        long? odometerKm = null;
        var fw = device.FirmwareVersion ?? "";
        if (fw.StartsWith("L", StringComparison.OrdinalIgnoreCase)
            && lastPosition.OdometerKm.HasValue
            && lastPosition.OdometerKm.Value > 0
            && lastPosition.OdometerKm.Value != 1048574)
        {
            odometerKm = lastPosition.OdometerKm.Value;
        }
        else if (vehicle != null)
        {
            odometerKm = vehicle.Mileage;
        }

        return Ok(new
        {
            found = true,
            hasGps = true,
            connected = !isStale,
            isStale,
            imei = MaskImei(device.DeviceUid),
            mat = device.Mat,
            plate = vehicle?.Plate,
            vehicleName = vehicle?.Name,
            firmwareVersion = device.FirmwareVersion,
            fuelSensorMode = device.FuelSensorMode,
            model = device.Model,
            deviceStatus = device.Status,
            lastPosition = new
            {
                latitude = lastPosition.Latitude,
                longitude = lastPosition.Longitude,
                speedKph = lastPosition.SpeedKph,
                ignitionOn = lastPosition.IgnitionOn,
                fuelPercent,
                fuelRaw = lastPosition.FuelRaw,
                odometerKm,
                address = lastPosition.Address,
                recordedAt = lastPosition.RecordedAt,
                satellites = lastPosition.Satellites,
                isValid = lastPosition.IsValid
            },
            minutesSinceLastFrame = Math.Round(minutesSinceLastFrame, 1)
        });
    }
}
