using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Services;

/// <summary>
/// Garde-fou anti-doublons des identifiants boîtier GPS : IMEI, MAT, numéro SIM.
///
/// Contexte : la base de prod contenait ~40 doublons (audit du 15/07/2026) — dont
/// deux boîtiers partageant la SIM 92002732 (véhicules HTZ 159 et 262 TU 8165),
/// source de confusion d'attribution des trames. Seul l'IMEI a un index unique en
/// base ; MAT et SIM n'en ont pas (et ne peuvent pas en recevoir tant que les
/// doublons historiques ne sont pas purgés). Ce garde est donc la barrière au
/// niveau applicatif : AUCUNE création/modification de véhicule ne doit produire
/// deux boîtiers partageant un IMEI, un MAT ou une SIM.
///
/// La comparaison est NORMALISÉE (espaces supprimés, casse ignorée) pour attraper
/// « 92 002 732 » vs « 92002732 » — c'est exactement ainsi que les doublons
/// historiques sont passés.
/// </summary>
public static class GpsDeviceUniquenessGuard
{
    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    /// <summary>
    /// Cherche un AUTRE boîtier (≠ <paramref name="excludeDeviceId"/>) portant déjà
    /// l'un des identifiants fournis. Retourne un message d'erreur français précis
    /// (quel identifiant, quel boîtier, quel véhicule) ou null si aucun conflit.
    /// La table gps_devices est petite (quelques centaines de lignes) : on compare
    /// en mémoire pour appliquer la même normalisation que ci-dessus.
    /// </summary>
    public static async Task<string?> FindConflictAsync(
        IGisDbContext context, int excludeDeviceId,
        string? imei, string? mat, string? sim,
        CancellationToken ct = default)
    {
        var nImei = Normalize(imei);
        var nMat = Normalize(mat);
        var nSim = Normalize(sim);
        if (nImei.Length == 0 && nMat.Length == 0 && nSim.Length == 0) return null;

        var others = await context.GpsDevices
            .AsNoTracking()
            .Where(d => d.Id != excludeDeviceId)
            .Select(d => new
            {
                d.Id,
                d.DeviceUid,
                d.Mat,
                d.SimNumber,
                VehicleLabel = d.Vehicle != null ? (d.Vehicle.Plate ?? d.Vehicle.Name) : null
            })
            .ToListAsync(ct);

        foreach (var o in others)
        {
            if (nImei.Length > 0 && Normalize(o.DeviceUid) == nImei)
                return Conflict("l'IMEI", imei!, o.Id, o.VehicleLabel);
            if (nMat.Length > 0 && Normalize(o.Mat) == nMat)
                return Conflict("le MAT", mat!, o.Id, o.VehicleLabel);
            if (nSim.Length > 0 && Normalize(o.SimNumber) == nSim)
                return Conflict("le numéro SIM", sim!, o.Id, o.VehicleLabel);
        }
        return null;
    }

    private static string Conflict(string what, string value, int deviceId, string? vehicleLabel) =>
        $"Doublon refusé : {what} « {value.Trim()} » est déjà utilisé par le boîtier #{deviceId}" +
        (vehicleLabel != null ? $" (véhicule {vehicleLabel})." : " (boîtier non affecté).");
}
