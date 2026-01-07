using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Service for detecting and analyzing driving behavior patterns
/// </summary>
public interface IDrivingBehaviorService
{
    Task<DrivingEvent?> DetectTurnAsync(int vehicleId, double previousHeading, double currentHeading, double latitude, double longitude, DateTime timestamp);
    Task<DrivingEvent?> DetectHarshAccelerationAsync(int vehicleId, double previousSpeed, double currentSpeed, double timeDeltaSeconds, double latitude, double longitude, DateTime timestamp);
    Task<DrivingEvent?> DetectHarshBrakingAsync(int vehicleId, double previousSpeed, double currentSpeed, double timeDeltaSeconds, double latitude, double longitude, DateTime timestamp);
    Task<DrivingEvent?> DetectExcessiveSpeedAsync(int vehicleId, double currentSpeed, double speedLimit, double latitude, double longitude, DateTime timestamp);
    Task<DrivingEvent?> DetectIdlingAsync(int vehicleId, bool ignitionOn, double speed, TimeSpan idleDuration, double latitude, double longitude, DateTime timestamp);
    Task<DrivingScore> CalculateDrivingScoreAsync(int vehicleId, DateTime startDate, DateTime endDate);
    Task<List<DrivingEvent>> GetDrivingEventsAsync(int vehicleId, DateTime startDate, DateTime endDate);
}

