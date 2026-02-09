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

        // Calculate route SEGMENT BY SEGMENT (A->B, B->C, C->D...)
        // This ensures the vehicle reaches each GPS point exactly
        var roadPath = new List<double[]>();
        var segmentBoundaries = new List<int> { 0 }; // Index in roadPath where each GPS point starts
        bool valhallaSuccess = false;
        string method = "none";
        int segmentsRouted = 0;

        for (int i = 0; i < request.Points.Count - 1; i++)
        {
            var from = request.Points[i];
            var to = request.Points[i + 1];

            List<double[]>? segmentPath = null;

            // Try Valhalla /route for this segment
            try
            {
                var segmentResult = await _valhallaService.GetRouteFromWaypointsAsync(new List<ValhallaPoint>
                {
                    new() { Lat = from.Lat, Lon = from.Lon },
                    new() { Lat = to.Lat, Lon = to.Lon }
                });

                if (segmentResult?.DecodedPolyline != null && segmentResult.DecodedPolyline.Count >= 2)
                {
                    segmentPath = segmentResult.DecodedPolyline;
                    segmentsRouted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Valhalla /route failed for segment {Index}", i);
            }

            // Add segment points (or straight line fallback)
            if (segmentPath != null && segmentPath.Count >= 2)
            {
                // Skip first point of subsequent segments to avoid duplicates
                var pointsToAdd = roadPath.Count > 0 ? segmentPath.Skip(1) : segmentPath;
                roadPath.AddRange(pointsToAdd);
            }
            else
            {
                // Fallback: straight line between the two GPS points
                if (roadPath.Count == 0)
                    roadPath.Add(new[] { from.Lat, from.Lon });
                roadPath.Add(new[] { to.Lat, to.Lon });
            }

            // Record where this GPS segment ends in the roadPath
            segmentBoundaries.Add(roadPath.Count - 1);
        }

        if (segmentsRouted > 0)
        {
            valhallaSuccess = true;
            method = $"route_segments({segmentsRouted}/{request.Points.Count - 1})";
        }

        _logger.LogInformation("Route processing: {GpsCount} GPS -> {Segments} segments routed -> {RoadCount} road points",
            request.Points.Count, segmentsRouted, roadPath.Count);

        // Convert road path to response
        var roadPathDto = roadPath?.Select(p => new RoadPointDto { Lat = p[0], Lon = p[1] }).ToList();

        // Original GPS points as fallback
        var resultPoints = request.Points.Select(p => new ProcessedPointDto
        {
            Lat = p.Lat,
            Lon = p.Lon,
            OriginalLat = p.Lat,
            OriginalLon = p.Lon,
            Speed = p.Speed ?? 0,
            Heading = p.Heading ?? 0,
            IsSnapped = false,
            DistanceFromRoad = 0
        }).ToList();

        return Ok(new ProcessedRouteResponse
        {
            Points = resultPoints,
            RoadPath = roadPathDto,
            SegmentBoundaries = segmentBoundaries,
            OriginalCount = request.Points.Count,
            InterpolatedCount = request.Points.Count,
            FinalCount = resultPoints.Count,
            RoadPathCount = roadPath.Count,
            RoadSnappingApplied = valhallaSuccess,
            Method = method
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
    public List<int> SegmentBoundaries { get; set; } = new(); // roadPath indices where each GPS point maps to
    public int OriginalCount { get; set; }
    public int InterpolatedCount { get; set; }
    public int FinalCount { get; set; }
    public int RoadPathCount { get; set; }
    public bool RoadSnappingApplied { get; set; }
    public string Method { get; set; } = "none";
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
