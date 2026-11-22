namespace GisAPI.Application.Common.FuelCalibration;

/// <summary>
/// Niveau de carburant AFFICHÉ (%) à partir de la valeur brute envoyée par le boîtier,
/// selon <c>gps_devices.fuel_sensor_mode</c>.
///
/// <para>Une seule règle pour tous les chemins qui montrent le carburant : la requête de la
/// carte, le cache Redis fusionné dans <c>/vehicles/with-positions</c>, le temps réel
/// (SignalR) et la relecture. Elle était recopiée trois fois, et le quatrième chemin (le
/// cache Redis du rafraîchissement de la carte) ne convertissait pas du tout : il prenait la
/// valeur brute pour un pourcentage. Recette du 20/11/2026 : Ibiza 237 TU 4672 (HERTZ),
/// boîtier en mode <c>liters</c>, réservoir de 40 L, 19 L envoyés — la carte affichait
/// 19 % au lieu de 48 % et allumait l'alerte « carburant bas ». 482 boîtiers sur 580 sont
/// en mode <c>liters</c> sur TN.</para>
/// </summary>
public static class FuelLevel
{
    /// <summary>Mode par défaut de gps_devices.fuel_sensor_mode (entité, base et ingestion).</summary>
    public const string DefaultMode = "raw_255";

    /// <summary>Réservoir supposé quand la fiche véhicule n'en indique pas.</summary>
    public const int DefaultTankCapacity = 60;

    /// <summary>
    /// Convertit la valeur brute en pourcentage borné à 0..100 :
    /// <c>percent</c> = déjà en %, <c>raw_255</c> = échelle 0..255, <c>liters</c> = litres,
    /// <c>half_liter</c> = demi-litres (ces deux-là rapportés au réservoir). Un mode inconnu
    /// laisse la valeur telle quelle, comme avant.
    /// </summary>
    public static int ToPercent(int raw, string? mode, int? tankCapacity)
    {
        var tank = tankCapacity ?? DefaultTankCapacity;
        var percent = (mode ?? DefaultMode) switch
        {
            "percent" => raw,
            "raw_255" => (int)Math.Round(raw / 255.0 * 100.0),
            "liters" => tank > 0 ? (int)Math.Round(raw * 100.0 / tank) : raw,
            "half_liter" => tank > 0 ? (int)Math.Round(raw * 0.5 * 100.0 / tank) : (int)Math.Round(raw * 0.5),
            _ => raw
        };
        return Math.Clamp(percent, 0, 100);
    }
}
