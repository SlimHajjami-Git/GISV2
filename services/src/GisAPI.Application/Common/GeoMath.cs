namespace GisAPI.Application.Common;

/// <summary>
/// Shared geographic math utilities — single source of truth for distance calculations.
/// Replaces duplicate Haversine implementations across controllers and services.
/// </summary>
public static class GeoMath
{
    private const double EarthRadiusMeters = 6371000;

    /// <summary>
    /// Haversine distance between two points in meters
    /// </summary>
    public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }

    /// <summary>
    /// Distance from a point to a line segment (for route deviation detection)
    /// </summary>
    public static double DistanceFromSegment(double pLat, double pLon,
        double aLat, double aLon, double bLat, double bLon)
    {
        var ap = new[] { pLat - aLat, pLon - aLon };
        var ab = new[] { bLat - aLat, bLon - aLon };
        var abLen2 = ab[0] * ab[0] + ab[1] * ab[1];

        if (abLen2 == 0)
            return HaversineDistance(pLat, pLon, aLat, aLon);

        var t = Math.Max(0, Math.Min(1, (ap[0] * ab[0] + ap[1] * ab[1]) / abLen2));
        var closestLat = aLat + t * ab[0];
        var closestLon = aLon + t * ab[1];

        return HaversineDistance(pLat, pLon, closestLat, closestLon);
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;
}
