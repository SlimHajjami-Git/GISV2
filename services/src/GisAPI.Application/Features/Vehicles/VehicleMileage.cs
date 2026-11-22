using GisAPI.Application.Common;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Common;
using GisAPI.Domain.Entities;

namespace GisAPI.Application.Features.Vehicles;

/// <summary>
/// Propagation d'un relevé compteur vers <c>vehicles.mileage</c> — définition
/// UNIQUE, partagée par tous les points de saisie (plein manuel, import Excel,
/// entretien « marquer fait », entretien saisi depuis les Dépenses, réparation).
///
/// <para>Pivot de l'offre « gestion de parc sans GPS » : sans boîtier,
/// <c>vehicles.mileage</c> est la SEULE source des échéances d'entretien au
/// kilométrage et du kilométrage affiché à l'écran Véhicules. Jusqu'à la recette
/// du 08/09/2026, seuls DEUX chemins sur huit faisaient avancer le compteur : le
/// client saisissait un relevé depuis l'import, une réparation ou l'écran
/// Dépenses et la fiche véhicule ne bougeait pas.</para>
///
/// <para>Règle : on ne recule JAMAIS. Un relevé inférieur (ticket ancien saisi
/// après coup, import d'historique, faute de frappe) laisse le compteur en
/// place. Corriger une valeur trop haute passe par le chemin dédié
/// (<c>PUT /api/vehicles/{id}/mileage</c>), tracé et motivé.</para>
/// </summary>
public static class VehicleMileage
{
    // ── Relevé invraisemblable (import de pleins du 20/11/2026) ─────────────────
    //
    // Un import de 405 pleins d'octobre a fait avancer 17 fiches véhicule, dont 8
    // sur une faute de frappe : 221 030 km puis 2 215 595 km (chiffre en trop),
    // 433 988 km puis 464 624 km six jours plus tard (chiffres permutés), une
    // Caddy neuve à 3 900 km portée à 1 525 727 km. Comme un compteur ne recule
    // jamais, la fiche restait fausse pour toujours : affichée sur la carte, base
    // des échéances d'entretien, et figée sur les véhicules au compteur CAN (le
    // boîtier ne la met à jour que vers le HAUT). Les seuils ci-dessous laissent
    // passer l'écart normal entre le tableau de bord et la fiche (jusqu'à 13 000 km
    // mesurés le même jour chez ce client) et arrêtent les fautes d'un ordre de
    // grandeur.

    /// <summary>Au-delà, aucun véhicule routier : la valeur porte un chiffre en trop.</summary>
    public const int PlafondKm = 2_000_000;

    /// <summary>Distance maximale qu'un véhicule parcourt en un jour.</summary>
    public const int KmParJourMax = 1_500;

    /// <summary>Marge au-dessus du relevé précédent, pour les relevés du même jour ou presque.</summary>
    public const int MargeReleveKm = 2_000;

    /// <summary>
    /// Écart toléré au-dessus de la fiche : le compteur du tableau de bord et celui
    /// du boîtier (ou la distance GPS) divergent de plusieurs milliers de km.
    /// </summary>
    public const int EcartFicheToleréKm = 20_000;

    /// <summary>
    /// Fait avancer le compteur du véhicule si le relevé lui est supérieur ET
    /// vraisemblable (<see cref="EcartInvraisemblable"/>) : un relevé aberrant reste
    /// sur sa ligne (plein, réparation…) mais ne touche pas la fiche.
    /// Renvoie true si la fiche a changé (l'appelant reste maître du SaveChanges).
    /// </summary>
    public static bool Advance(Vehicle? vehicle, int? reading) => Advance(vehicle, reading, DateTime.UtcNow);

    /// <inheritdoc cref="Advance(Vehicle?, int?)"/>
    public static bool Advance(Vehicle? vehicle, int? reading, DateTime nowUtc)
    {
        if (vehicle is null || reading is not > 0 || reading.Value <= vehicle.Mileage)
            return false;
        if (EcartInvraisemblable(vehicle, reading.Value, nowUtc) is not null)
            return false;

        vehicle.Mileage = reading.Value;
        vehicle.UpdatedAt = nowUtc;
        return true;
    }

    /// <summary>Surcharge pour les relevés stockés en <c>long?</c> (fuel_entries.odometer_km).</summary>
    public static bool Advance(Vehicle? vehicle, long? reading) =>
        Advance(vehicle, reading is > 0 and <= int.MaxValue ? (int)reading.Value : null);

