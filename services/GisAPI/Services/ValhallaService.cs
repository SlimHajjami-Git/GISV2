using System.Text.Json;
using System.Text.Json.Serialization;

namespace GisAPI.Services;

public interface IValhallaService
{
    Task<ValhallaRouteResult?> SnapToRoadAsync(List<ValhallaPoint> points);
    Task<ValhallaRouteResult?> GetRouteAsync(double originLat, double originLon, double destLat, double destLon);
    Task<ValhallaRouteResult?> GetRouteFromWaypointsAsync(List<ValhallaPoint> waypoints);
    Task<bool> IsAvailableAsync();
}

public class ValhallaService : IValhallaService
{
    private static readonly JsonSerializerOptions _serializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly JsonSerializerOptions _deserializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly ILogger<ValhallaService> _logger;
    private bool _isAvailable = true;

    public ValhallaService(IConfiguration configuration, ILogger<ValhallaService> logger)
    {
        _logger = logger;
        _baseUrl = configuration["Valhalla:Url"] ?? Environment.GetEnvironmentVariable("VALHALLA_URL") ?? "http://valhalla:8002";
        
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        _logger.LogInformation("Valhalla service initialized with URL: {Url}", _baseUrl);
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/status");
            _isAvailable = response.IsSuccessStatusCode;
            return _isAvailable;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Valhalla health check failed");
            _isAvailable = false;
            return false;
        }
    }

    public async Task<ValhallaRouteResult?> SnapToRoadAsync(List<ValhallaPoint> points)
    {
        if (points == null || points.Count == 0)
        {
            return new ValhallaRouteResult
            {
                Points = new List<SnappedPoint>(),
                TotalDistanceKm = 0,
                TotalTimeSeconds = 0
            };
        }

        try
        {
            var request = new ValhallaTraceRequest
            {
                Shape = points.Select(p => new ValhallaShapePoint
                {
                    Lat = p.Lat,
                    Lon = p.Lon,
                    Time = p.Timestamp?.ToUnixTimeSeconds(),
                    Radius = 50.0,
                    Heading = p.Heading,
                    HeadingTolerance = p.Heading.HasValue ? 45.0 : null
                }).ToList(),
                Costing = "auto",
                ShapeMatch = "map_snap",
                TraceOptions = new ValhallaTraceOptions
                {
                    SearchRadius = 50.0,
                    GpsAccuracy = 10.0,
                    BreakageDistance = 2000.0,
                    InterpolationDistance = 10.0
                }
            };

            var json = JsonSerializer.Serialize(request, _serializeOptions);

            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/trace_route", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Valhalla returned error {Status}: {Error}", response.StatusCode, error);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var valhallaResponse = JsonSerializer.Deserialize<ValhallaTraceResponse>(responseJson, _deserializeOptions);

            if (valhallaResponse == null)
            {
                return null;
            }

            var snappedPoints = new List<SnappedPoint>();

            if (valhallaResponse.MatchedPoints != null)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    var original = points[i];
                    if (i < valhallaResponse.MatchedPoints.Count)
                    {
                        var matched = valhallaResponse.MatchedPoints[i];
                        snappedPoints.Add(new SnappedPoint
                        {
                            OriginalLat = original.Lat,
                            OriginalLon = original.Lon,
                            SnappedLat = matched.Lat,
                            SnappedLon = matched.Lon,
                            DistanceFromRoad = matched.DistanceFromTracePoint ?? 0,
                            Timestamp = original.Timestamp,
                            IsMatched = matched.Type != "unmatched"
                        });
                    }
                    else
                    {
                        snappedPoints.Add(new SnappedPoint
                        {
                            OriginalLat = original.Lat,
                            OriginalLon = original.Lon,
                            SnappedLat = original.Lat,
                            SnappedLon = original.Lon,
                            DistanceFromRoad = 0,
                            Timestamp = original.Timestamp,
                            IsMatched = false
                        });
                    }
                }
            }
            else
            {
                // Fallback: return original points if no matched_points
                snappedPoints = points.Select(p => new SnappedPoint
                {
                    OriginalLat = p.Lat,
                    OriginalLon = p.Lon,
                    SnappedLat = p.Lat,
                    SnappedLon = p.Lon,
                    DistanceFromRoad = 0,
                    Timestamp = p.Timestamp,
                    IsMatched = false
                }).ToList();
            }

            // Extract encoded polyline from Valhalla response
            // Valhalla trace_route returns shape in: trip.legs[].shape (per leg) or trip.shape (overall)
            string? encodedPolyline = null;
            
            // Try trip.shape first (overall shape)
            if (!string.IsNullOrEmpty(valhallaResponse.Trip?.Shape))
            {
                encodedPolyline = valhallaResponse.Trip.Shape;
                _logger.LogInformation("Found shape in trip.shape");
            }
            // Try trip.legs[].shape (concatenate all legs)
            else if (valhallaResponse.Trip?.Legs != null && valhallaResponse.Trip.Legs.Count > 0)
            {
                // Decode each leg's shape and combine
                var allLegPoints = new List<double[]>();
                foreach (var leg in valhallaResponse.Trip.Legs)
                {
                    if (!string.IsNullOrEmpty(leg.Shape))
                    {
                        var legPoints = DecodePolyline(leg.Shape);
                        // Skip first point of subsequent legs to avoid duplicates
                        if (allLegPoints.Count > 0 && legPoints.Count > 0)
                            allLegPoints.AddRange(legPoints.Skip(1));
                        else
                            allLegPoints.AddRange(legPoints);
                    }
                }
                
                if (allLegPoints.Count > 0)
                {
                    _logger.LogInformation("Found shape in trip.legs: {LegCount} legs, {PointCount} total points", 
                        valhallaResponse.Trip.Legs.Count, allLegPoints.Count);
                    
                    return new ValhallaRouteResult
                    {
                        Points = snappedPoints,
                        TotalDistanceKm = valhallaResponse.Trip?.Summary?.Length ?? 0,
                        TotalTimeSeconds = valhallaResponse.Trip?.Summary?.Time ?? 0,
                        DecodedPolyline = allLegPoints,
                        // Même bugfix que GetRouteFromWaypointsAsync : ce retour
                        // anticipé n'alimentait jamais EncodedPolyline.
                        EncodedPolyline = allLegPoints.Count > 1 ? EncodePolyline(allLegPoints) : null
                    };
                }
            }
            // Try root shape
            else if (!string.IsNullOrEmpty(valhallaResponse.Shape))
            {
                encodedPolyline = valhallaResponse.Shape;
                _logger.LogInformation("Found shape in root.shape");
            }
            
            if (string.IsNullOrEmpty(encodedPolyline))
            {
                _logger.LogWarning("No shape found in Valhalla response. Response keys: trip={HasTrip}, trip.shape={TripShape}, trip.legs={LegCount}, root.shape={RootShape}",
                    valhallaResponse.Trip != null,
                    valhallaResponse.Trip?.Shape != null,
                    valhallaResponse.Trip?.Legs?.Count ?? 0,
                    valhallaResponse.Shape != null);
                    
                // Log raw response for debugging (first 500 chars)
                var rawPreview = responseJson.Length > 500 ? responseJson[..500] : responseJson;
                _logger.LogWarning("Valhalla raw response preview: {Response}", rawPreview);
            }

            var result = new ValhallaRouteResult
            {
                Points = snappedPoints,
                TotalDistanceKm = valhallaResponse.Trip?.Summary?.Length ?? 0,
                TotalTimeSeconds = valhallaResponse.Trip?.Summary?.Time ?? 0,
                EncodedPolyline = encodedPolyline
            };

            // Decode polyline to get the full road path for animation
            if (!string.IsNullOrEmpty(encodedPolyline))
            {
                var decoded = DecodePolyline(encodedPolyline);
                result.DecodedPolyline = decoded;
                _logger.LogInformation("Valhalla route decoded: {Count} road points from polyline", decoded.Count);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Valhalla trace_route");
            return null;
        }
    }

    public async Task<ValhallaRouteResult?> GetRouteAsync(double originLat, double originLon, double destLat, double destLon)
    {
        return await GetRouteFromWaypointsAsync(new List<ValhallaPoint>
        {
            new() { Lat = originLat, Lon = originLon },
            new() { Lat = destLat, Lon = destLon }
        });
    }

    /// <summary>
    /// Calculate actual road route between GPS waypoints using Valhalla /route endpoint.
    /// Unlike trace_route (map matching), this works even when points are far apart.
    /// </summary>
    public async Task<ValhallaRouteResult?> GetRouteFromWaypointsAsync(List<ValhallaPoint> waypoints)
    {
        if (waypoints == null || waypoints.Count < 2) return null;

        try
        {
            // Build Valhalla /route request with locations
            var locations = waypoints.Select(p => new Dictionary<string, object>
            {
                { "lat", p.Lat },
                { "lon", p.Lon },
                { "type", "through" } // "through" = don't stop, just pass through
            }).ToList();
            
            // First and last must be "break" type (start/end of route)
            locations[0]["type"] = "break";
            locations[^1]["type"] = "break";

            var requestObj = new Dictionary<string, object>
            {
                { "locations", locations },
                { "costing", "auto" },
                { "directions_options", new Dictionary<string, object> { { "units", "kilometers" } } }
            };

            var json = JsonSerializer.Serialize(requestObj);
            _logger.LogInformation("Valhalla /route request: {Count} waypoints", waypoints.Count);

            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/route", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Valhalla /route error {Status}: {Error}", response.StatusCode, error);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var valhallaResponse = JsonSerializer.Deserialize<ValhallaTraceResponse>(responseJson, _deserializeOptions);

            if (valhallaResponse?.Trip == null) return null;

            // Extract and decode shape from trip.legs[].shape
            var allPoints = new List<double[]>();
            if (valhallaResponse.Trip.Legs != null)
            {
                foreach (var leg in valhallaResponse.Trip.Legs)
                {
                    if (!string.IsNullOrEmpty(leg.Shape))
                    {
                        var legPoints = DecodePolyline(leg.Shape);
                        if (allPoints.Count > 0 && legPoints.Count > 0)
                            allPoints.AddRange(legPoints.Skip(1)); // Skip duplicate first point
                        else
                            allPoints.AddRange(legPoints);
                    }
                }
            }

            _logger.LogInformation("Valhalla /route success: {WaypointCount} waypoints -> {RoadPointCount} road points", 
                waypoints.Count, allPoints.Count);

            // Extract per-leg times and distances
            var legTimes = new List<double>();
            var legDistances = new List<double>();
            if (valhallaResponse.Trip.Legs != null)
            {
                foreach (var leg in valhallaResponse.Trip.Legs)
                {
                    legTimes.Add(leg.Summary?.Time ?? 0);
                    legDistances.Add(leg.Summary?.Length ?? 0);
                }
            }

            return new ValhallaRouteResult
            {
                Points = new List<SnappedPoint>(),
                TotalDistanceKm = valhallaResponse.Trip.Summary?.Length ?? 0,
                TotalTimeSeconds = valhallaResponse.Trip.Summary?.Time ?? 0,
                DecodedPolyline = allPoints.Count > 0 ? allPoints : null,
                // BUGFIX : EncodedPolyline n'était JAMAIS renseigné sur ce chemin
                // (les tronçons per-leg sont le cas normal) → toutes les tournées
                // stockaient EstimatedRoutePolyline = NULL et la carte du détail
                // retombait sur la liaison droite. Ré-encode les points assemblés.
                EncodedPolyline = allPoints.Count > 1 ? EncodePolyline(allPoints) : null,
                LegTimesSeconds = legTimes,
                LegDistancesKm = legDistances
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Valhalla /route");
            return null;
        }
    }

    /// <summary>
    /// Encode une liste de points [lat, lng] en polyline précision 6 (format
    /// Valhalla) — inverse exact de <see cref="DecodePolyline"/>. Sert à
    /// reconstituer EstimatedRoutePolyline quand Valhalla répond par tronçons.
    /// </summary>
    public static string EncodePolyline(List<double[]> points)
    {
        var sb = new System.Text.StringBuilder();
        long lastLat = 0, lastLng = 0;
        foreach (var p in points)
        {
            var lat = (long)Math.Round(p[0] * 1e6);
            var lng = (long)Math.Round(p[1] * 1e6);
            EncodeValue(sb, lat - lastLat);
            EncodeValue(sb, lng - lastLng);
            lastLat = lat;
            lastLng = lng;
        }
        return sb.ToString();
    }

    private static void EncodeValue(System.Text.StringBuilder sb, long value)
    {
        var v = value < 0 ? ~(value << 1) : value << 1;
        while (v >= 0x20)
        {
            sb.Append((char)((0x20 | (v & 0x1f)) + 63));
            v >>= 5;
        }
        sb.Append((char)(v + 63));
    }

    public static List<double[]> DecodePolyline(string encoded)
    {
        var points = new List<double[]>();
        int index = 0;
        int lat = 0;
        int lng = 0;

        while (index < encoded.Length)
        {
            int shift = 0;
            int result = 0;
            int b;

            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);

            lat += (result & 1) != 0 ? ~(result >> 1) : (result >> 1);

            shift = 0;
            result = 0;

            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);

            lng += (result & 1) != 0 ? ~(result >> 1) : (result >> 1);

            // Valhalla uses 6 decimal precision (1e6)
            points.Add(new[] { lat / 1e6, lng / 1e6 });
        }

        return points;
    }
}

