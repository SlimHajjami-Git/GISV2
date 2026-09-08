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
}
