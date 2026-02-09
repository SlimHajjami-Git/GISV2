using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GisAPI.Services;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RoutingController : ControllerBase
{
    private readonly IValhallaService _valhallaService;
    private readonly IGpsInterpolationService _interpolationService;
    private readonly ILogger<RoutingController> _logger;

    public RoutingController(
        IValhallaService valhallaService, 
        IGpsInterpolationService interpolationService,
        ILogger<RoutingController> logger)
    {
        _valhallaService = valhallaService;
        _interpolationService = interpolationService;
        _logger = logger;
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<ActionResult> HealthCheck()
    {
        var isAvailable = await _valhallaService.IsAvailableAsync();
        return Ok(new { 
            service = "valhalla",
            available = isAvailable,
            timestamp = DateTime.UtcNow
        });
    }

    [HttpPost("snap")]
    public async Task<ActionResult<ValhallaRouteResult>> SnapToRoad([FromBody] SnapToRoadRequest request)
    {
        if (request.Points == null || request.Points.Count == 0)
        {
            return BadRequest(new { error = "Points array is required" });
        }

        if (request.Points.Count > 10000)
        {
            return BadRequest(new { error = "Maximum 10000 points allowed per request" });
        }

        var valhallaPoints = request.Points.Select(p => new ValhallaPoint
        {
            Lat = p.Lat,
            Lon = p.Lon,
            Timestamp = p.Timestamp.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(p.Timestamp.Value) : null
        }).ToList();

        var result = await _valhallaService.SnapToRoadAsync(valhallaPoints);

        if (result == null)
        {
            return StatusCode(503, new { error = "Valhalla service unavailable" });
        }

        return Ok(result);
    }

    [HttpPost("route")]
    public async Task<ActionResult<ValhallaRouteResult>> GetRoute([FromBody] RouteRequest request)
    {
        var result = await _valhallaService.GetRouteAsync(
            request.OriginLat,
            request.OriginLon,
            request.DestinationLat,
            request.DestinationLon
        );

        if (result == null)
        {
            return StatusCode(503, new { error = "Valhalla service unavailable" });
        }

        return Ok(result);
    }

    [HttpPost("decode-polyline")]
    [AllowAnonymous]
    public ActionResult<List<double[]>> DecodePolyline([FromBody] DecodePolylineRequest request)
    {
        if (string.IsNullOrEmpty(request.EncodedPolyline))
        {
            return BadRequest(new { error = "encodedPolyline is required" });
        }

        try
        {
            var decoded = ValhallaService.DecodePolyline(request.EncodedPolyline);
            return Ok(new { points = decoded });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decode polyline");
            return BadRequest(new { error = "Invalid polyline encoding" });
        }
    }

    /// <summary>
    /// Interpolate GPS points based on time and speed for smooth playback
    /// This creates intermediate points between GPS readings for fluid animation
    /// </summary>
    [HttpPost("interpolate")]
    public ActionResult<InterpolateResponse> InterpolateRoute([FromBody] InterpolateRequest request)
    {
        if (request.Points == null || request.Points.Count < 2)
        {
            return BadRequest(new { error = "At least 2 points are required" });
        }

        if (request.Points.Count > 10000)
        {
            return BadRequest(new { error = "Maximum 10000 points allowed per request" });
        }

        var trackPoints = request.Points.Select(p => new GpsTrackPoint
        {
            Latitude = p.Lat,
            Longitude = p.Lon,
            Timestamp = p.Timestamp.HasValue 
                ? DateTimeOffset.FromUnixTimeMilliseconds(p.Timestamp.Value).UtcDateTime 
                : DateTime.UtcNow,
            Speed = p.Speed ?? 0,
            Heading = p.Heading,
            IgnitionOn = p.IgnitionOn
        }).ToList();

        var interpolated = request.SmartMode 
            ? _interpolationService.SmartInterpolate(trackPoints)
            : _interpolationService.InterpolateRoute(trackPoints, new InterpolationOptions
            {
                MinDistanceMeters = request.MinDistanceMeters ?? 5,
                InterpolationIntervalMeters = request.IntervalMeters ?? 20,
                MaxPointsPerSegment = request.MaxPointsPerSegment ?? 50
            });

        return Ok(new InterpolateResponse
        {
            Points = interpolated.Select(p => new InterpolatedPointDto
            {
                Lat = p.Latitude,
                Lon = p.Longitude,
                Timestamp = new DateTimeOffset(p.Timestamp).ToUnixTimeMilliseconds(),
                Speed = p.Speed,
                Heading = p.Heading,
                IsOriginal = p.IsOriginal,
                IsInterpolated = p.IsInterpolated
            }).ToList(),
            OriginalCount = request.Points.Count,
            InterpolatedCount = interpolated.Count
        });
    }

    /// <summary>
    /// Smart route processing: interpolate + road snap for best results
    /// Combines time/speed-based interpolation with Valhalla map-matching
    /// </summary>
    [HttpPost("process")]
    public async Task<ActionResult<ProcessedRouteResponse>> ProcessRoute([FromBody] ProcessRouteRequest request)
    {
        if (request.Points == null || request.Points.Count < 2)
        {
            return BadRequest(new { error = "At least 2 points are required" });
        }

        // Step 1: Smart interpolation based on speed and time
        var trackPoints = request.Points.Select(p => new GpsTrackPoint
        {
            Latitude = p.Lat,
            Longitude = p.Lon,
            Timestamp = p.Timestamp.HasValue 
                ? DateTimeOffset.FromUnixTimeMilliseconds(p.Timestamp.Value).UtcDateTime 
                : DateTime.UtcNow,
            Speed = p.Speed ?? 0,
            Heading = p.Heading,
            IgnitionOn = p.IgnitionOn
        }).ToList();

        var interpolated = _interpolationService.SmartInterpolate(trackPoints);

        // Step 2: Try Valhalla road snapping if enabled
        List<SnappedPoint>? snappedPoints = null;
        List<double[]>? roadPath = null; // The actual road path from Valhalla polyline
        bool valhallaSuccess = false;

        if (request.EnableRoadSnapping)
        {
            try
            {
                var valhallaPoints = interpolated.Select(p => new ValhallaPoint
                {
                    Lat = p.Latitude,
                    Lon = p.Longitude,
                    Timestamp = new DateTimeOffset(p.Timestamp)
                }).ToList();

                var valhallaResult = await _valhallaService.SnapToRoadAsync(valhallaPoints);
                
                if (valhallaResult != null && valhallaResult.Points.Count > 0)
                {
                    snappedPoints = valhallaResult.Points;
                    valhallaSuccess = true;
                    
                    // Get the decoded polyline (actual road path with all road points)
                    if (valhallaResult.DecodedPolyline != null && valhallaResult.DecodedPolyline.Count > 0)
                    {
                        roadPath = valhallaResult.DecodedPolyline;
                        _logger.LogInformation("Valhalla road path: {Count} road points from polyline", roadPath.Count);
                    }
                    
                    _logger.LogInformation("Valhalla road snapping successful: {Count} snapped points", snappedPoints.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Valhalla road snapping failed, using interpolated points");
            }
        }

        // Build response - use snapped points for GPS markers
        var resultPoints = valhallaSuccess && snappedPoints != null
            ? snappedPoints.Select((p, i) => new ProcessedPointDto
            {
                Lat = p.SnappedLat,
                Lon = p.SnappedLon,
                OriginalLat = p.OriginalLat,
                OriginalLon = p.OriginalLon,
                Timestamp = interpolated[Math.Min(i, interpolated.Count - 1)].Timestamp,
                Speed = interpolated[Math.Min(i, interpolated.Count - 1)].Speed,
                Heading = interpolated[Math.Min(i, interpolated.Count - 1)].Heading,
                IsSnapped = p.IsMatched,
                DistanceFromRoad = p.DistanceFromRoad
            }).ToList()
            : interpolated.Select(p => new ProcessedPointDto
            {
                Lat = p.Latitude,
                Lon = p.Longitude,
                OriginalLat = p.Latitude,
                OriginalLon = p.Longitude,
                Timestamp = p.Timestamp,
                Speed = p.Speed,
                Heading = p.Heading,
                IsSnapped = false,
                DistanceFromRoad = 0
            }).ToList();

        // Convert road path to DTO format
        var roadPathDto = roadPath?.Select(p => new RoadPointDto { Lat = p[0], Lon = p[1] }).ToList();

        return Ok(new ProcessedRouteResponse
        {
            Points = resultPoints,
            RoadPath = roadPathDto, // Full road path for vehicle animation
            OriginalCount = request.Points.Count,
            InterpolatedCount = interpolated.Count,
            FinalCount = resultPoints.Count,
            RoadPathCount = roadPath?.Count ?? 0,
            RoadSnappingApplied = valhallaSuccess
        });
    }
}

public class InterpolateRequest
{
    public List<TrackPointDto> Points { get; set; } = new();
    public bool SmartMode { get; set; } = true;
    public double? MinDistanceMeters { get; set; }
    public double? IntervalMeters { get; set; }
    public int? MaxPointsPerSegment { get; set; }
}

public class TrackPointDto
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public long? Timestamp { get; set; }
    public double? Speed { get; set; }
    public double? Heading { get; set; }
    public bool? IgnitionOn { get; set; }
}

public class InterpolateResponse
{
    public List<InterpolatedPointDto> Points { get; set; } = new();
    public int OriginalCount { get; set; }
    public int InterpolatedCount { get; set; }
}

public class InterpolatedPointDto
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public long Timestamp { get; set; }
    public double Speed { get; set; }
    public double Heading { get; set; }
    public bool IsOriginal { get; set; }
    public bool IsInterpolated { get; set; }
}

public class ProcessRouteRequest
{
    public List<TrackPointDto> Points { get; set; } = new();
    public bool EnableRoadSnapping { get; set; } = true;
}

public class ProcessedRouteResponse
{
    public List<ProcessedPointDto> Points { get; set; } = new();
    public List<RoadPointDto>? RoadPath { get; set; } // Full road path for vehicle animation
    public int OriginalCount { get; set; }
    public int InterpolatedCount { get; set; }
    public int FinalCount { get; set; }
    public int RoadPathCount { get; set; }
    public bool RoadSnappingApplied { get; set; }
}

public class RoadPointDto
{
    public double Lat { get; set; }
    public double Lon { get; set; }
}

public class ProcessedPointDto
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public double OriginalLat { get; set; }
    public double OriginalLon { get; set; }
    public DateTime Timestamp { get; set; }
    public double Speed { get; set; }
    public double Heading { get; set; }
    public bool IsSnapped { get; set; }
    public double DistanceFromRoad { get; set; }
}

public class SnapToRoadRequest
{
    public List<SnapPoint> Points { get; set; } = new();
}

public class SnapPoint
{
    public double Lat { get; set; }
    public double Lon { get; set; }
    public long? Timestamp { get; set; }
}

public class RouteRequest
{
    public double OriginLat { get; set; }
    public double OriginLon { get; set; }
    public double DestinationLat { get; set; }
    public double DestinationLon { get; set; }
}

public class DecodePolylineRequest
{
    public string EncodedPolyline { get; set; } = string.Empty;
}
