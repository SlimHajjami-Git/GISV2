using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reports.Queries.GetMileageReport;

/// <summary>
/// Agrégation SQL du kilométrage.
///
/// POURQUOI — le rapport chargeait auparavant TOUTES les positions GPS en
/// mémoire (<c>ToListAsync</c>), pour la période courante ET la période de
/// comparaison, véhicule par véhicule. Sur TN (14,6 M de positions, pod limité
/// à 512 Mi) l'endpoint répondait 500 avec une <c>OutOfMemoryException</c> :
/// la page Rapports → Kilométrage était donc inutilisable.
///
/// Le calcul se prête entièrement aux fonctions de fenêtrage : chaque grandeur
/// ne dépend que du point précédent. On le pousse donc dans Postgres, qui
/// renvoie une ligne par véhicule et par jour au lieu de millions de points.
///
/// Les seuils sont RIGOUREUSEMENT ceux de l'ancien code C# afin que les
/// chiffres restent comparables :
///   distance   : segment retenu si l'un des deux points bouge, 5 m ≤ d ≤ 10 km,
///                et vitesse implicite ≤ 250 km/h (filtre les sauts GPS)
///   conduite   : temps compté quand le point PRÉCÉDENT est à plus de 2 km/h,
///                par tranches de moins de 10 minutes (au-delà = trou de trame)
///   trajets    : transitions « à l'arrêt → en mouvement » au seuil de 3 km/h
///
/// La distance utilise <c>ST_DistanceSphere</c> (PostGIS, déjà présent) plutôt
/// qu'un Haversine réécrit en SQL : même modèle sphérique, écart négligeable.
/// </summary>
public static class MileageAggregation
{
    /// <summary>Une ligne par appareil et par jour local.</summary>
    public class DailyRow
    {
        public int DeviceId { get; set; }
        public DateTime LocalDay { get; set; }
        public double DistanceKm { get; set; }
        public int DrivingSeconds { get; set; }
        public int TripCount { get; set; }
        public double MaxSpeedKph { get; set; }
        // gps_positions.odometer_km est un bigint côté Postgres : on mappe en
        // long? et non en decimal?, sinon Npgsql lève à la conversion.
        public long? StartOdometerKm { get; set; }
        public long? EndOdometerKm { get; set; }
    }

    /// <summary>Distance totale par appareil (utilisée pour la période de comparaison).</summary>
    public class TotalRow
    {
        public int DeviceId { get; set; }
        public double DistanceKm { get; set; }
    }

    // Le fenêtrage est partitionné par (appareil, jour local) : les agrégats
    // journaliers sont ainsi identiques à ceux de l'ancien code, qui découpait
    // les positions en listes par jour avant de calculer.
    private const string DailySql = @"
WITH pts AS (
    SELECT
        p.device_id,
        (p.recorded_at + interval '1 hour')::date              AS local_day,
        p.recorded_at,
        p.latitude, p.longitude,
        COALESCE(p.speed_kph, 0)                               AS spd,
        p.odometer_km,
        LAG(p.latitude)  OVER w                                AS prev_lat,
        LAG(p.longitude) OVER w                                AS prev_lon,
        LAG(COALESCE(p.speed_kph, 0)) OVER w                   AS prev_spd,
        LAG(p.recorded_at) OVER w                              AS prev_at
    FROM gps_positions p
    WHERE p.device_id = ANY({0})
      AND p.recorded_at >= {1}
      AND p.recorded_at <  {2}
    WINDOW w AS (
        PARTITION BY p.device_id, (p.recorded_at + interval '1 hour')::date
        ORDER BY p.recorded_at
    )
),
seg AS (
    SELECT
        device_id,
        local_day,
        recorded_at,
        spd,
        prev_spd,
        odometer_km,
        CASE
            WHEN prev_at IS NOT NULL AND (spd > 0 OR prev_spd > 0)
            THEN ST_DistanceSphere(
                     ST_MakePoint(prev_lon, prev_lat),
                     ST_MakePoint(longitude, latitude)
                 ) / 1000.0
        END                                                     AS seg_km,
        CASE
            WHEN prev_at IS NOT NULL
            THEN EXTRACT(EPOCH FROM (recorded_at - prev_at))
        END                                                     AS seg_sec
    FROM pts
)
SELECT
    device_id                                                   AS ""DeviceId"",
    local_day                                                   AS ""LocalDay"",
    COALESCE(SUM(
        CASE
            WHEN seg_km >= 0.005 AND seg_km <= 10.0
             AND (seg_sec IS NULL OR seg_sec <= 0 OR seg_km / (seg_sec / 3600.0) <= 250.0)
            THEN seg_km
        END
    ), 0)                                                       AS ""DistanceKm"",
    COALESCE(SUM(
        CASE
            WHEN prev_spd > 2 AND seg_sec > 0 AND seg_sec < 600
            THEN seg_sec
        END
    ), 0)::int                                                  AS ""DrivingSeconds"",
    COUNT(*) FILTER (WHERE spd > 3 AND COALESCE(prev_spd, 0) <= 3)::int
                                                                AS ""TripCount"",
    COALESCE(MAX(spd), 0)                                       AS ""MaxSpeedKph"",
    (array_agg(odometer_km ORDER BY recorded_at ASC)
        FILTER (WHERE odometer_km > 0 AND odometer_km <> 1048574))[1]
                                                                AS ""StartOdometerKm"",
    (array_agg(odometer_km ORDER BY recorded_at DESC)
        FILTER (WHERE odometer_km > 0 AND odometer_km <> 1048574))[1]
                                                                AS ""EndOdometerKm""
FROM seg
GROUP BY device_id, local_day
ORDER BY device_id, local_day;
";

