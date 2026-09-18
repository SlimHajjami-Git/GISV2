using GisAPI.Application.Common;
using GisAPI.Domain.Entities;

namespace GisAPI.Services;

/// <summary>
/// Règles de planification et d'avancement d'une tournée, partagées par les
/// actions manuelles (<c>ToursController</c>) et automatiques
/// (<c>TourMonitoringService</c>).
///
/// Constat du 18/09/2026 : chaque chemin avait sa propre copie de ces règles et
/// elles avaient divergé — la modification d'une tournée effaçait les heures
/// estimées, les géofences et les marges des étapes ; le démarrage manuel ne
/// décalait pas les estimations d'un départ en retard alors que le démarrage
/// automatique le faisait ; aucun chemin manuel ne mettait à jour
/// <c>WaypointStatus</c> (12 origines sur 12 restées « pending » en prod alors
/// qu'elles étaient cochées). Une seule définition, testée, pour tous.
///
/// Invariant tenu par <see cref="MarkReached"/> et <see cref="MarkSkipped"/> :
/// <c>IsCompleted</c> est vrai si et seulement si <c>WaypointStatus</c> vaut
/// « completed ».
/// </summary>
public static class TourPlanning
{
    /// <summary>En deçà, un démarrage n'est pas considéré en retard (même seuil
    /// que le démarrage automatique historique).</summary>
    public static readonly TimeSpan LateStartThreshold = TimeSpan.FromMinutes(2);

    public const int DefaultDeadlineMarginMinutes = 60;

    /// <summary>
    /// Repli quand le routage ne répond pas : distance à vol d'oiseau majorée
    /// du détour routier, parcourue à une vitesse moyenne prudente.
    ///
    /// Le but n'est pas une heure exacte mais une ÉCHÉANCE pour chaque étape.
    /// Relecture du 18/09/2026 : une étape sans heure prévue ne passe jamais
    /// « temps_depasse », donc une étape manquée restait « pending » pour
    /// toujours et verrouillait la validation de la destination — la tournée
    /// ne se clôturait plus seule. À l'inverse, l'ancien repli (toutes les
    /// étapes à l'heure de départ) déclenchait de faux « temps dépassé ».
    /// Prudent = plutôt tard que tôt : une alerte un peu tardive coûte moins
    /// qu'une fausse alerte de priorité haute.
    /// </summary>
    public const double FallbackDetourFactor = 1.3;

    /// <summary>Vitesse moyenne du repli, cf. <see cref="FallbackDetourFactor"/>.</summary>
    public const double FallbackAverageSpeedKph = 50;

    /// <summary>Tolérance de comparaison des coordonnées : les étapes renvoyées
    /// par l'écran reviennent telles qu'elles ont été stockées.</summary>
    private const double SamePlaceTolerance = 1e-7;

    public sealed record StopInput(double Latitude, double Longitude, int PlannedPauseMinutes);

    /// <summary>Estimation d'une étape : minutes du tronçon qui y mène et heure
    /// d'arrivée prévue (toujours renseignée, cf. <see cref="FallbackDetourFactor"/>).</summary>
    public sealed record StopEstimate(int LegMinutes, DateTime EstimatedArrivalTime);

    // ───────────────────────── Estimations ─────────────────────────

    /// <summary>Durée de repli d'un tronçon, en secondes (routage indisponible).</summary>
    public static double FallbackLegSeconds(StopInput from, StopInput to) =>
        GeoMath.HaversineDistance(from.Latitude, from.Longitude, to.Latitude, to.Longitude) / 1000.0
        * FallbackDetourFactor / FallbackAverageSpeedKph * 3600.0;