// Request/Response DTOs
public class ValhallaPoint
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public double? Heading { get; set; }
}

public class ValhallaShapePoint
{
    [JsonPropertyName("lat")]
    public double Lat { get; set; }
    
    [JsonPropertyName("lon")]
    public double Lon { get; set; }
    
    [JsonPropertyName("time")]
    public long? Time { get; set; }
    
    [JsonPropertyName("radius")]
    public double? Radius { get; set; }
    
    [JsonPropertyName("heading")]
    public double? Heading { get; set; }
    
    [JsonPropertyName("heading_tolerance")]
    public double? HeadingTolerance { get; set; }
}

public class ValhallaTraceRequest
{
    [JsonPropertyName("shape")]
    public List<ValhallaShapePoint> Shape { get; set; } = new();
    
    [JsonPropertyName("costing")]
    public string Costing { get; set; } = "auto";
    
    [JsonPropertyName("shape_match")]
    public string ShapeMatch { get; set; } = "map_snap";
    
    [JsonPropertyName("trace_options")]
    public ValhallaTraceOptions? TraceOptions { get; set; }
}

public class ValhallaTraceOptions
{
    [JsonPropertyName("search_radius")]
    public double SearchRadius { get; set; } = 50.0;
    
    [JsonPropertyName("gps_accuracy")]
    public double GpsAccuracy { get; set; } = 10.0;
    
