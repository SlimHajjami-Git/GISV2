using System.Text.Json.Serialization;
using GisAPI.Application.Common;

namespace GisAPI.Services;

public interface IGpsInterpolationService
{
    List<InterpolatedPoint> InterpolateRoute(List<GpsTrackPoint> points, InterpolationOptions? options = null);
    List<InterpolatedPoint> SmartInterpolate(List<GpsTrackPoint> points);
}

public class GpsInterpolationService : IGpsInterpolationService
{
    private readonly ILogger<GpsInterpolationService> _logger;
    private const double EARTH_RADIUS_KM = 6371.0;

    public GpsInterpolationService(ILogger<GpsInterpolationService> logger)
    {
        _logger = logger;
    }

    public List<InterpolatedPoint> InterpolateRoute(List<GpsTrackPoint> points, InterpolationOptions? options = null)
    {
        options ??= new InterpolationOptions();
        
        if (points.Count < 2)
        {
            return points.Select(p => new InterpolatedPoint
            {
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Timestamp = p.Timestamp,
                Speed = p.Speed,
                Heading = p.Heading ?? 0,
                IsOriginal = true,
                IsInterpolated = false
            }).ToList();
        }

        var result = new List<InterpolatedPoint>();

        for (int i = 0; i < points.Count - 1; i++)
        {
            var from = points[i];
            var to = points[i + 1];

            // Add the original point
            result.Add(new InterpolatedPoint
            {
                Latitude = from.Latitude,
                Longitude = from.Longitude,
                Timestamp = from.Timestamp,
                Speed = from.Speed,
                Heading = from.Heading ?? 0,
                IsOriginal = true,
                IsInterpolated = false
            });

            // Calculate interpolated points between from and to
            var interpolated = InterpolateSegment(from, to, options);
            result.AddRange(interpolated);
        }

        // Add the last point
        var last = points[^1];
        result.Add(new InterpolatedPoint
        {
            Latitude = last.Latitude,
            Longitude = last.Longitude,
            Timestamp = last.Timestamp,
            Speed = last.Speed,
            Heading = last.Heading ?? 0,
            IsOriginal = true,
            IsInterpolated = false
        });

        return result;
    }

    public List<InterpolatedPoint> SmartInterpolate(List<GpsTrackPoint> points)
    {
        if (points.Count < 2)
        {
            return points.Select(p => new InterpolatedPoint
            {
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Timestamp = p.Timestamp,
                Speed = p.Speed,
                Heading = p.Heading ?? 0,
                IsOriginal = true,
                IsInterpolated = false
            }).ToList();
        }

        var result = new List<InterpolatedPoint>();

        for (int i = 0; i < points.Count - 1; i++)
        {
            var from = points[i];
            var to = points[i + 1];

            // Add the original point
            result.Add(new InterpolatedPoint
            {
                Latitude = from.Latitude,
                Longitude = from.Longitude,
                Timestamp = from.Timestamp,
                Speed = from.Speed,
                Heading = from.Heading ?? 0,
                IsOriginal = true,
                IsInterpolated = false
            });

            // Smart interpolation based on speed and time
            var interpolated = SmartInterpolateSegment(from, to);
            result.AddRange(interpolated);
        }

        // Add the last point
        var last = points[^1];
        result.Add(new InterpolatedPoint
        {
            Latitude = last.Latitude,
            Longitude = last.Longitude,
            Timestamp = last.Timestamp,
            Speed = last.Speed,
            Heading = last.Heading ?? 0,
            IsOriginal = true,
            IsInterpolated = false
        });

        _logger.LogInformation("Smart interpolation: {Original} points -> {Total} points", 
            points.Count, result.Count);

        return result;
    }