    /// <summary>
    /// Heures d'arrivée prévues à partir de l'itinéraire calculé :
    /// arrivée(i) = départ + somme des tronçons jusqu'à i + pauses prévues aux
    /// étapes précédentes.
    ///
    /// <paramref name="legTimesSeconds"/> null = pas d'itinéraire (routage
    /// indisponible) : chaque tronçon prend sa durée de repli
    /// (<see cref="FallbackLegSeconds"/>).
    ///
    /// Les étapes intermédiaires étant envoyées au routage en « through »,
    /// celui-ci ne rend qu'UN tronçon global dès qu'il y a plus de deux points
    /// (constaté en prod : tout le temps de trajet porté par la 1re étape, puis
    /// 0). Dans ce cas la durée totale est répartie au prorata des distances à
    /// vol d'oiseau de chaque tronçon — la destination retombe ainsi sur la
    /// durée totale, pauses comprises.
    /// </summary>
    public static List<StopEstimate> EstimateFromRoute(
        DateTime scheduledStart,
        IReadOnlyList<StopInput> stops,
        IReadOnlyList<double>? legTimesSeconds,
        double totalTimeSeconds)
    {
        var n = stops.Count;
        if (n == 0) return new List<StopEstimate>();
        var legs = new double[n];

        if (legTimesSeconds != null && legTimesSeconds.Count == n - 1)
        {
            for (var i = 1; i < n; i++) legs[i] = legTimesSeconds[i - 1];
        }
        else
        {
            var total = legTimesSeconds == null ? 0
                : totalTimeSeconds > 0 ? totalTimeSeconds : legTimesSeconds.Sum();

            if (total > 0)
            {
                var distances = new double[n];
                for (var i = 1; i < n; i++)
                    distances[i] = GeoMath.HaversineDistance(
                        stops[i - 1].Latitude, stops[i - 1].Longitude, stops[i].Latitude, stops[i].Longitude);
                var sum = distances.Sum();
                for (var i = 1; i < n; i++)
                    legs[i] = sum > 0 ? total * distances[i] / sum : total / (n - 1);
            }
            else
            {
                for (var i = 1; i < n; i++) legs[i] = FallbackLegSeconds(stops[i - 1], stops[i]);
            }
        }

        return Accumulate(scheduledStart, stops, legs, legMinutesOverride: null);
    }

    /// <summary>
    /// Estimations quand le routage est indisponible pendant une modification :
    /// on ne jette PAS ce qui était connu. Un tronçon dont les deux extrémités
    /// sont inchangées reprend la durée qu'il avait (écart des anciennes heures
    /// d'arrivée, pause déduite) ; un tronçon nouveau (étape ajoutée,
    /// déplacée, réordonnée) prend sa durée de repli. Les heures sont ensuite
    /// recumulées sur la nouvelle heure de départ et les nouvelles pauses.
    ///
    /// Relecture du 18/09/2026 : la première version redonnait à une étape
    /// placée après une étape insérée son ANCIENNE heure prévue, qui ignorait
    /// le détour et la pause de l'étape insérée — échéance trop tôt, donc faux
    /// « temps dépassé » alors que le chauffeur suivait le plan.
    /// </summary>
    public static List<StopEstimate> EstimateFromPrevious(
        DateTime scheduledStart,
        IReadOnlyList<StopInput> stops,
        IReadOnlyList<TourWaypoint> previous)
    {
        var n = stops.Count;
        if (n == 0) return new List<StopEstimate>();

        var prev = previous.OrderBy(w => w.SequenceOrder).ToList();
        var legs = new double[n];
        var legMinutes = new int?[n];

        for (var i = 1; i < n; i++)
        {
            var known = PreviousLegSeconds(prev, stops[i - 1], stops[i], i);
            if (known.HasValue)
            {
                legs[i] = known.Value.Seconds;
                legMinutes[i] = known.Value.Minutes;
            }
            else
            {
                legs[i] = FallbackLegSeconds(stops[i - 1], stops[i]);
            }
        }

        return Accumulate(scheduledStart, stops, legs, legMinutes);
    }