    [JsonPropertyName("breakage_distance")]
    public double BreakageDistance { get; set; } = 2000.0;
    
    [JsonPropertyName("interpolation_distance")]
    public double InterpolationDistance { get; set; } = 10.0;
}

public class ValhallaTraceResponse
{
    [JsonPropertyName("trip")]
    public ValhallaTrip? Trip { get; set; }
    
    [JsonPropertyName("matched_points")]
    public List<ValhallaMatchedPoint>? MatchedPoints { get; set; }
    
    [JsonPropertyName("shape")]
    public string? Shape { get; set; }
}

public class ValhallaTrip
{
    [JsonPropertyName("legs")]
    public List<ValhallaLeg>? Legs { get; set; }
    
    [JsonPropertyName("summary")]
    public ValhallaSummary? Summary { get; set; }
    
    [JsonPropertyName("shape")]
    public string? Shape { get; set; }
}

public class ValhallaLeg
{
    [JsonPropertyName("shape")]
    public string? Shape { get; set; }
    
    [JsonPropertyName("summary")]
    public ValhallaSummary? Summary { get; set; }
}

public class ValhallaSummary
{
    [JsonPropertyName("length")]
    public double Length { get; set; }
    
    [JsonPropertyName("time")]
    public double Time { get; set; }
}

