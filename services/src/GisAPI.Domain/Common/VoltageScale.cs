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

    /// <summary>Le boîtier parle-t-il le protocole NEMS ?</summary>
    public static bool IsNems(string? protocolType) =>
        string.Equals(protocolType, NemsProtocol, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Plage des valeurs brutes de l'octet « Batterie » (34-36) qui sont une vraie
    /// tension : 45 (7,03 V) à 92 (14,375 V).
    ///
    /// <para><b>Pourquoi ces bornes, et pourquoi on juge la valeur et plus le
    /// boîtier</b> (Karim, 25/09/2026). Les firmwares R00C30d recopient l'octet de
    /// cap dans ce champ : 0 à 44 brut, soit 0 à 6,9 V. Les R00C32a et FMS envoient
    /// une vraie tension. Relevé sur toute la flotte NEMS TN, 24 h du 24 au 25/09 :
    /// ≈192 000 trames entre 0 et 44, puis presque rien entre 45 et 69, puis la
    /// batterie entre 70 et 89 — aucune au-delà. Trier la valeur suffit donc, sans
    /// connaître le firmware (que la base ne connaît d'ailleurs pas :
    /// <c>firmware_version</c> vaut « L » partout). Un boîtier qui change de
    /// firmware dans la journée ne voit pas ses anciennes valeurs polluer son
    /// minimum.</para>
    ///
    /// <para>Conséquence assumée : une batterie réellement sous 7 V s'affiche N/A,
    /// car elle ne se distingue pas de l'octet de cap. Décision de Karim.</para>
    /// </summary>
    public const int NemsBatteryMeaningfulMinRaw = 45;
    public const int NemsBatteryMeaningfulMaxRaw = 92;

    /// <summary>
    /// Seuil de l'icône « Anomalie batterie » du monitoring pour les NEMS : minimum
    /// du jour de l'octet 34-36 sous 11,5 V (Karim, 25/09/2026). Remplace, à
    /// l'écran seulement, l'alerte calculée sur l'octet 32-34.
    /// </summary>
    public const double NemsBatteryLowWarningV = 11.5;

    /// <summary>
    /// Tension en volts d'une valeur brute de l'octet « Batterie », ou <c>null</c>
    /// si la valeur n'a pas de sens (absente, 0, octet de cap R00C30d, hors échelle).
    /// Non arrondie.
    /// </summary>
    public static double? NemsMeaningfulVolts(int? raw) =>
        raw is >= NemsBatteryMeaningfulMinRaw and <= NemsBatteryMeaningfulMaxRaw
            ? raw.Value * NemsBatteryFactor
            : null;

    /// <summary>Bornes de l'échelle du pourcentage de charge d'une batterie 12 V.</summary>
    public const double BatteryEmptyV = 11.0;
    public const double BatteryFullV = 12.8;

    /// <summary>
    /// Pourcentage de charge affiché à côté de la tension : 0 % à 11,0 V ou moins,
    /// 100 % à 12,8 V ou plus, linéaire entre les deux.
    /// </summary>
    public static int BatteryPercent(double volts)
    {
        if (volts <= BatteryEmptyV) return 0;
        if (volts >= BatteryFullV) return 100;
        return (int)Math.Round((volts - BatteryEmptyV) / (BatteryFullV - BatteryEmptyV) * 100.0);
    }

    /// <summary>
    /// Bande plausible, au repos, pour une batterie 12 V mesurée par
    /// <c>power_voltage</c> (Teltonika). Hors de cette bande, l'échelle est fausse
    /// (ou le véhicule n'est pas en 12 V) et le pourcentage, calibré 11,0-12,8 V,
    /// n'aurait aucun sens. Les NEMS n'y passent plus : leur octet « Batterie » est
    /// trié valeur par valeur (<see cref="NemsMeaningfulVolts"/>).
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
