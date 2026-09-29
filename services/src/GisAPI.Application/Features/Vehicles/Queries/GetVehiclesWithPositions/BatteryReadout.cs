using GisAPI.Domain.Common;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions;

/// <summary>
/// Tension et niveau de batterie affichés par le monitoring pour un véhicule.
///
/// <para><b>NEMS — la tension du DERNIER DÉMARRAGE</b> (Slim, 29/09/2026).
/// L'octet 34-36 mélange deux grandeurs : moteur tournant il porte l'alternateur
/// (13,5 à 14,4 V), pas la batterie. Seul l'instant du démarrage dit dans quel
/// état est la batterie. <c>BatteryStartReadingService</c> retient cette valeur
/// et l'écrit sur le boîtier ; elle reste affichée jusqu'au démarrage suivant,
/// donc parfois plusieurs jours — d'où <see cref="Result.MeasuredAt"/>, qui
/// permet de la dater à l'écran. Rien n'est calculé ici, et surtout pas dans la
/// requête du chemin chaud.</para>
///
/// <para>Le verdict de l'audit (<c>voltage_sensor_reliable</c>) n'est pas
/// consulté pour les NEMS : c'est le service qui écrit la valeur qui porte sa
/// propre garde « l'octet bouge-t-il » (<see cref="VoltageScale.NemsByteMoves"/>),
/// calculée sur 24 h et donc auto-réparante, là où un verdict sur 7 jours
/// laissait masqué plusieurs jours un boîtier tout juste reflashé.</para>
///
/// <para><b>Autres protocoles (Teltonika)</b> : règle inchangée — tension de la
/// dernière trame, affichée seulement si l'audit a validé le capteur.</para>
/// </summary>
public static class BatteryReadout
{
    /// <param name="Volts">Tension affichée, arrondie à 0,1 V, ou null (N/A).</param>
    /// <param name="Percent">Niveau de charge affiché, ou null.</param>
    /// <param name="IsStartReading">
    /// Vrai pour un NEMS : la valeur est celle du dernier démarrage, pas de la
    /// dernière trame. Le temps réel ne doit alors pas la remplacer.
    /// </param>
    /// <param name="MeasuredAt">Instant (UTC) du démarrage mesuré, ou null.</param>
    /// <param name="MedianVolts">
    /// NEMS : médiane des derniers démarrages, arrondie à 0,1 V, ou null si on n'en a
    /// pas assez. Affichée en infobulle à côté de la dernière mesure, pour que
    /// l'exploitant comprenne pourquoi le témoin est allumé — ou éteint.
    /// </param>
    /// <param name="LowWarning">
    /// NEMS : MÉDIANE des derniers démarrages sous
    /// <see cref="VoltageScale.NemsBatteryLowWarningV"/>, ce qui allume l'icône
    /// « Anomalie batterie ». Jamais le dernier démarrage seul : un creux isolé (radio
    /// oubliée) ne prouve rien. Toujours faux hors NEMS : leur icône reste pilotée par
    /// l'alerte de santé batterie.
    /// </param>
    public readonly record struct Result(
        double? Volts,
        int? Percent,
        bool IsStartReading,
        DateTime? MeasuredAt,
        double? MedianVolts,
        bool LowWarning);

    public static Result Compute(
        string? protocolType,
        bool? voltageSensorReliable,
        int? deviceBatteryLevel,
        int? lastBatteryRaw,
        int? lastPowerVoltage,
        int? batteryStartRaw,
        DateTime? batteryStartAt,
        int? batteryStartMedianRaw)
    {
        if (VoltageScale.IsNems(protocolType))
        {
            var startV = VoltageScale.NemsMeaningfulVolts(batteryStartRaw);
            if (startV == null)
                return new Result(null, null, IsStartReading: true, MeasuredAt: null,
                    MedianVolts: null, LowWarning: false);

            // Le témoin se juge sur la médiane, pas sur cette mesure-ci. Tant qu'on n'a
            // pas assez de démarrages (médiane nulle), pas de témoin : on montre la
            // tension sans l'interpréter.
            var medianV = VoltageScale.NemsMeaningfulVolts(batteryStartMedianRaw);

            return new Result(
                VoltageScale.RoundVolts(startV.Value),
                VoltageScale.BatteryPercent(startV.Value),
                IsStartReading: true,
                MeasuredAt: batteryStartAt,
                MedianVolts: medianV == null ? null : VoltageScale.RoundVolts(medianV.Value),
                LowWarning: medianV < VoltageScale.NemsBatteryLowWarningV);
        }

        if (voltageSensorReliable != true) return new Result(null, null, false, null, null, false);

        var displayVolts = VoltageScale.DisplayVolts(protocolType, lastBatteryRaw, lastPowerVoltage);
        if (displayVolts == null) return new Result(null, null, false, null, null, false);

        var voltage = Math.Min(displayVolts.Value, VoltageScale.AlternatorCeilingV);
        return new Result(
            VoltageScale.RoundVolts(voltage),
            deviceBatteryLevel ?? VoltageScale.BatteryPercent(voltage),
            IsStartReading: false,
            MeasuredAt: null,
            MedianVolts: null,
            LowWarning: false);
    }
}