public class ValhallaMatchedPoint
{
    [JsonPropertyName("lat")]
    public double Lat { get; set; }
    
    [JsonPropertyName("lon")]
    public double Lon { get; set; }
    
    [JsonPropertyName("type")]
    public string? Type { get; set; }
    
    [JsonPropertyName("edge_index")]
    public int? EdgeIndex { get; set; }
    
    [JsonPropertyName("distance_from_trace_point")]
    public double? DistanceFromTracePoint { get; set; }
}

public class SnappedPoint
{
    public double OriginalLat { get; set; }
    public double OriginalLon { get; set; }
    public double SnappedLat { get; set; }
    public double SnappedLon { get; set; }
    public double DistanceFromRoad { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public bool IsMatched { get; set; }
}

public class ValhallaRouteResult
{
    public List<SnappedPoint> Points { get; set; } = new();
    public double TotalDistanceKm { get; set; }
    public double TotalTimeSeconds { get; set; }
    public string? EncodedPolyline { get; set; }
    public List<double[]>? DecodedPolyline { get; set; }
    // Per-leg times in seconds (legs[0] = waypoint0→waypoint1, legs[1] = waypoint1→waypoint2, etc.)
    public List<double> LegTimesSeconds { get; set; } = new();
    public List<double> LegDistancesKm { get; set; } = new();
}
