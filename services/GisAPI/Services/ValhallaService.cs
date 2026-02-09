using System.Text.Json;
using System.Text.Json.Serialization;

namespace GisAPI.Services;

public interface IValhallaService
{
    Task<ValhallaRouteResult?> SnapToRoadAsync(List<ValhallaPoint> points);
    Task<ValhallaRouteResult?> GetRouteAsync(double originLat, double originLon, double destLat, double destLon);
    Task<bool> IsAvailableAsync();
}

public class ValhallaService : IValhallaService
{
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
                    Radius = 50.0
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

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/trace_route", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Valhalla returned error {Status}: {Error}", response.StatusCode, error);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            var valhallaResponse = JsonSerializer.Deserialize<ValhallaTraceResponse>(responseJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

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

            var result = new ValhallaRouteResult
            {
                Points = snappedPoints,
                TotalDistanceKm = valhallaResponse.Trip?.Summary?.Length ?? 0,
                TotalTimeSeconds = valhallaResponse.Trip?.Summary?.Time ?? 0,
                EncodedPolyline = valhallaResponse.Trip?.Shape ?? valhallaResponse.Shape
            };

            // Decode polyline if present and no matched points
            if (!string.IsNullOrEmpty(result.EncodedPolyline) && snappedPoints.All(p => !p.IsMatched))
            {
                var decoded = DecodePolyline(result.EncodedPolyline);
                result.DecodedPolyline = decoded;
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
        return await SnapToRoadAsync(new List<ValhallaPoint>
        {
            new() { Lat = originLat, Lon = originLon },
            new() { Lat = destLat, Lon = destLon }
        });
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
}
