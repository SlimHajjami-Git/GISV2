namespace GisAPI.Domain.Entities;

/// <summary>
/// Tension batterie relevée à UN démarrage de véhicule (boîtiers NEMS).
///
/// <para>Une ligne par démarrage, écrite par <c>BatteryStartReadingService</c>.
/// Sert à confirmer le témoin « anomalie batterie » sur la médiane des
/// <see cref="Common.VoltageScale.StartHistoryWindow"/> derniers démarrages plutôt
/// que sur un seul : mesuré sur la production TN le 29/09/2026, 9 témoins sur 34
/// venaient d'un creux isolé entouré de démarrages sains — radio oubliée, phares,
/// trajet trop court pour recharger.</para>
///
/// <para><b>Pas de <c>CompanyId</c>, donc pas de filtre de tenant.</b> La table est
/// un journal technique du boîtier, écrit et lu par des services de fond hors
/// contexte tenant ; le cloisonnement se fait en amont, par le boîtier auquel elle
/// est rattachée. Elle n'est exposée par aucun endpoint.</para>
/// </summary>
public class BatteryStartReading
{
    public long Id { get; set; }

    /// <summary>Boîtier concerné (<c>gps_devices.id</c>).</summary>
    public int DeviceId { get; set; }

    /// <summary>Instant (UTC) du démarrage. Unique par boîtier : c'est la clé d'idempotence.</summary>
    public DateTime StartAt { get; set; }

    /// <summary>
    /// Valeur BRUTE de l'octet « Batterie » (34-36), × <c>VoltageScale.NemsBatteryFactor</c>
    /// pour des volts. Toujours dans la bande 68-92 : le service ne stocke rien d'autre.
    /// </summary>
    public short BatteryRaw { get; set; }

    public DateTime CreatedAt { get; set; }

    public GpsDevice? Device { get; set; }
}
