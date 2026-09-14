using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Vehicles.Services;

/// <summary>
/// Conflit d'identifiant détecté par <see cref="GpsDeviceUniquenessGuard"/>.
/// Structuré pour que les handlers puissent dire la vérité sur la situation
/// (ex. le boîtier en conflit est le boîtier actuel du véhicule édité) au lieu
/// d'un message générique — constat du 14/09/2026 sur HTZ 278 / 255 / 292.
/// </summary>
/// <param name="DeviceId">Boîtier qui porte déjà l'identifiant.</param>
/// <param name="Identifier">Identifiant en cause : « imei », « mat » ou « sim ».</param>
/// <param name="Value">Valeur demandée (trimée), telle que saisie.</param>
/// <param name="DeviceImei">IMEI (device_uid) du boîtier en conflit.</param>
/// <param name="VehicleId">Véhicule rattaché au boîtier en conflit, s'il y en a un.</param>
/// <param name="VehicleLabel">Plaque (ou nom) de ce véhicule.</param>
public sealed record GpsDeviceConflict(
    int DeviceId,
    string Identifier,
    string Value,
    string DeviceImei,
    int? VehicleId,
    string? VehicleLabel)
{
    public const string Imei = "imei";
    public const string Mat = "mat";
    public const string Sim = "sim";

    /// <summary>« l'IMEI », « le MAT », « le numéro SIM ».</summary>
    public string IdentifierLabel => Identifier switch
    {
        Imei => "l'IMEI",
        Mat => "le MAT",
        _ => "le numéro SIM"
    };

    /// <summary>Message historique, inchangé (l'écran s'appuie sur « Doublon refusé »).</summary>
    public string Message =>
        $"Doublon refusé : {IdentifierLabel} « {Value} » est déjà utilisé par le boîtier #{DeviceId}" +
        (VehicleLabel != null ? $" (véhicule {VehicleLabel})." : " (boîtier non affecté).");
}

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
    /// Valeur à enregistrer pour un identifiant saisi (IMEI, MAT). Par défaut : la saisie
    /// sans ses espaces de bord — une correction de casse ou d'espaces doit pouvoir
    /// s'enregistrer. Avec <paramref name="keepDeviceSpelling"/>, et seulement si la saisie
    /// ne diffère de la valeur stockée que par la casse ou des espaces, la valeur stockée
    /// est gardée.
    ///
    /// POURQUOI — l'ingestion (gps-ingest-rust/src/db.rs) retrouve un boîtier par son
    /// IMEI et contrôle son MAT à l'IDENTIQUE. Le 14/09/2026, la correction des IMEI mal
    /// saisis (HTZ 278, 255, 292) rattache le véhicule à la fiche qui émet : les valeurs
    /// du formulaire viennent alors d'une AUTRE fiche, et un MAT tapé en minuscules aurait
    /// fait rejeter les trames du boîtier. À l'inverse, quand l'opérateur modifie la fiche
    /// affichée dans son formulaire, sa saisie doit gagner : garder la valeur stockée
    /// rendait impossible la correction d'un MAT mal orthographié (contre-relecture du
    /// 14/09/2026). Les appelants ne demandent donc la conservation que lorsque la valeur
    /// stockée est celle du boîtier et que la saisie n'a pas été faite pour cette fiche.
    ///
    /// Une valeur stockée avec des espaces de bord n'est jamais « celle du boîtier » :
    /// l'ingestion coupe les espaces du MAT reçu et un IMEI n'en contient pas.
    /// </summary>
    public static string StoredValueFor(string? stored, string input, bool keepDeviceSpelling)
    {
        var trimmed = input.Trim();
        return keepDeviceSpelling
               && !string.IsNullOrEmpty(stored)
               && stored == stored.Trim()
               && trimmed.Length > 0
               && Normalize(stored) == Normalize(trimmed)
            ? stored
            : trimmed;
    }

    /// <summary>
    /// IMEI plausible : 15 chiffres dont le dernier est la clé de Luhn.
    ///
    /// POURQUOI — le 14/09/2026, trois véhicules (HTZ 278, 255, 292) pointaient vers
    /// des fiches créées avec un IMEI mal recopié (un chiffre faux, clé de Luhn
    /// invalide) pendant que le vrai boîtier émettait sous une autre fiche portant
    /// le même MAT. Ce contrôle sert UNIQUEMENT à enrichir un message : des
    /// device_uid non-IMEI (10 ou 32 caractères) existent légitimement, on ne
    /// bloque jamais une saisie sur ce critère.
    /// </summary>
    public static bool IsValidImei(string? value)
    {
        var s = Normalize(value);
        if (s.Length != 15) return false;

        var sum = 0;
        for (var i = 0; i < 15; i++)
        {
            var c = s[i];
            if (c < '0' || c > '9') return false;
            var digit = c - '0';
            // En partant de la droite, un chiffre sur deux est doublé (hors clé).
            if (i % 2 == 1)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
        }
        return sum % 10 == 0;
    }

    /// <summary>
    /// Cherche un AUTRE boîtier (≠ <paramref name="excludeDeviceId"/>) portant déjà
    /// l'un des identifiants fournis. Retourne un message d'erreur français précis
    /// (quel identifiant, quel boîtier, quel véhicule) ou null si aucun conflit.
    /// La table gps_devices est petite (quelques centaines de lignes) : on compare
    /// en mémoire pour appliquer la même normalisation que ci-dessus.
    /// </summary>
    /// <param name="ignoreDeviceIds">
    /// Fiches supplémentaires à ignorer — typiquement celles qu'une même opération
    /// est en train de supprimer (la requête lit la base, pas le suivi EF).
    /// </param>
    public static async Task<string?> FindConflictAsync(
        IGisDbContext context, int excludeDeviceId,
        string? imei, string? mat, string? sim,
        CancellationToken ct = default,
        IEnumerable<int>? ignoreDeviceIds = null)
    {
        var conflict = await FindConflictDetailAsync(
            context, excludeDeviceId, imei, mat, sim, ct, ignoreDeviceIds);
        return conflict?.Message;
    }

    /// <summary>
    /// Même recherche que <see cref="FindConflictAsync"/>, mais renvoie le conflit
    /// structuré (boîtier, identifiant en cause, IMEI du boîtier, véhicule) pour
    /// que l'appelant personnalise son message.
    /// </summary>
    public static async Task<GpsDeviceConflict?> FindConflictDetailAsync(
        IGisDbContext context, int excludeDeviceId,
        string? imei, string? mat, string? sim,
        CancellationToken ct = default,
        IEnumerable<int>? ignoreDeviceIds = null)
    {
        var nImei = Normalize(imei);
        var nMat = Normalize(mat);
        var nSim = Normalize(sim);
        if (nImei.Length == 0 && nMat.Length == 0 && nSim.Length == 0) return null;

        var ignored = ignoreDeviceIds != null ? new HashSet<int>(ignoreDeviceIds) : new HashSet<int>();

        var others = await context.GpsDevices
            .AsNoTracking()
            .Where(d => d.Id != excludeDeviceId)
            // Ordre stable : le conflit signalé (et donc le message) ne dépend pas de
            // l'ordre physique des lignes.
            .OrderBy(d => d.Id)
            .Select(d => new
            {
                d.Id,
                d.DeviceUid,
                d.Mat,
                d.SimNumber,
                VehicleId = d.Vehicle != null ? (int?)d.Vehicle.Id : null,
                VehicleLabel = d.Vehicle != null ? (d.Vehicle.Plate ?? d.Vehicle.Name) : null
            })
            .ToListAsync(ct);

        foreach (var o in others)
        {
            if (ignored.Contains(o.Id)) continue;

            if (nImei.Length > 0 && Normalize(o.DeviceUid) == nImei)
                return new GpsDeviceConflict(o.Id, GpsDeviceConflict.Imei, imei!.Trim(), o.DeviceUid, o.VehicleId, o.VehicleLabel);
            if (nMat.Length > 0 && Normalize(o.Mat) == nMat)
                return new GpsDeviceConflict(o.Id, GpsDeviceConflict.Mat, mat!.Trim(), o.DeviceUid, o.VehicleId, o.VehicleLabel);
            if (nSim.Length > 0 && Normalize(o.SimNumber) == nSim)
                return new GpsDeviceConflict(o.Id, GpsDeviceConflict.Sim, sim!.Trim(), o.DeviceUid, o.VehicleId, o.VehicleLabel);
        }
        return null;
    }
}
