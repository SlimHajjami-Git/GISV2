namespace GisAPI.Application.Common;

/// <summary>
/// Distance parcourue d'après une série de relevés compteur saisis à la main
/// (pleins, réparations…), pour les véhicules sans boîtier GPS.
///
/// Extrait tel quel de <c>GetRealFuelConsumptionQueryHandler</c> (recette client
/// du 04/09/2026) pour être partagé avec les rapports de coûts : le kilométrage
/// affiché dans « Carburant réel » et dans « Coût d'exploitation » doit être le
/// MÊME chiffre, calculé par le même code.
///
/// Règles :
/// <list type="bullet">
///   <item>Seuls les relevés &gt; 0 comptent ; ils sont pris dans l'ordre des dates.</item>
///   <item>Un relevé ISOLÉ aberrant (145 200 km entre 46 845 et 47 455 — faute de
///     frappe à l'import) est ignoré comme relevé, PAS comme intervalle. Critère :
///     ses deux voisins sont cohérents entre eux (0 &lt; Δ ≤ <see cref="MaxSegmentKm"/>)
///     alors qu'il s'écarte des deux.</item>
///   <item>La distance est la somme des écarts cohérents (0 &lt; Δ ≤ <see cref="MaxSegmentKm"/>)
///     entre relevés conservés consécutifs. Un écart hors bornes est une RUPTURE de
///     série (changement de compteur, deux imports incompatibles) : il n'est pas
///     additionné, sans rien rejeter d'autre.</item>
///   <item>Chaque écart cohérent est attribué au mois du relevé AVAL.</item>
/// </list>
/// </summary>
public static class OdometerDistance
{
    /// <summary>
    /// Δkm au-delà duquel deux relevés consécutifs ne décrivent plus le même
    /// trajet (plein manqué, relevé faux) : l'écart est exclu du calcul.
    /// </summary>
    public const long MaxSegmentKm = 3000;

    public sealed record Result(
        decimal DistanceKm,                                              // somme des écarts cohérents
        int KeptReadings,                                                // relevés conservés
        int IgnoredReadings,                                             // relevés isolés aberrants écartés
        int Breaks,                                                      // ruptures de série
        IReadOnlyDictionary<(int Year, int Month), decimal> MonthlyKm)   // écart attribué au mois du relevé aval
    {
        /// <summary>Série continue, sans relevé écarté ni rupture, d'au moins deux relevés.</summary>
        public bool Reliable => IgnoredReadings == 0 && Breaks == 0 && KeptReadings >= 2;

        /// <summary>Au moins un écart cohérent a pu être additionné.</summary>
        public bool Measurable => DistanceKm > 0;
    }

    public static Result Compute(IEnumerable<(long Km, DateTime Date)> readings)
    {
        // OrderBy est stable : deux relevés à la même date gardent l'ordre d'arrivée.
        var odo = readings.Where(r => r.Km > 0).OrderBy(r => r.Date).ToList();

        var kept = new List<(long Km, DateTime Date)>();
        int ignored = 0;
        for (int i = 0; i < odo.Count; i++)
        {
            var km = odo[i].Km;
            if (i > 0 && i < odo.Count - 1)
            {
                var prev = odo[i - 1].Km;
                var next = odo[i + 1].Km;
                var neighboursCoherent = next - prev > 0 && next - prev <= MaxSegmentKm;
                var offFromPrev = km - prev <= 0 || km - prev > MaxSegmentKm;
                var offFromNext = next - km <= 0 || next - km > MaxSegmentKm;
                if (neighboursCoherent && offFromPrev && offFromNext) { ignored++; continue; }
            }
            kept.Add(odo[i]);
        }

        decimal segKm = 0;
        int breaks = 0;
        var monthly = new Dictionary<(int Year, int Month), decimal>();
        for (int i = 1; i < kept.Count; i++)
        {
            var dKm = kept[i].Km - kept[i - 1].Km;
            if (dKm <= 0 || dKm > MaxSegmentKm) { breaks++; continue; }
            segKm += dKm;
            var k = (kept[i].Date.Year, kept[i].Date.Month);
            monthly[k] = monthly.GetValueOrDefault(k) + dKm;
        }

        return new Result(segKm, kept.Count, ignored, breaks, monthly);
    }
}
