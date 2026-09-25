using GisAPI.Domain.Common;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions;

/// <summary>
/// Tension et niveau de batterie affichés par le monitoring pour un véhicule.
///
/// <para><b>NEMS — minimum du jour de l'octet « Batterie » (34-36)</b> (Karim,
/// 25/09/2026). L'octet 34-36 fait foi ; on affiche sa plus petite valeur sensée
/// depuis minuit (heure de Tunis), et non plus la dernière trame. Le verdict de
/// l'audit (<c>voltage_sensor_reliable</c>, jugé sur 7 jours) n'est plus consulté
/// pour les NEMS : un boîtier passé en R00C32a gardait six jours de valeurs
/// R00C30d dans sa fenêtre et restait masqué plusieurs jours. C'est désormais le
/// tri de la valeur elle-même (<see cref="VoltageScale.NemsMeaningfulVolts"/>) qui
/// écarte l'octet de cap. Aucune valeur sensée aujourd'hui → N/A.</para>
///
/// <para><b>Autres protocoles (Teltonika)</b> : règle inchangée — tension de la
/// dernière trame, affichée seulement si l'audit a validé le capteur.</para>
/// </summary>
public static class BatteryReadout
{
    /// <param name="Volts">Tension affichée, arrondie à 0,1 V, ou null (N/A).</param>
    /// <param name="Percent">Niveau de charge affiché, ou null.</param>
    /// <param name="IsDailyMin">Vrai pour un NEMS : la valeur est le minimum du jour.</param>
    /// <param name="LowWarning">
    /// NEMS : minimum du jour sous <see cref="VoltageScale.NemsBatteryLowWarningV"/>,
    /// ce qui allume l'icône « Anomalie batterie ». Toujours faux hors NEMS : leur
    /// icône reste pilotée par l'alerte de santé batterie.
    /// </param>
    public readonly record struct Result(double? Volts, int? Percent, bool IsDailyMin, bool LowWarning);

    public static Result Compute(
        string? protocolType,
        bool? voltageSensorReliable,
        int? deviceBatteryLevel,
        int? lastBatteryRaw,
        int? lastPowerVoltage,
        int? dailyMinBatteryRaw)
    {
        if (VoltageScale.IsNems(protocolType))
        {
            var minV = VoltageScale.NemsMeaningfulVolts(dailyMinBatteryRaw);
            if (minV == null) return new Result(null, null, IsDailyMin: true, LowWarning: false);

            return new Result(
                Math.Round(minV.Value, 1),
                VoltageScale.BatteryPercent(minV.Value),
                IsDailyMin: true,
                LowWarning: minV.Value < VoltageScale.NemsBatteryLowWarningV);
        }

        if (voltageSensorReliable != true) return new Result(null, null, false, false);

        var displayVolts = VoltageScale.DisplayVolts(protocolType, lastBatteryRaw, lastPowerVoltage);
        if (displayVolts == null) return new Result(null, null, false, false);

        var voltage = Math.Min(displayVolts.Value, VoltageScale.AlternatorCeilingV);
        return new Result(
            Math.Round(voltage, 1),
            deviceBatteryLevel ?? VoltageScale.BatteryPercent(voltage),
            IsDailyMin: false,
            LowWarning: false);
    }
}