public class DrivingBehaviorService : IDrivingBehaviorService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DrivingBehaviorService> _logger;

    // Configurable thresholds
    private const double TURN_ANGLE_THRESHOLD = 45.0;          // degrees for significant turn
    private const double SHARP_TURN_THRESHOLD = 90.0;          // degrees for sharp turn
    private const double HARSH_ACCELERATION_THRESHOLD = 3.5;   // m/s² (0-100 km/h in ~8s)
    private const double HARSH_BRAKING_THRESHOLD = -4.0;       // m/s² (negative for deceleration)
    private const double EXCESSIVE_IDLE_MINUTES = 5.0;         // minutes of idling
    private const double DEFAULT_SPEED_LIMIT = 120.0;          // km/h

    public DrivingBehaviorService(IServiceProvider serviceProvider, ILogger<DrivingBehaviorService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Detect turn direction based on heading change
    /// </summary>
    public async Task<DrivingEvent?> DetectTurnAsync(
        int vehicleId, 
        double previousHeading, 
        double currentHeading, 
        double latitude, 
        double longitude, 
        DateTime timestamp)
    {
        var angleDiff = NormalizeAngleDifference(currentHeading - previousHeading);
        
        if (Math.Abs(angleDiff) < TURN_ANGLE_THRESHOLD)
            return null; // No significant turn

        var eventType = angleDiff > 0 ? DrivingEventTypes.TurnRight : DrivingEventTypes.TurnLeft;
        var severity = Math.Abs(angleDiff) >= SHARP_TURN_THRESHOLD 
            ? DrivingEventSeverities.High 
            : DrivingEventSeverities.Low;

        var drivingEvent = new DrivingEvent
        {
            VehicleId = vehicleId,
            Type = eventType,
            Severity = severity,
            GForce = Math.Abs(angleDiff) / 90.0, // Normalize to G-force equivalent
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp
        };

        await SaveDrivingEventAsync(drivingEvent);
        return drivingEvent;
    }

    /// <summary>
    /// Detect harsh acceleration events
    /// </summary>
    public async Task<DrivingEvent?> DetectHarshAccelerationAsync(
        int vehicleId,
        double previousSpeed,
        double currentSpeed,
        double timeDeltaSeconds,
        double latitude,
        double longitude,
        DateTime timestamp)
    {
        if (timeDeltaSeconds <= 0) return null;

        // Convert km/h to m/s and calculate acceleration
        var speedDeltaMs = (currentSpeed - previousSpeed) / 3.6;
        var acceleration = speedDeltaMs / timeDeltaSeconds; // m/s²

        if (acceleration < HARSH_ACCELERATION_THRESHOLD)
            return null;

        var severity = acceleration switch
        {
            >= 5.0 => DrivingEventSeverities.Critical,
            >= 4.0 => DrivingEventSeverities.High,
            _ => DrivingEventSeverities.Medium
        };

        var drivingEvent = new DrivingEvent
        {
            VehicleId = vehicleId,
            Type = DrivingEventTypes.HarshAcceleration,
            Severity = severity,
            GForce = acceleration / 9.81, // Convert m/s² to G
            SpeedKph = currentSpeed,
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp
        };

        await SaveDrivingEventAsync(drivingEvent);
        return drivingEvent;
    }

    /// <summary>
    /// Detect harsh braking events
    /// </summary>
    public async Task<DrivingEvent?> DetectHarshBrakingAsync(
        int vehicleId,
        double previousSpeed,
        double currentSpeed,
        double timeDeltaSeconds,
        double latitude,
        double longitude,
        DateTime timestamp)
    {
        if (timeDeltaSeconds <= 0) return null;

        // Convert km/h to m/s and calculate deceleration (negative value)
        var speedDeltaMs = (currentSpeed - previousSpeed) / 3.6;
        var deceleration = speedDeltaMs / timeDeltaSeconds; // m/s²

        if (deceleration > HARSH_BRAKING_THRESHOLD)
            return null; // Not harsh braking

        var severity = deceleration switch
        {
            <= -6.0 => DrivingEventSeverities.Critical,
            <= -5.0 => DrivingEventSeverities.High,
            _ => DrivingEventSeverities.Medium
        };

        var drivingEvent = new DrivingEvent
        {
            VehicleId = vehicleId,
            Type = DrivingEventTypes.HarshBraking,
            Severity = severity,
            GForce = Math.Abs(deceleration) / 9.81,
            SpeedKph = previousSpeed,
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp
        };

        await SaveDrivingEventAsync(drivingEvent);
        return drivingEvent;
    }

    /// <summary>
    /// Detect excessive speed events
    /// </summary>
    public async Task<DrivingEvent?> DetectExcessiveSpeedAsync(
        int vehicleId,
        double currentSpeed,
        double speedLimit,
        double latitude,
        double longitude,
        DateTime timestamp)
    {
        if (speedLimit <= 0) speedLimit = DEFAULT_SPEED_LIMIT;
        
        if (currentSpeed <= speedLimit)
            return null;

        var excessPercent = ((currentSpeed - speedLimit) / speedLimit) * 100;
        
        var severity = excessPercent switch
        {
            >= 50 => DrivingEventSeverities.Critical,
            >= 30 => DrivingEventSeverities.High,
            >= 15 => DrivingEventSeverities.Medium,
            _ => DrivingEventSeverities.Low
        };

        var drivingEvent = new DrivingEvent
        {
            VehicleId = vehicleId,
            Type = DrivingEventTypes.Speeding,
            Severity = severity,
            SpeedKph = currentSpeed,
            SpeedLimitKph = speedLimit,
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp
        };

        await SaveDrivingEventAsync(drivingEvent);
        return drivingEvent;
    }

    /// <summary>
    /// Detect excessive idling
    /// </summary>
    public async Task<DrivingEvent?> DetectIdlingAsync(
        int vehicleId,
        bool ignitionOn,
        double speed,
        TimeSpan idleDuration,
        double latitude,
        double longitude,
        DateTime timestamp)
    {
        if (!ignitionOn || speed > 2 || idleDuration.TotalMinutes < EXCESSIVE_IDLE_MINUTES)
            return null;

        var severity = idleDuration.TotalMinutes switch
        {
            >= 30 => DrivingEventSeverities.High,
            >= 15 => DrivingEventSeverities.Medium,
            _ => DrivingEventSeverities.Low
        };

        var drivingEvent = new DrivingEvent
        {
            VehicleId = vehicleId,
            Type = DrivingEventTypes.ExcessiveIdling,
            Severity = severity,
            DurationSeconds = (int)idleDuration.TotalSeconds,
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp
        };

        await SaveDrivingEventAsync(drivingEvent);
        return drivingEvent;
    }

    /// <summary>
    /// Calculate driving score for a vehicle over a period
    /// </summary>
    public async Task<DrivingScore> CalculateDrivingScoreAsync(int vehicleId, DateTime startDate, DateTime endDate)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();

        var vehicle = await context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == vehicleId);

        if (vehicle?.GpsDevice == null)
        {
            return new DrivingScore { VehicleId = vehicleId, Score = 100, Period = $"{startDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd}" };
        }

        var events = await context.DrivingEvents
            .Where(e => e.VehicleId == vehicleId 
                     && e.Timestamp >= startDate 
                     && e.Timestamp <= endDate)
            .ToListAsync();

        // Base score of 100, deduct points for events
        double score = 100;
        int harshBrakingCount = 0, harshAccelCount = 0, speedingCount = 0, sharpTurnCount = 0;

        foreach (var evt in events)
        {
            var deduction = evt.Severity switch
            {
                DrivingEventSeverities.Critical => 5.0,
                DrivingEventSeverities.High => 3.0,
                DrivingEventSeverities.Medium => 1.5,
                DrivingEventSeverities.Low => 0.5,
                _ => 0
            };

            score -= deduction;

            if (evt.Type == DrivingEventTypes.HarshBraking) harshBrakingCount++;
            else if (evt.Type == DrivingEventTypes.HarshAcceleration) harshAccelCount++;
            else if (evt.Type == DrivingEventTypes.Speeding) speedingCount++;
            else if ((evt.Type == DrivingEventTypes.TurnLeft || evt.Type == DrivingEventTypes.TurnRight) 
                     && evt.Severity == DrivingEventSeverities.High) sharpTurnCount++;
        }

        return new DrivingScore
        {
            VehicleId = vehicleId,
            Score = Math.Max(0, Math.Min(100, score)),
            Period = $"{startDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd}",
            TotalEvents = events.Count,
            HarshBrakingCount = harshBrakingCount,
            HarshAccelerationCount = harshAccelCount,
            SpeedingCount = speedingCount,
            SharpTurnCount = sharpTurnCount,
            Grade = score switch
            {
                >= 90 => "A",
                >= 80 => "B",
                >= 70 => "C",
                >= 60 => "D",
                _ => "F"
            }
        };
    }

    /// <summary>
    /// Get driving events for a vehicle
    /// </summary>
    public async Task<List<DrivingEvent>> GetDrivingEventsAsync(int vehicleId, DateTime startDate, DateTime endDate)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();

        return await context.DrivingEvents
            .Where(e => e.VehicleId == vehicleId 
                     && e.Timestamp >= startDate 
                     && e.Timestamp <= endDate)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    private async Task SaveDrivingEventAsync(DrivingEvent drivingEvent)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<GisDbContext>();
            context.DrivingEvents.Add(drivingEvent);
            await context.SaveChangesAsync();
            
            _logger.LogInformation("Driving event detected: {Type} at ({Lat}, {Lon})", 
                drivingEvent.Type, drivingEvent.Latitude, drivingEvent.Longitude);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save driving event");
        }
    }

    /// <summary>
    /// Normalize angle difference to -180 to 180 range
    /// </summary>
    private static double NormalizeAngleDifference(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }
}

#region Models

public class DrivingScore
{
    public int VehicleId { get; set; }
    public double Score { get; set; }
    public string Grade { get; set; } = "A";
    public string Period { get; set; } = string.Empty;
    public int TotalEvents { get; set; }
    public int HarshBrakingCount { get; set; }
    public int HarshAccelerationCount { get; set; }
    public int SpeedingCount { get; set; }
    public int SharpTurnCount { get; set; }
}

public static class DrivingEventTypes
{
    public const string TurnLeft = "turn_left";
    public const string TurnRight = "turn_right";
    public const string HarshAcceleration = "harsh_acceleration";
    public const string HarshBraking = "harsh_braking";
    public const string Speeding = "speeding";
    public const string ExcessiveIdling = "excessive_idling";
    public const string Cornering = "cornering";
}

public static class DrivingEventSeverities
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";
}

#endregion