    /// <summary>
    /// Durée d'un tronçon inchangé de l'ancienne tournée : deux étapes
    /// consécutives aux mêmes coordonnées ; à égalité, celui qui occupait la
    /// même position (un aller-retour a son origine et sa destination au même
    /// endroit). Null si le tronçon est nouveau ou sans estimation exploitable.
    /// </summary>
    private static (double Seconds, int? Minutes)? PreviousLegSeconds(
        List<TourWaypoint> prev, StopInput from, StopInput to, int position)
    {
        var matches = Enumerable.Range(1, Math.Max(0, prev.Count - 1))
            .Where(j => SamePlace(prev[j - 1], from) && SamePlace(prev[j], to))
            .ToList();
        if (matches.Count == 0) return null;
        var j = matches.Contains(position) ? position : matches[0];

        int? minutes = prev[j].EstimatedLegMinutes > 0 ? prev[j].EstimatedLegMinutes : null;
        if (prev[j].EstimatedArrivalTime.HasValue && prev[j - 1].EstimatedArrivalTime.HasValue)
        {
            var gap = (prev[j].EstimatedArrivalTime!.Value - prev[j - 1].EstimatedArrivalTime!.Value).TotalSeconds
                      - prev[j - 1].PlannedPauseMinutes * 60.0;
            return (Math.Max(0, gap), minutes);
        }
        return minutes.HasValue ? (minutes.Value * 60.0, minutes) : null;
    }

    private static List<StopEstimate> Accumulate(
        DateTime scheduledStart, IReadOnlyList<StopInput> stops, double[] legs, int?[]? legMinutesOverride)
    {
        var result = new List<StopEstimate>(stops.Count) { new(0, scheduledStart) };
        double cumulativeSeconds = 0;
        var cumulativePauseMinutes = 0;

        for (var i = 1; i < stops.Count; i++)
        {
            cumulativePauseMinutes += stops[i - 1].PlannedPauseMinutes;
            cumulativeSeconds += legs[i];

            var minutes = legMinutesOverride?[i] ?? (int)Math.Ceiling(legs[i] / 60.0);
            result.Add(new StopEstimate(minutes,
                scheduledStart.AddSeconds(cumulativeSeconds).AddMinutes(cumulativePauseMinutes)));
        }

        return result;
    }

    private static bool SamePlace(TourWaypoint w, StopInput s) =>
        Math.Abs(w.Latitude - s.Latitude) < SamePlaceTolerance
        && Math.Abs(w.Longitude - s.Longitude) < SamePlaceTolerance;

    // ───────────────────────── Suivi ─────────────────────────

    /// <summary>
    /// Le véhicule de la tournée appartient-il toujours à la société de la
    /// tournée ? Relecture du 18/09/2026 : un administrateur système peut
    /// transférer un véhicule à une autre société
    /// (UpdateAdminVehicleCommandHandler) sans toucher à ses tournées. Le
    /// moniteur, qui tourne hors filtre société, aurait alors suivi la trace
    /// GPS de la nouvelle société et diffusé ses positions (écart d'itinéraire,
    /// « Point atteint ») à l'ancienne ; le suivi en direct et la chronologie
    /// du détail aussi. La position n'est lue que si ce test passe.
    /// </summary>
    public static bool VehicleBelongsToTourCompany(Tour tour) =>
        tour.Vehicle != null && tour.Vehicle.CompanyId == tour.CompanyId;

    /// <summary>Échéance d'une étape : heure prévue + marge (null sans heure prévue).</summary>
    public static DateTime? DeadlineOf(TourWaypoint wp) =>
        wp.EstimatedArrivalTime?.AddMinutes(wp.DeadlineMarginMinutes);

    /// <summary>Étape encore attendue dont l'échéance est passée : le moniteur la
    /// passe « temps_depasse », ce qui la résout (elle ne bloque plus la destination).</summary>
    public static bool IsOverdue(TourWaypoint wp, DateTime now) =>
        !wp.IsCompleted && wp.WaypointStatus == "pending" && DeadlineOf(wp) is { } deadline && now > deadline;

    /// <summary>
    /// Une étape antérieure est-elle encore attendue (« pending », non
    /// atteinte) ? Tant que oui, le moniteur ne valide pas la destination — un
    /// aller-retour se clôturerait sinon dès le départ. Toute étape a une
    /// échéance (<see cref="FallbackDetourFactor"/>), donc ce verrou se lève au
    /// plus tard quand elle passe « temps_depasse ».
    /// </summary>
    public static bool HasPendingStopBefore(IEnumerable<TourWaypoint> waypoints, TourWaypoint stop) =>
        waypoints.Any(w => w.SequenceOrder < stop.SequenceOrder && !w.IsCompleted && w.WaypointStatus == "pending");

