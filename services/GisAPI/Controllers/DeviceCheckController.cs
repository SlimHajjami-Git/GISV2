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
/// <para><b>Conséquence côté écran, à trancher par Slim :</b> la page Angular
/// « /device-check » est aujourd'hui publique (aucun <c>AuthGuard</c> sur la route) et
/// l'intercepteur retire volontairement le jeton des appels « /devicecheck/ ». Tant que le
/// frontend n'est pas repris, cette page recevra 401. La route n'est pas supprimée : elle a
/// un usage légitime d'outil interne, mais elle est désormais réservée aux comptes
/// connectés et au périmètre de l'appelant.</para>
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
        var lastPosition = await _context.GpsPositions.AsNoTracking()
            .Where(p => p.DeviceId == device.Id)
            .OrderByDescending(p => p.Id)
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

        // Connection status: last frame > 40 min = stale (grey)
        var lastFrameTime = lastPosition.RecordedAt;
        var minutesSinceLastFrame = (DateTime.UtcNow - lastFrameTime).TotalMinutes;
        var isStale = minutesSinceLastFrame > 40;

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
