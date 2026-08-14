namespace GisAPI.Domain.Common;

/// <summary>
/// Conversion de l'octet de tension remonté par les boîtiers en volts, et
/// règles décidant si cette valeur est affichable.
///
/// <para><b>Pourquoi ce fichier existe.</b> Le facteur 0,3 était écrit en dur
/// dans le chemin de lecture et appliqué à TOUS les protocoles. Or les
/// Teltonika encodent la tension en dixièmes de volt (facteur 0,1) : leur
/// médiane de 137 donnait 41 V, écrêtée à 14,4 V — soit « 14,4 V / 100 % »
/// affiché en permanence, quel que soit l'état réel de la batterie.</para>
/// </summary>
public static class VoltageScale
{
    /// <summary>
    /// Volts par unité brute, par protocole. Tout protocole absent de cette
    /// table est <b>non affichable</b> : mieux vaut ne rien montrer que
    /// d'inventer une échelle.
    /// </summary>
    public static double? FactorFor(string? protocolType) => protocolType switch
    {
        // NEMS L/S : octet = tension × 0,3 (parseur hh.rs, decode_power).
        "gps_type_1" => 0.3,
        // Teltonika : le parseur stocke déjà millivolts/100, donc dixièmes de volt.
        "teltonika" => 0.1,
        _ => null
    };

    /// <summary>
    /// Plafond d'affichage : sortie régulée d'un alternateur 12 V (specs
    /// constructeur). Au-delà, c'est un artefact de calibration, pas une mesure.
    /// </summary>
    public const double AlternatorCeilingV = 14.4;

    /// <summary>
    /// Écart minimal, en unités brutes, entre la médiane moteur tournant et la
    /// médiane moteur éteint pour considérer que le capteur mesure vraiment.
    /// Un alternateur ajoute ~1,6 V ; on exige seulement 0,9 V pour ne pas
    /// écarter un boîtier un peu mou, tout en éliminant les capteurs plats.
    /// </summary>
    public const int MinAlternatorDeltaRaw = 3;

    /// <summary>
    /// Bande plausible pour une batterie 12 V au repos. Hors de cette bande,
    /// l'échelle est fausse (ou le véhicule n'est pas en 12 V) et le
    /// pourcentage, calibré 11,0–12,8 V, n'aurait aucun sens.
    /// </summary>
    public const double RestingPlausibleMinV = 10.5;
    public const double RestingPlausibleMaxV = 14.0;

    /// <summary>
    /// Planchers statistiques : une médiane sur quelques trames ne vaut rien,
    /// et conclure « capteur plat » sur un véhicule qui n'a pas roulé serait
    /// faux — d'où le verdict indécis plutôt qu'un « false » abusif.
    /// </summary>
    public const int MinDrivingFrames = 100;
    public const int MinRestingFrames = 50;

    /// <summary>
    /// Le capteur de tension de ce boîtier mesure-t-il réellement la batterie ?
    ///
    /// <para><c>true</c> = il suit l'alternateur ET son échelle donne une
    /// tension au repos plausible pour du 12 V. <c>false</c> = valeur plate
    /// (le cas de l'écrasante majorité) ou hors échelle. <c>null</c> = pas de
    /// quoi conclure : protocole sans échelle connue, ou véhicule trop peu
    /// roulé. En aval, <c>null</c> et <c>false</c> se traitent pareil — on
    /// n'affiche rien.</para>
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

        // 1. L'alternateur doit se voir : c'est le test qui démasque les
        //    capteurs constants.
        if (drivingMedian.Value - restingMedian.Value < MinAlternatorDeltaRaw)
            return false;

        // 2. Et l'échelle doit tomber juste : un capteur qui bouge mais annonce
        //    25 V au repos n'est pas exploitable par une jauge calibrée 12 V.
        var restingV = restingMedian.Value * factor.Value;
        return restingV >= RestingPlausibleMinV && restingV <= RestingPlausibleMaxV;
    }
}
