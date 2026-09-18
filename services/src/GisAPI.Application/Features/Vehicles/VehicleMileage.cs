using GisAPI.Application.Common;
using GisAPI.Application.Common.Interfaces;
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
    /// <summary>
    /// Fait avancer le compteur du véhicule si le relevé lui est supérieur.
    /// Renvoie true si la fiche a changé (l'appelant reste maître du SaveChanges).
    /// </summary>
    public static bool Advance(Vehicle? vehicle, int? reading)
    {
        if (vehicle is null || reading is not > 0 || reading.Value <= vehicle.Mileage)
            return false;

        vehicle.Mileage = reading.Value;
        vehicle.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>Surcharge pour les relevés stockés en <c>long?</c> (fuel_entries.odometer_km).</summary>
    public static bool Advance(Vehicle? vehicle, long? reading) =>
        Advance(vehicle, reading is > 0 and <= int.MaxValue ? (int)reading.Value : null);

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