    private List<InterpolatedPoint> InterpolateSegment(GpsTrackPoint from, GpsTrackPoint to, InterpolationOptions options)
    {
        var result = new List<InterpolatedPoint>();

        var distance = HaversineDistance(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
        var timeDiff = (to.Timestamp - from.Timestamp).TotalSeconds;

        // Skip if points are too close or no time difference
        if (distance < options.MinDistanceMeters || timeDiff <= 0)
        {
            return result;
        }

        // Calculate number of interpolated points based on distance
        int numPoints = Math.Max(1, (int)(distance / options.InterpolationIntervalMeters));
        numPoints = Math.Min(numPoints, options.MaxPointsPerSegment);

        for (int j = 1; j <= numPoints; j++)
        {
            double t = (double)j / (numPoints + 1);

            // Linear interpolation of position
            var lat = from.Latitude + (to.Latitude - from.Latitude) * t;
            var lon = from.Longitude + (to.Longitude - from.Longitude) * t;

            // Interpolate time
            var timestamp = from.Timestamp.AddSeconds(timeDiff * t);

            // Interpolate speed (weighted average favoring destination)
            var speed = from.Speed + (to.Speed - from.Speed) * t;

            // Calculate heading from interpolated position to next
            var heading = CalculateBearing(lat, lon, to.Latitude, to.Longitude);

            result.Add(new InterpolatedPoint
            {
                Latitude = lat,
                Longitude = lon,
                Timestamp = timestamp,
                Speed = speed,
                Heading = heading,
                IsOriginal = false,
                IsInterpolated = true,
                InterpolationFactor = t
            });
        }

        return result;
    }

    private List<InterpolatedPoint> SmartInterpolateSegment(GpsTrackPoint from, GpsTrackPoint to)
    {
        var result = new List<InterpolatedPoint>();

        var distance = HaversineDistance(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
        var timeDiff = (to.Timestamp - from.Timestamp).TotalSeconds;

        // Skip if no time difference or vehicle is stopped
        if (timeDiff <= 0)
        {
            return result;
        }

        // Average speed for this segment (km/h)
        var avgSpeedKmh = (distance / 1000.0) / (timeDiff / 3600.0);
        
        // If both points have speed data, use the average of recorded speeds
        var recordedAvgSpeed = (from.Speed + to.Speed) / 2.0;

        // Use the more reliable speed estimate
        var estimatedSpeed = recordedAvgSpeed > 0 ? recordedAvgSpeed : avgSpeedKmh;

        // Vehicle is stopped (speed < 3 km/h)
        if (estimatedSpeed < 3)
        {
            return result;
        }

        // Calculate expected distance based on speed and time
        var expectedDistanceKm = estimatedSpeed * (timeDiff / 3600.0);
        var expectedDistanceM = expectedDistanceKm * 1000;

        // If actual distance is much larger than expected (GPS jump), limit interpolation
        if (distance > expectedDistanceM * 2 && distance > 500)
        {
            _logger.LogDebug("GPS jump detected: actual={Actual}m, expected={Expected}m", 
                distance, expectedDistanceM);
            // Still interpolate but with fewer points
            expectedDistanceM = distance;
        }

        // Determine interpolation density based on speed
        // Higher speed = fewer interpolation points (vehicle moving fast, less detail needed)
        // Lower speed = more interpolation points (vehicle moving slow, more detail for accuracy)
        double intervalMeters;
        if (estimatedSpeed > 80) intervalMeters = 50;      // Highway: every 50m
        else if (estimatedSpeed > 50) intervalMeters = 30; // Fast road: every 30m
        else if (estimatedSpeed > 30) intervalMeters = 20; // Urban: every 20m
        else intervalMeters = 10;                          // Slow/residential: every 10m

        int numPoints = Math.Max(0, (int)(distance / intervalMeters) - 1);
        numPoints = Math.Min(numPoints, 100); // Cap at 100 points per segment

        if (numPoints <= 0)
        {
            return result;
        }

        for (int j = 1; j <= numPoints; j++)
        {
            double t = (double)j / (numPoints + 1);

            // Use bezier-like smoothing for more natural curves
            // This considers the heading at start and end points
            var (lat, lon) = InterpolateWithHeading(from, to, t);

            // Interpolate time linearly
            var timestamp = from.Timestamp.AddSeconds(timeDiff * t);

            // Smooth speed interpolation using cosine for natural acceleration/deceleration
            var speedT = (1 - Math.Cos(t * Math.PI)) / 2; // Smooth ease-in-out
            var speed = from.Speed + (to.Speed - from.Speed) * speedT;

            // Calculate heading to next point
            var nextT = Math.Min(1.0, (double)(j + 1) / (numPoints + 1));
            var (nextLat, nextLon) = nextT >= 1.0 
                ? (to.Latitude, to.Longitude) 
                : InterpolateWithHeading(from, to, nextT);
            var heading = CalculateBearing(lat, lon, nextLat, nextLon);

            result.Add(new InterpolatedPoint
            {
                Latitude = lat,
                Longitude = lon,
                Timestamp = timestamp,
                Speed = speed,
                Heading = heading,
                IsOriginal = false,
                IsInterpolated = true,
                InterpolationFactor = t,
                EstimatedFromSpeed = true
            });
        }

        return result;
    }

    private (double lat, double lon) InterpolateWithHeading(GpsTrackPoint from, GpsTrackPoint to, double t)
    {
        // If we have heading information, use bezier curve for smoother path
        if (from.Heading.HasValue && to.Heading.HasValue)
        {
            // Calculate control points based on heading
            var distance = HaversineDistance(from.Latitude, from.Longitude, to.Latitude, to.Longitude);
            var controlDist = distance * 0.3; // 30% of distance for control point

            // First control point: extend from 'from' in direction of its heading
            var (cp1Lat, cp1Lon) = MovePoint(from.Latitude, from.Longitude, from.Heading.Value, controlDist);

            // Second control point: extend from 'to' in opposite direction of its heading
            var reverseHeading = (to.Heading.Value + 180) % 360;
            var (cp2Lat, cp2Lon) = MovePoint(to.Latitude, to.Longitude, reverseHeading, controlDist);

            // Cubic bezier interpolation
            var u = 1 - t;
            var lat = u * u * u * from.Latitude 
                    + 3 * u * u * t * cp1Lat 
                    + 3 * u * t * t * cp2Lat 
                    + t * t * t * to.Latitude;
            var lon = u * u * u * from.Longitude 
                    + 3 * u * u * t * cp1Lon 
                    + 3 * u * t * t * cp2Lon 
                    + t * t * t * to.Longitude;

            return (lat, lon);
        }

        // Linear interpolation fallback
        var linearLat = from.Latitude + (to.Latitude - from.Latitude) * t;
        var linearLon = from.Longitude + (to.Longitude - from.Longitude) * t;
        return (linearLat, linearLon);
    }

    private (double lat, double lon) MovePoint(double lat, double lon, double bearingDeg, double distanceM)
    {
        var bearingRad = bearingDeg * Math.PI / 180;
        var distanceRad = distanceM / 1000 / EARTH_RADIUS_KM;
        var latRad = lat * Math.PI / 180;
        var lonRad = lon * Math.PI / 180;

        var newLatRad = Math.Asin(
            Math.Sin(latRad) * Math.Cos(distanceRad) +
            Math.Cos(latRad) * Math.Sin(distanceRad) * Math.Cos(bearingRad)
        );

        var newLonRad = lonRad + Math.Atan2(
            Math.Sin(bearingRad) * Math.Sin(distanceRad) * Math.Cos(latRad),
            Math.Cos(distanceRad) - Math.Sin(latRad) * Math.Sin(newLatRad)
        );

        return (newLatRad * 180 / Math.PI, newLonRad * 180 / Math.PI);
    }

    private double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        => GeoMath.HaversineDistance(lat1, lon1, lat2, lon2);

    private double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
    {
        var lat1Rad = lat1 * Math.PI / 180;
        var lat2Rad = lat2 * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;

        var y = Math.Sin(dLon) * Math.Cos(lat2Rad);
        var x = Math.Cos(lat1Rad) * Math.Sin(lat2Rad) -
                Math.Sin(lat1Rad) * Math.Cos(lat2Rad) * Math.Cos(dLon);

        var bearing = Math.Atan2(y, x) * 180 / Math.PI;
        return (bearing + 360) % 360;
    }
}

// DTOs
public class GpsTrackPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Timestamp { get; set; }
    public double Speed { get; set; } // km/h
    public double? Heading { get; set; } // degrees (0-360)
    public bool? IgnitionOn { get; set; }
}

public class InterpolatedPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime Timestamp { get; set; }
    public double Speed { get; set; }
    public double Heading { get; set; }
    public bool IsOriginal { get; set; }
    public bool IsInterpolated { get; set; }
    public double InterpolationFactor { get; set; }
    public bool EstimatedFromSpeed { get; set; }
}

public class InterpolationOptions
{
    public double MinDistanceMeters { get; set; } = 5;
    public double InterpolationIntervalMeters { get; set; } = 20;
    public int MaxPointsPerSegment { get; set; } = 50;
}