    // ───────────────────────── Avancement ─────────────────────────

    /// <summary>
    /// Décale les heures prévues des étapes non atteintes de
    /// <paramref name="delay"/> s'il dépasse <see cref="LateStartThreshold"/>.
    /// Les échéances mesurent le temps de CONDUITE de chaque tronçon, pas le
    /// retard pris au départ : sans ce décalage, toutes les étapes d'une
    /// tournée lancée en retard passaient aussitôt « temps dépassé ».
    /// Retourne vrai si le décalage a été appliqué.
    /// </summary>
    public static bool ShiftPendingEstimates(IEnumerable<TourWaypoint> waypoints, TimeSpan delay)
    {
        if (delay <= LateStartThreshold) return false;

        foreach (var w in waypoints.Where(w => !w.IsCompleted && w.EstimatedArrivalTime.HasValue))
            w.EstimatedArrivalTime = w.EstimatedArrivalTime!.Value.Add(delay);
        return true;
    }

    /// <summary>
    /// Démarre la tournée (bouton « Démarrer » ou démarrage automatique à
    /// l'heure) : statut, heure réelle, décalage des estimations si le
    /// démarrage est en retard, origine atteinte. Retourne le décalage
    /// appliqué (zéro s'il n'y en a pas eu).
    /// </summary>
    public static TimeSpan Start(Tour tour, DateTime now)
    {
        tour.Status = "in_progress";
        tour.ActualStartTime = now;

        var delay = now - tour.ScheduledStartTime;
        var shifted = ShiftPendingEstimates(tour.Waypoints, delay) ? delay : TimeSpan.Zero;

        var origin = OriginOf(tour);
        if (origin != null) MarkReached(origin, now);

        return shifted;
    }

    /// <summary>Étape atteinte : les deux champs d'état passent ensemble.</summary>
    public static void MarkReached(TourWaypoint wp, DateTime arrivedAt)
    {
        wp.IsCompleted = true;
        wp.WaypointStatus = "completed";
        wp.ActualArrivalTime = arrivedAt;
    }

    /// <summary>Étape jamais atteinte, abandonnée à la clôture de la tournée.</summary>
    public static void MarkSkipped(TourWaypoint wp)
    {
        wp.IsCompleted = false;
        wp.WaypointStatus = "skipped";
    }

    /// <summary>
    /// État final des étapes à la clôture d'une tournée.
    ///
    /// - La destination est atteinte (clôture = arrivée, comme avant).
    /// - Une étape intermédiaire encore « pending » passe « skipped » et NON
    ///   « completed » : rien ne prouve que le véhicule y est passé, la marquer
    ///   atteinte fausserait le suivi et les rapports. C'est aussi la règle
    ///   retenue pour l'arrivée déclarée par le chauffeur (design du 18/09).
    /// - « temps_depasse » est conservé : c'est l'information utile (étape
    ///   manquée après son échéance).
    /// - Les étapes cochées mais restées « pending » (données antérieures au
    ///   18/09/2026) sont remises en cohérence.
    /// </summary>
    public static void CloseWaypointsOnCompletion(Tour tour, DateTime now)
    {
        var ordered = tour.Waypoints.OrderBy(w => w.SequenceOrder).ToList();
        var destination = ordered.LastOrDefault(w => w.Type == "destination") ?? ordered.LastOrDefault();

        if (destination != null && !destination.IsCompleted)
            MarkReached(destination, now);

        foreach (var wp in ordered)
        {
            if (wp.IsCompleted)
                wp.WaypointStatus = "completed";
            else if (wp.WaypointStatus == "pending")
                MarkSkipped(wp);
        }
    }

    /// <summary>Point de départ : l'étape de type « origin », à défaut la première.</summary>
    public static TourWaypoint? OriginOf(Tour tour) =>
        tour.Waypoints.FirstOrDefault(w => w.Type == "origin")
        ?? tour.Waypoints.OrderBy(w => w.SequenceOrder).FirstOrDefault();
}
