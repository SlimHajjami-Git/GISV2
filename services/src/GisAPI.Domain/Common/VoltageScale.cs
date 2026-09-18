namespace GisAPI.Domain.Common;

/// <summary>
/// Conversion en volts de la tension remontée par les boîtiers, et règles
/// décidant si cette valeur est affichable.
///
/// <para><b>17/09/2026 — changement de source sur les NEMS.</b> La tension venait
/// de l'octet 32-34 de la trame (« Power », facteur 0,3). Sur 288 boîtiers NEMS,
/// 281 renvoyaient la même valeur moteur tournant et moteur éteint : cet octet ne
/// mesure rien, et l'application a affiché « 12,9 V / 100 % » sur un véhicule
/// incapable de démarrer (259 TU 4987, 14/08/2026). Le fournisseur a confirmé que
/// la tension batterie du véhicule est l'octet 34-36 (« Batterie »), facteur
/// <see cref="NemsBatteryFactor"/>, et que « Power » est à ignorer.</para>
///
/// <para><b>Les Teltonika ne changent pas.</b> Leur <c>power_voltage</c> porte
/// bien la tension externe, en dixièmes de volt (facteur 0,1) : 11 appareils qui
/// mesurent correctement, et qu'il serait absurde de casser.</para>
/// </summary>
public static class VoltageScale
{
    /// <summary>
    /// Volts par unité de l'octet « Batterie » (34-36) des boîtiers NEMS :
    /// 40 V de pleine échelle sur 8 bits, soit 0,15625 — le fournisseur l'écrit
    /// 0,156. Vérifié sur les 7 boîtiers qui renseignent le champ : 12,48 à
    /// 13,73 V, une batterie 12 V.
    /// </summary>
    public const double NemsBatteryFactor = 40.0 / 256.0;

    /// <summary>Protocole des boîtiers NEMS (L et S), les seuls à parler AJ+.</summary>
    public const string NemsProtocol = "gps_type_1";

    /// <summary>
    /// Volts par unité brute pour les protocoles dont la tension se lit encore
    /// dans <c>power_voltage</c>. Tout protocole absent est <b>non affichable</b> :
    /// mieux vaut ne rien montrer que d'inventer une échelle.
    ///
    /// <para>Les NEMS n'y figurent plus : leur tension vient désormais de
    /// <see cref="NemsBatteryFactor"/> appliqué à l'octet « Batterie ».</para>
    /// </summary>
    public static double? FactorFor(string? protocolType) => protocolType switch
    {
        // Teltonika : le parseur stocke déjà millivolts/100, donc dixièmes de volt.
        "teltonika" => 0.1,
        _ => null
    };

    /// <summary>
    /// Tension à afficher pour un boîtier, ou <c>null</c> s'il n'y a rien de
    /// fiable à montrer. <paramref name="batteryRaw"/> est l'octet 34-36 (NEMS),
    /// <paramref name="powerVoltage"/> l'ancien octet 32-34 (Teltonika).
    /// </summary>
    public static double? DisplayVolts(string? protocolType, int? batteryRaw, int? powerVoltage)
    {
        if (string.Equals(protocolType, NemsProtocol, StringComparison.OrdinalIgnoreCase))
        {
            if (batteryRaw is not > 0) return null;
            return batteryRaw.Value * NemsBatteryFactor;
        }

        var factor = FactorFor(protocolType);
        if (factor == null || powerVoltage is not > 0) return null;
        return powerVoltage.Value * factor.Value;
    }

    /// <summary>
    /// Plafond d'affichage : sortie régulée d'un alternateur 12 V (specs
    /// constructeur). Au-delà, c'est un artefact de calibration, pas une mesure.
    /// </summary>
    public const double AlternatorCeilingV = 14.4;

    /// <summary>
    /// Bande plausible pour une batterie 12 V. Hors de cette bande, l'échelle est
    /// fausse (ou le véhicule n'est pas en 12 V) et le pourcentage, calibré
    /// 11,0-12,8 V, n'aurait aucun sens.
    ///
    /// <para>C'est ce critère — et non un test d'alternateur — qui sépare les
    /// boîtiers NEMS qui mesurent de ceux qui recopient l'octet de cap dans le
    /// champ « Batterie » : ces derniers produisent 0 à 6,9 V, très loin de la
    /// bande. Voir <see cref="EvaluateNemsBattery"/> pour pourquoi l'alternateur
    /// n'est plus exigé.</para>
    /// </summary>
    public const double RestingPlausibleMinV = 10.5;
    public const double RestingPlausibleMaxV = 14.4;