    // Période de comparaison : seule la distance totale est utilisée. Inutile
    // de grouper par jour, et surtout inutile de rapatrier les points — c'était
    // la moitié de la mémoire consommée pour un seul chiffre.
    private const string TotalSql = @"
WITH pts AS (
    SELECT
        p.device_id,
        p.recorded_at,
        p.latitude, p.longitude,
        COALESCE(p.speed_kph, 0)                  AS spd,
        LAG(p.latitude)  OVER w                   AS prev_lat,
        LAG(p.longitude) OVER w                   AS prev_lon,
        LAG(COALESCE(p.speed_kph, 0)) OVER w      AS prev_spd,
        LAG(p.recorded_at) OVER w                 AS prev_at
    FROM gps_positions p
    WHERE p.device_id = ANY({0})
      AND p.recorded_at >= {1}
      AND p.recorded_at <  {2}
    WINDOW w AS (PARTITION BY p.device_id ORDER BY p.recorded_at)
),
seg AS (
    SELECT
        device_id,
        CASE
            WHEN prev_at IS NOT NULL AND (spd > 0 OR prev_spd > 0)
            THEN ST_DistanceSphere(
                     ST_MakePoint(prev_lon, prev_lat),
                     ST_MakePoint(longitude, latitude)
                 ) / 1000.0
        END                                       AS seg_km,
        CASE
            WHEN prev_at IS NOT NULL
            THEN EXTRACT(EPOCH FROM (recorded_at - prev_at))
        END                                       AS seg_sec
    FROM pts
)
SELECT
    device_id                                     AS ""DeviceId"",
    COALESCE(SUM(
        CASE
            WHEN seg_km >= 0.005 AND seg_km <= 10.0
             AND (seg_sec IS NULL OR seg_sec <= 0 OR seg_km / (seg_sec / 3600.0) <= 250.0)
            THEN seg_km
        END
    ), 0)                                         AS ""DistanceKm""
FROM seg
GROUP BY device_id;
";

    public static Task<List<DailyRow>> DailyAsync(
        IGisDbContext context, int[] deviceIds, DateTime start, DateTime end, CancellationToken ct) =>
        context.Database.SqlQueryRaw<DailyRow>(DailySql, deviceIds, start, end).ToListAsync(ct);

    public static Task<List<TotalRow>> TotalsAsync(
        IGisDbContext context, int[] deviceIds, DateTime start, DateTime end, CancellationToken ct) =>
        context.Database.SqlQueryRaw<TotalRow>(TotalSql, deviceIds, start, end).ToListAsync(ct);
}