    /// <summary>
    /// Kilométrage DÉCLARÉ du véhicule lui-même (colonne « km » de l'import des
    /// véhicules) : il remplace la fiche s'il est supérieur, sans contrôle de
    /// vraisemblance — comme le formulaire du véhicule, c'est le moyen assumé de
    /// remonter une fiche restée trop basse.
    /// </summary>
    public static bool AdvanceDeclared(Vehicle? vehicle, int? mileage)
    {
        if (vehicle is null || mileage is not > 0 || mileage.Value <= vehicle.Mileage)
            return false;

        vehicle.Mileage = mileage.Value;
        vehicle.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Motif de refus si le relevé est invraisemblable face à la FICHE du véhicule,
    /// null sinon. Invraisemblable : au-delà de <see cref="PlafondKm"/>, ou plus de
    /// <see cref="EcartFicheToleréKm"/> km au-dessus de la fiche en plus de ce que le
    /// véhicule a pu rouler depuis qu'elle a bougé (<c>UpdatedAt</c>,
    /// <see cref="KmParJourMax"/> par jour). Une fiche à 0 (kilométrage inconnu) ne
    /// borne rien.
    /// </summary>
    public static string? EcartInvraisemblable(Vehicle vehicle, long reading, DateTime nowUtc)
    {
        if (reading > PlafondKm)
            return $"Le kilométrage saisi ({FormatFr.N(reading, 0)} km) dépasse {FormatFr.N(PlafondKm, 0)} km : " +
                   "vérifiez la valeur (un chiffre en trop ?).";

        if (vehicle.Mileage <= 0)
            return null;

        var ecart = reading - vehicle.Mileage;
        var jours = Math.Max(0, (nowUtc - vehicle.UpdatedAt).TotalDays);
        if (ecart <= EcartFicheToleréKm + KmParJourMax * jours)
            return null;

        return $"Le kilométrage saisi ({FormatFr.N(reading, 0)} km) dépasse de {FormatFr.N(ecart, 0)} km le kilométrage " +
               $"actuel du véhicule ({FormatFr.N(vehicle.Mileage, 0)} km) : vérifiez la valeur (un chiffre en trop ?). " +
               "Si c'est le kilométrage du véhicule qui est faux, corrigez-le d'abord dans sa fiche.";
    }

    /// <summary>
    /// Contrôle complet d'un relevé saisi à la date <paramref name="date"/> : le relevé
    /// SAISI le plus récent à cette date ou avant (pleins, entretiens, réparations,
    /// dépenses), puis la fiche (<see cref="EcartInvraisemblable"/>). Face au relevé
    /// précédent, l'écart permis est ce que le véhicule a pu rouler entre les deux
    /// dates (<see cref="KmParJourMax"/> par jour, plus <see cref="MargeReleveKm"/>).
    /// Renvoie le motif de refus, ou null. Le relevé trop BAS reste l'affaire de
    /// <see cref="FloorAtAsync"/>.
    /// </summary>
    public static async Task<string?> ControlerReleveAsync(
        IGisDbContext context, int companyId, Vehicle vehicle, DateTime date, long reading, CancellationToken ct)
    {
        if (reading <= 0)
            return null;

        var releves = await OdometerReadings.LoadAsync(
            context, companyId, new[] { vehicle.Id },
            DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc),
            DateTime.SpecifyKind(date.Date.AddDays(1), DateTimeKind.Utc),
            ct);

        var precedents = releves[vehicle.Id].ToList();
        if (precedents.Count > 0)
        {
            var precedent = precedents.OrderByDescending(r => r.Date.Date).ThenByDescending(r => r.Km).First();
            var jours = Math.Max(0, (date.Date - precedent.Date.Date).Days);
            var ecart = reading - precedent.Km;
            if (ecart > MargeReleveKm + (long)KmParJourMax * jours)
                return $"Le kilométrage saisi ({FormatFr.N(reading, 0)} km) dépasse de {FormatFr.N(ecart, 0)} km le relevé " +
                       $"du {precedent.Date:dd/MM/yyyy} ({FormatFr.N(precedent.Km, 0)} km), plus qu'un véhicule ne peut " +
                       $"parcourir en {(jours <= 1 ? "un jour" : $"{jours} jours")} : vérifiez la valeur (un chiffre en trop " +
                       "ou deux chiffres inversés ?) ou la date.";
        }

        return EcartInvraisemblable(vehicle, reading, DateTime.UtcNow);
    }

    /// <summary>
    /// Plancher d'un relevé saisi à la date <paramref name="date"/> : la valeur en dessous
    /// de laquelle « un compteur ne recule pas » refuse la saisie.
    ///
    /// <para>Daté du jour (ou après) : le compteur courant, <c>vehicles.mileage</c>. C'est
    /// la garde contre la faute de frappe (« 4 500 » pour « 45 000 »).</para>
    ///
    /// <para>Daté dans le passé : le plus haut relevé SAISI à cette date ou avant
    /// (<see cref="OdometerReadings"/> : pleins, entretiens, réparations, dépenses), 0 s'il
    /// n'y en a aucun. Comparer au compteur COURANT refusait toute saisie après coup :
    /// l'entretien fait le 06/07 à 10 000 km et saisi le 11/08, quand le boîtier affichait
    /// 18 593, était rejeté (revue de l'intégration du 18/09/2026 : 22 entretiens HERTZ sur
    /// 39 saisis ainsi). L'odomètre du boîtier n'est volontairement pas relu ici : il ne
    /// coïncide pas forcément avec le compteur du tableau de bord, et un écart y
    /// refuserait un relevé exact.</para>
    /// </summary>
    public static async Task<int> FloorAtAsync(
        IGisDbContext context, int companyId, Vehicle vehicle, DateTime date, CancellationToken ct)
    {
        if (date.Date >= DateTime.UtcNow.Date)
            return vehicle.Mileage;

        var releves = await OdometerReadings.LoadAsync(
            context, companyId, new[] { vehicle.Id },
            DateTime.SpecifyKind(new DateTime(2000, 1, 1), DateTimeKind.Utc),
            DateTime.SpecifyKind(date.Date.AddDays(1), DateTimeKind.Utc),
            ct);

        var plusHaut = releves[vehicle.Id].Select(r => r.Km).DefaultIfEmpty(0).Max();
        return (int)Math.Min(plusHaut, int.MaxValue);
    }
}
