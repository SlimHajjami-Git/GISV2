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
    /// Tension d'UNE trame, ou <c>null</c> s'il n'y a rien de fiable à montrer.
    /// <paramref name="batteryRaw"/> est l'octet « Batterie » 34-36 (NEMS), trié
    /// comme partout ailleurs (<see cref="NemsMeaningfulVolts"/> : l'octet de cap
    /// des R00C30d donne null) ; <paramref name="powerVoltage"/> est la tension des
    /// Teltonika. Le monitoring n'affiche pas la dernière trame d'un NEMS mais la
    /// tension retenue à son dernier démarrage (BatteryReadout).
    /// </summary>
    public static double? DisplayVolts(string? protocolType, int? batteryRaw, int? powerVoltage)
    {
        if (IsNems(protocolType)) return NemsMeaningfulVolts(batteryRaw);

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
    /// tension : 68 (10,625 V) à 92 (14,375 V).
    ///
    /// <para><b>Pourquoi on juge la valeur et plus le boîtier</b> (Karim, 25/09/2026).
    /// Les firmwares R00C30d recopient l'octet de cap dans ce champ : 0 à 44 brut.
    /// Les R00C32a et FMS envoient une vraie tension. Trier la valeur suffit donc,
    /// sans connaître le firmware — que la base ne connaît d'ailleurs pas,
    /// <c>firmware_version</c> valant « L » partout.</para>
    ///
    /// <para><b>Pourquoi le plancher est passé de 45 à 68 le 29/09/2026.</b> Slim,
    /// devant un graphe qui descendait à 7 V : « je pense que c'est du bruit,
    /// protection à rajouter, ne pas accepter les valeurs 34-36 &lt; 10,5 ». 10,5 V
    /// tombe entre 67 (10,47 V, refusé) et 68 (10,625 V, accepté).</para>
    ///
    /// <para><b>Ce que les mesures disaient.</b> Sur 5 jours de production TN, sur les
    /// trames À L'ARRÊT, part de celles suivies d'un roulage au-dessus de 20 km/h dans
    /// les 10 minutes — un démarreur ne tourne pas sous 9 V, donc un véhicule qui
    /// repart n'avait pas cette tension-là : 7,0-7,8 V → 27,9 % repartent, soit la
    /// ligne de base du parc sain (23 à 32 %), ces lectures ne sont pas la batterie ;
    /// 8,0-10,6 V → 3,9 à 7,9 %, trois à huit fois moins, comportement d'une batterie
    /// réellement faible.</para>
    ///
    /// <para><b>Le coût, connu et accepté.</b> Couper à 10,5 V et non à 8 V rend muets
    /// trois véhicules dont la batterie est la plus dégradée du parc : 257 TU 7933
    /// (8,4 V de médiane sur 107 démarrages), 236 TU 6531 (9,5 V sur 63) et
    /// 251 TU 8814 (8,3 V). Ils n'affichent plus rien et ne déclenchent plus d'alerte.
    /// Le parc passe de 18 à 14 alertes. Arbitrage de Slim, qui préfère le silence au
    /// risque d'un chiffre faux.</para>
    ///
    /// <para><b>Conséquence sur la détection.</b> La fenêtre utile se réduit à
    /// 10,5-11,5 V, soit six valeurs brutes (68 à 73). En dessous, N/A. Si un jour on
    /// veut de nouveau signaler les batteries effondrées, cela demandera un signal
    /// distinct — pas un élargissement de cette bande.</para>
    /// </summary>
    public const int NemsBatteryMeaningfulMinRaw = 68;
    public const int NemsBatteryMeaningfulMaxRaw = 92;

    /// <summary>
    /// Seuil de l'icône « Anomalie batterie » ET de la notification « batterie en fin
    /// de vie » pour les NEMS : tension retenue au démarrage sous 11,5 V (Slim,
    /// 29/09/2026). L'écran et l'alerte jugent désormais la MÊME valeur.
    ///
    /// <para><b>Pourquoi 11,5 et pas les 11,9 V du manuel plomb-acide.</b> Mesuré sur
    /// la production TN le 29/09/2026, sur les 226 boîtiers ayant démarré dans les
    /// 48 h : médiane de la flotte 12,19 V au démarrage, premier quartile 11,72 V.
    /// À 11,9 V l'alerte visait 84 boîtiers (37 %) ; à 11,5 V elle en vise 34 (15 %),
    /// et la population s'effondre juste en dessous. Un témoin allumé sur un tiers du
    /// parc s'ignore en une semaine.</para>
    ///
    /// <para>En valeur brute de l'octet 34-36 : 73 vaut 11,41 V (sous le seuil) et 74
    /// vaut 11,56 V (au-dessus).</para>
    /// </summary>
    public const double NemsBatteryLowWarningV = 11.5;

    /// <summary>
    /// Durée sans aucune trame moteur allumé au bout de laquelle la trame moteur
    /// allumé suivante est un DÉMARRAGE.
    ///
    /// <para>Un seuil, et pas la transition <c>ignition_on</c> faux → vrai : sur un
    /// NEMS à l'arrêt ce drapeau clignote vrai/faux toutes les une à deux secondes
    /// (vérifié le 28/09/2026 sur 243 TU 7247, des dizaines de bascules par heure sans
    /// que le véhicule bouge). Compter chaque bascule comme un démarrage reviendrait à
    /// afficher n'importe quelle trame.</para>
    /// </summary>
    public static readonly TimeSpan StartQuietPeriod = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Fenêtre, après le démarrage, dans laquelle on lit la tension : au-delà,
    /// l'alternateur a pris le relais et l'octet ne parle plus de la batterie.
    ///
    /// <para>Deux minutes, et pas plus : mesuré sur la production TN le 29/09/2026
    /// sur les 272 boîtiers ayant démarré dans les 24 h, l'élargir à 5 puis 10 minutes
    /// ne fait passer la couverture que de 207 à 208 boîtiers. Les 65 restants
    /// n'émettent tout simplement pas de valeur exploitable au démarrage (firmwares
    /// R00C30d, qui recopient l'octet de cap) : ils affichent N/A, ce qui est la
    /// réponse honnête.</para>
    /// </summary>
    public static readonly TimeSpan StartSampleWindow = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Nombre de trames retenues au démarrage. La MÉDIANE de ces trames est la valeur
    /// affichée : trois valeurs suffisent à écarter la trame isolée aberrante, qui est
    /// exactement ce qui faisait dérailler le minimum du jour (229 TU 9662 le
    /// 28/09/2026 : 10,0 V affichés à cause d'une trame sur 211, médiane 12,81 V).
    /// </summary>
    public const int StartSampleFrames = 3;

    /// <summary>
    /// Nombre de démarrages récents sur lesquels se calcule la MÉDIANE qui allume le
    /// témoin « anomalie batterie » et déclenche la notification — ou moins, si le
    /// boîtier en a moins (voir <see cref="MinStartsForWarning"/>).
    ///
    /// <para><b>Pourquoi une médiane et pas le dernier démarrage</b> (Slim, 29/09/2026 :
    /// « si un chauffeur laisse la radio allumée notre code peut donner des faux
    /// positifs non ? »). Mesuré sur la production TN, 237 boîtiers, 4 jours, ~19
    /// démarrages par véhicule : sur 34 témoins allumés par le dernier démarrage seul,
    /// 9 venaient d'un creux ISOLÉ — 251 TU 8789 lisait 10,9 puis 12,3 | 12,5 | 12,5 |
    /// 12,3 V. Et l'inverse : 5 batteries franchement faibles étaient MANQUÉES parce que
    /// leur dernier démarrage était bon, typiquement après un long trajet qui avait
    /// rechargé — 235 TU 5540 lisait 13,1 puis 11,3 | 11,3 | 11,3 | 11,3 V.</para>
    ///
    /// <para><b>Médiane et non moyenne</b> : sur 10,9 | 12,3 | 12,5 la moyenne vaut
    /// 11,9 V et allumerait encore le témoin ; la médiane vaut 12,3 V et l'éteint.</para>
    ///
    /// <para><b>Pourquoi 20 « au plus »</b> : seuls 91 boîtiers sur 237 ont 20 démarrages
    /// en 4 jours. Exiger vraiment 20 priverait 146 véhicules de tout témoin, batteries
    /// mourantes comprises, pour un écart de 3 véhicules sur le résultat. 20 démarrages
    /// couvrent ~56 h : le témoin réagit en 2 à 3 jours, ce qui convient à « prévoir un
    /// remplacement » — il n'a jamais prétendu annoncer la panne du lendemain.</para>
    /// </summary>
    public const int StartHistoryWindow = 20;

    /// <summary>
    /// En dessous de ce nombre de démarrages connus, aucun témoin ni notification : une
    /// médiane sur une ou deux valeurs, c'est la valeur elle-même, donc le faux positif
    /// qu'on cherche à éviter. 13 boîtiers sur 237 étaient dans ce cas le 29/09/2026 ;
    /// ils affichent leur tension, sans témoin.
    /// </summary>
    public const int MinStartsForWarning = 3;

    /// <summary>
    /// Médiane basse des démarrages récents, <c>null</c> s'il n'y en a pas assez pour
    /// conclure. <paramref name="recentRawDescending"/> est la liste des valeurs brutes,
    /// du démarrage le plus récent au plus ancien ; seules les
    /// <see cref="StartHistoryWindow"/> premières comptent.
    ///
    /// <para>Médiane basse (élément d'indice <c>(n-1)/2</c> sur la liste triée) : sur un
    /// nombre pair de démarrages, on prend la valeur du bas plutôt que la moyenne des
    /// deux du milieu — devant une batterie, à égalité, on penche du côté prudent. C'est
    /// aussi ce que fait <c>percentile_disc(0.5)</c> côté PostgreSQL, donc les deux
    /// chemins donnent le même chiffre.</para>
    /// </summary>
    public static short? MedianStartRaw(IReadOnlyList<short> recentRawDescending)
    {
        if (recentRawDescending.Count < MinStartsForWarning) return null;

        var window = recentRawDescending.Count > StartHistoryWindow
            ? recentRawDescending.Take(StartHistoryWindow)
            : recentRawDescending;

        var sorted = window.OrderBy(v => v).ToArray();
        return sorted[(sorted.Length - 1) / 2];
    }

    /// <summary>
    /// Planchers de la garde « l'octet bouge-t-il » : nombre minimal de trames en
    /// mouvement ET à l'arrêt, sur 24 h, avant de pouvoir conclure qu'un octet
    /// strictement constant ne mesure rien.
    ///
    /// <para>Sans ces planchers on condamnerait tous les véhicules garés de la
    /// journée : un octet constant sur un véhicule qui n'a pas roulé est normal.
    /// Avec eux, mesuré sur 891 journées-boîtier du 24 au 29/09/2026, la garde ne se
    /// déclenche que sur UNE (244 TU 1249 le 27/09 : 1 124 trames toutes à 49 brut,
    /// soit 7,66 V figé, en roulant jusqu'à 88 km/h — la signature exacte de
    /// l'incident du 14/08/2026).</para>
    /// </summary>
    public const int MinFrozenCheckMovingFrames = 20;
    public const int MinFrozenCheckRestingFrames = 20;

    /// <summary>
    /// L'octet « Batterie » de ce boîtier mesure-t-il quelque chose ? Faux uniquement
    /// quand il est strictement constant sur la journée ALORS QUE le véhicule a roulé
    /// et s'est arrêté (planchers ci-dessus). Vrai par défaut : faute de preuve du
    /// contraire, on affiche — c'est le tri de la valeur elle-même
    /// (<see cref="NemsMeaningfulVolts"/>) qui écarte déjà l'octet de cap.
    /// </summary>
    public static bool NemsByteMoves(int? minRaw, int? maxRaw, long movingFrames, long restingFrames)
    {
        if (minRaw == null || maxRaw == null) return true;
        if (movingFrames < MinFrozenCheckMovingFrames || restingFrames < MinFrozenCheckRestingFrames)
            return true;
        return minRaw.Value != maxRaw.Value;
    }

    /// <summary>
    /// Tension en volts d'une valeur brute de l'octet « Batterie », ou <c>null</c>
    /// si la valeur n'a pas de sens (absente, 0, octet de cap R00C30d, hors échelle).
    /// Non arrondie.
    /// </summary>
    public static double? NemsMeaningfulVolts(int? raw) =>
        raw is >= NemsBatteryMeaningfulMinRaw and <= NemsBatteryMeaningfulMaxRaw
            ? raw.Value * NemsBatteryFactor
            : null;

    /// <summary>
    /// Arrondi d'affichage d'une tension, au dixième de volt.
    ///
    /// <para><b>À la valeur supérieure sur les demis</b>, et pas l'arrondi « au pair »
    /// de <c>Math.Round</c> par défaut. Dans la bande 68-92, une seule valeur brute
    /// tombe pile sur un demi : 72 vaut 11,25 V. L'arrondi au pair l'affichait 11,2 V
    /// quand PostgreSQL — donc toute vérification en base — et n'importe quel humain
    /// disent 11,3 V. Un écran qui contredit la requête d'investigation coûte plus cher
    /// que ce dixième de volt.</para>
    /// </summary>
    public static double RoundVolts(double volts) =>
        Math.Round(volts, 1, MidpointRounding.AwayFromZero);

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
