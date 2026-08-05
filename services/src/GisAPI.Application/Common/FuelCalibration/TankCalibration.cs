namespace GisAPI.Application.Common.FuelCalibration;

/// <summary>
/// Un point d'étalonnage : un plein FACTURÉ (litres exacts du ticket de pompe)
/// rapproché d'une montée de jauge (en points de %). Le ticket fait foi côté
/// litres ; la jauge fait foi côté points. Le rapport litres/point est la seule
/// vérité terrain sur la géométrie du réservoir.
/// </summary>
public record TankCalibrationPoint(
    DateTime InvoiceDate,
    decimal InvoiceLiters,
    int DeltaPoints
);

/// <summary>
/// Conversion points de jauge → litres, étalonnée par véhicule.
///
/// POURQUOI — la conversion naïve « capacité/100 » a accusé un chauffeur à tort :
/// Scania 001 (SGF), plein de 438 L affiché « 390 L détectés » parce que la fiche
/// disait 500 L alors que les pleins confirmés donnent ~5,2-5,6 L/point (~540 L
/// réels). La forme du réservoir (mesurée sur 6 semaines de roulage : profil
/// quasi plat, ±10 %) et la quantification de la jauge (1 point) imposent de
/// toute façon une FOURCHETTE, jamais un chiffre sec.
/// </summary>
public record TankCalibrationResult(
    bool IsCalibrated,
    decimal LitersPerPoint,
    decimal UncertaintyPerPoint,
    int PointCount,
    int NominalCapacity)
{
    /// <summary>Pleins confirmés nécessaires pour faire foi (validé avec l'exploitant : 4-5).</summary>
    public const int MinPointsToCalibrate = 4;

    /// <summary>Dispersion relative (MAD/médiane) au-delà de laquelle la jauge n'est pas fiable.</summary>
    public const decimal MaxRelativeDispersion = 0.20m;

    /// <summary>
    /// Incertitude par défaut sans étalonnage : ±10 % — la variation de forme
    /// mesurée sur un réservoir réel (9-13 km/pt selon la zone, Scania 001).
    /// </summary>
    public const decimal DefaultRelativeUncertainty = 0.10m;

    /// <summary>Un plein doit couvrir au moins ce nombre de points pour étalonner (sinon bruit de jauge).</summary>
    public const int MinDeltaPointsForPoint = 15;

    public decimal ConvertToLiters(int deltaPoints) =>
        Math.Round(deltaPoints * LitersPerPoint, 1);

    /// <summary>
    /// Fourchette honnête : ± (1 point de quantification + l'incertitude apprise
    /// sur chaque point). C'est la fourchette qui protège le chauffeur — un
    /// chiffre sec laisse croire à une précision que la jauge n'a pas.
    /// </summary>
    public (decimal Low, decimal High) RangeFor(int deltaPoints)
    {
        var center = deltaPoints * LitersPerPoint;
        var half = LitersPerPoint + deltaPoints * UncertaintyPerPoint;
        return (Math.Round(Math.Max(0, center - half), 1), Math.Round(center + half, 1));
    }

    /// <summary>Capacité effective apprise (n'a de sens qu'étalonné).</summary>
    public int? EffectiveTankLiters =>
        IsCalibrated ? (int)Math.Round(LitersPerPoint * 100) : null;

    public static TankCalibrationResult Uncalibrated(int nominalCapacity)
    {
        var lpp = nominalCapacity / 100m;
        return new TankCalibrationResult(false, lpp, lpp * DefaultRelativeUncertainty, 0, nominalCapacity);
    }

    /// <summary>
    /// Ajuste la conversion à partir des pleins confirmés.
    ///
    /// Méthode volontairement robuste plutôt que savante : médiane des rapports
    /// litres/point, rejet des points à plus de ±35 % de la médiane (plein
    /// partiel mal rapproché, panne de jauge, facture partielle — cas réels
    /// observés), puis dispersion par MAD. On ne déclare l'étalonnage acquis
    /// qu'avec ≥ 4 points cohérents : en dessous, on garde la conversion
    /// nominale et sa fourchette large — ne jamais faire semblant de savoir.
    /// </summary>
    public static TankCalibrationResult Fit(IReadOnlyList<TankCalibrationPoint> points, int nominalCapacity)
    {
        var nominalLpp = nominalCapacity / 100m;

        var ratios = points
            .Where(p => p.InvoiceLiters > 0 && p.DeltaPoints >= MinDeltaPointsForPoint)
            .Select(p => p.InvoiceLiters / p.DeltaPoints)
            // Garde-fou physique : un rapport hors [0,3× ; 3×] du nominal n'est
            // pas un réservoir, c'est une erreur de rapprochement.
            .Where(r => r >= nominalLpp * 0.3m && r <= nominalLpp * 3m)
            .OrderBy(r => r)
            .ToList();

        if (ratios.Count == 0)
            return Uncalibrated(nominalCapacity);

        var median = Median(ratios);

        var kept = ratios.Where(r => Math.Abs(r - median) <= median * 0.35m).ToList();
        if (kept.Count == 0)
            return Uncalibrated(nominalCapacity);

        var finalMedian = Median(kept);
        var mad = Median(kept.Select(r => Math.Abs(r - finalMedian)).OrderBy(x => x).ToList());

        var calibrated = kept.Count >= MinPointsToCalibrate
                         && finalMedian > 0
                         && mad / finalMedian <= MaxRelativeDispersion;

        // Non étalonné : on retient quand même le nominal (pas la médiane) — une
        // poignée de points bruyants ne doit pas déplacer la conversion officielle.
        return calibrated
            ? new TankCalibrationResult(true, Math.Round(finalMedian, 3),
                Math.Round(Math.Max(mad, finalMedian * 0.03m), 3), kept.Count, nominalCapacity)
            : Uncalibrated(nominalCapacity) with { PointCount = kept.Count };
    }

    private static decimal Median(IReadOnlyList<decimal> sorted)
    {
        if (sorted.Count == 0) return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2m;
    }
}
