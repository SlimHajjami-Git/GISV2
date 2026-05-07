using GisAPI.Domain.Entities;

namespace GisAPI.Application.Common.Services;

/// <summary>
/// Calypso 8 — Calcul cumule de distance Haversine sur une serie de positions GPS.
/// Centralise la logique pour que tous les consommateurs (rapport km par
/// periode, calcul de km d emprunt, etc.) donnent EXACTEMENT la meme valeur
/// pour la meme fenetre temporelle. Avant cette extraction, chaque endroit
/// avait sa propre fonction et le client voyait des ecarts (ex: emprunt 247
/// km vs rapport 976 km sur la meme periode).
/// </summary>
public static class GpsDistanceCalculator
{
    /// <summary>
    /// Distance maximale acceptee entre 2 points consecutifs (km). Au-dela,
    /// on considere que c est un saut GPS / donnee corrompue et on ignore
    /// le segment.
    /// </summary>
    public const double MaxSegmentKm = 10.0;

    /// <summary>
    /// Vitesse implicite maximale (km/h). Si distance/temps depasse cette
    /// valeur, le segment est rejete (typique des points GPS aberrants).
    /// </summary>
    public const double MaxImpliedKph = 250.0;

    /// <summary>
    /// Distance minimale d un segment (km). Sous ce seuil c est du bruit
    /// GPS quand le vehicule est a l arret (jitter capteur).
    /// </summary>
    public const double MinSegmentKm = 0.005; // 5 metres

    /// <summary>
    /// Calcule la distance cumulee sur une liste de positions ordonnees par
    /// recordedAt asc. Filtre les segments aberrants (saut GPS, vitesse
    /// implicite irrealiste, micro-mouvements de bruit).
    /// </summary>
    public static double CalculateTotalDistanceKm(IReadOnlyList<GpsPosition> positions)
    {
        if (positions == null || positions.Count < 2) return 0;

        double total = 0;
        for (int i = 1; i < positions.Count; i++)
        {
            var prev = positions[i - 1];
            var curr = positions[i];

            // Ne compte que si le vehicule bouge sur au moins un des deux points.
            // Evite de compter le bruit GPS quand le vehicule est completement arrete.
            if ((curr.SpeedKph ?? 0) <= 0 && (prev.SpeedKph ?? 0) <= 0) continue;

            var segmentKm = HaversineKm(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
            if (segmentKm < MinSegmentKm || segmentKm > MaxSegmentKm) continue;

            var elapsedHours = (curr.RecordedAt - prev.RecordedAt).TotalHours;
            if (elapsedHours > 0)
            {
                var impliedKph = segmentKm / elapsedHours;
                if (impliedKph > MaxImpliedKph) continue;
            }

            total += segmentKm;
        }
        return total;
    }

    /// <summary>
    /// Distance Haversine entre deux points en km.
    /// </summary>
    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0; // rayon Terre en km
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