    /// <summary>
    /// Écart minimal, en unités brutes, entre médiane moteur tournant et médiane
    /// moteur éteint pour considérer qu'un capteur <b>Teltonika</b> mesure vraiment.
    ///
    /// <para>Valeur historique délibérément inchangée le 17/09/2026. À 0,1 V
    /// l'unité elle ne représente que 0,3 V, bien moins que les ~1,6 V d'un
    /// alternateur : la durcir aurait fait basculer en « masqué » des boîtiers
    /// Teltonika qui mesurent correctement aujourd'hui (11 appareils), ce que la
    /// bascule des NEMS n'avait aucune raison d'emporter avec elle.</para>
    /// </summary>
    public const int MinAlternatorDeltaRawTeltonika = 3;

    /// <summary>
    /// Planchers statistiques : une médiane sur quelques trames ne vaut rien, et
    /// conclure « capteur plat » sur un véhicule qui n'a pas roulé serait faux —
    /// d'où le verdict indécis plutôt qu'un « false » abusif.
    /// </summary>
    public const int MinDrivingFrames = 100;
    public const int MinRestingFrames = 50;

    /// <summary>
    /// Le champ « Batterie » de ce boîtier NEMS porte-t-il une vraie mesure ?
    ///
    /// <para><c>true</c> = la médiane au repos tombe dans la bande d'une batterie
    /// 12 V. <c>false</c> = hors bande : c'est le doublon de l'octet de cap des
    /// firmwares R00C30d (0 à 6,9 V). <c>null</c> = pas assez de trames pour
    /// conclure. En aval, <c>null</c> et <c>false</c> se traitent pareil — on
    /// n'affiche rien.</para>
    ///
    /// <para><b>Pourquoi aucun test d'alternateur ici</b>, contrairement à
    /// l'ancienne règle : sur les 7 boîtiers qui renseignent ce champ, l'écart
    /// entre roulage et repos va de −0,16 à +0,47 V, jamais les ~1,6 V d'une
    /// recharge. Le boîtier lisse visiblement sa mesure. Exiger l'alternateur
    /// rejetterait donc les 7 boîtiers sains et n'afficherait plus rien — alors
    /// que la bande de plausibilité, elle, sépare exactement les deux familles.
    /// Conséquence assumée : ce champ renseigne l'état de charge au repos, pas la
    /// santé de la recharge.</para>
    /// </summary>
    public static bool? EvaluateNemsBattery(int? restingMedianRaw, long restingFrames)
    {
        if (restingMedianRaw == null || restingFrames < MinRestingFrames) return null;
        if (restingMedianRaw.Value <= 0) return false;

        var restingV = restingMedianRaw.Value * NemsBatteryFactor;
        return restingV >= RestingPlausibleMinV && restingV <= RestingPlausibleMaxV;
    }

    /// <summary>
    /// Le capteur de tension d'un boîtier <b>non NEMS</b> (Teltonika) mesure-t-il
    /// réellement la batterie ? Il doit suivre l'alternateur ET donner une tension
    /// au repos plausible pour du 12 V.
    /// </summary>
    public static bool? EvaluateSensor(
        string? protocolType,
        int? drivingMedian,
        int? restingMedian,
        long drivingFrames,
        long restingFrames)
    {
        var factor = FactorFor(protocolType);
        if (factor == null) return null;

        if (drivingMedian == null || restingMedian == null
            || drivingFrames < MinDrivingFrames
            || restingFrames < MinRestingFrames)
        {
            return null;
        }

        if (drivingMedian.Value - restingMedian.Value < MinAlternatorDeltaRawTeltonika)
            return false;

        var restingV = restingMedian.Value * factor.Value;
        return restingV >= RestingPlausibleMinV && restingV <= RestingPlausibleMaxV;
    }
}
