using GisAPI.Domain.Entities;
using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Service for detecting and analyzing driving behavior patterns
/// </summary>
public interface IDrivingBehaviorService
{
    // Direction detection
    Task<DrivingEvent?> DetectTurnAsync(int vehicleId, double previousHeading, double currentHeading, double latitude, double longitude, DateTime timestamp);
    
    // Speed-based detection
    Task<DrivingEvent?> DetectHarshAccelerationAsync(int vehicleId, double previousSpeed, double currentSpeed, double timeDeltaSeconds, double latitude, double longitude, DateTime timestamp);
    Task<DrivingEvent?> DetectHarshBrakingAsync(int vehicleId, double previousSpeed, double currentSpeed, double timeDeltaSeconds, double latitude, double longitude, DateTime timestamp);
    Task<DrivingEvent?> DetectExcessiveSpeedAsync(int vehicleId, double currentSpeed, double speedLimit, double latitude, double longitude, DateTime timestamp);
    Task<DrivingEvent?> DetectIdlingAsync(int vehicleId, bool ignitionOn, double speed, TimeSpan idleDuration, double latitude, double longitude, DateTime timestamp);
    
    // MEMS-based detection (G-force)
    Task<DrivingEvent?> DetectMEMSEventAsync(int vehicleId, double accelX, double accelY, double accelZ, double speedKph, double latitude, double longitude, DateTime timestamp);
    Task<DrivingEvent?> DetectSpeedBumpAsync(int vehicleId, double accelZ, double speedKph, double latitude, double longitude, DateTime timestamp);
    Task<DrivingEvent?> DetectPotholeAsync(int vehicleId, double accelZ, double latitude, double longitude, DateTime timestamp);
    
    // Engine events
    Task<DrivingEvent?> DetectHighRpmAsync(int vehicleId, int rpm, double latitude, double longitude, DateTime timestamp);
    Task<DrivingEvent?> DetectIgnitionChangeAsync(int vehicleId, bool ignitionOn, double latitude, double longitude, DateTime timestamp);
    
    // Power events
    Task<DrivingEvent?> DetectLowBatteryAsync(int vehicleId, int powerVoltage, double latitude, double longitude, DateTime timestamp);
    
    // Scoring
    Task<DrivingScore> CalculateDrivingScoreAsync(int vehicleId, DateTime startDate, DateTime endDate);
    Task<List<DrivingEvent>> GetDrivingEventsAsync(int vehicleId, DateTime startDate, DateTime endDate);
}

public class DrivingBehaviorService : IDrivingBehaviorService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DrivingBehaviorService> _logger;

    // Configurable thresholds - based on GISV1 values
    private const double TURN_ANGLE_THRESHOLD = 45.0;          // degrees for significant turn
    private const double SHARP_TURN_THRESHOLD = 90.0;          // degrees for sharp turn
    private const double HARSH_ACCELERATION_THRESHOLD = 3.5;   // m/s² (0-100 km/h in ~8s)
    private const double HARSH_BRAKING_THRESHOLD = -4.0;       // m/s² (negative for deceleration)
    private const double EXCESSIVE_IDLE_MINUTES = 5.0;         // minutes of idling
    private const double DEFAULT_SPEED_LIMIT = 120.0;          // km/h
    
    // MEMS thresholds (G-force) - from GISV1 AAP.cs
    private const double MEMS_HARSH_BRAKING_G = -0.4;          // AccelX < -0.4G = harsh braking
    private const double MEMS_HARSH_ACCEL_G = 0.4;             // AccelX > 0.4G = harsh acceleration
    private const double MEMS_HARSH_CORNERING_G = 0.4;         // |AccelY| > 0.4G = harsh cornering
    private const double MEMS_SPEED_BUMP_G = 0.5;              // AccelZ > 0.5G = speed bump
    private const double MEMS_POTHOLE_G = -0.6;                // AccelZ < -0.6G = pothole
    private const double SPEED_BUMP_MIN_SPEED = 30.0;          // km/h minimum for speed bump detection
    
    // Engine thresholds
    private const int HIGH_RPM_THRESHOLD = 3200;               // RPM > 3200 = high RPM
    private const int LOW_BATTERY_THRESHOLD = 128;             // Power < 128 = low battery

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
    /// Detect driving events from MEMS accelerometer data (G-force)
    /// Based on GISV1 AAP.cs thresholds
    /// </summary>
    public async Task<DrivingEvent?> DetectMEMSEventAsync(
        int vehicleId,
        double accelX,  // Acceleration/Braking axis
        double accelY,  // Cornering axis (left/right)
        double accelZ,  // Vertical axis (bumps/potholes)
        double speedKph,
        double latitude,
        double longitude,
        DateTime timestamp)
    {
        // Priority order: most severe events first
        
        // 1. Harsh braking (X negative)
        if (accelX < MEMS_HARSH_BRAKING_G)
        {
            var severity = accelX < -0.6 ? DrivingEventSeverities.Critical : DrivingEventSeverities.High;
            return await SaveAndReturnEventAsync(vehicleId, DrivingEventTypes.HarshBraking, severity, 
                Math.Abs(accelX), speedKph, latitude, longitude, timestamp);
        }
        
        // 2. Harsh acceleration (X positive)
        if (accelX > MEMS_HARSH_ACCEL_G)
        {
            var severity = accelX > 0.6 ? DrivingEventSeverities.High : DrivingEventSeverities.Medium;
            return await SaveAndReturnEventAsync(vehicleId, DrivingEventTypes.HarshAcceleration, severity,
                accelX, speedKph, latitude, longitude, timestamp);
        }
        
        // 3. Harsh cornering (Y axis)
        if (Math.Abs(accelY) > MEMS_HARSH_CORNERING_G)
        {
            var eventType = accelY > 0 ? DrivingEventTypes.TurnRight : DrivingEventTypes.TurnLeft;
            var severity = Math.Abs(accelY) > 0.6 ? DrivingEventSeverities.High : DrivingEventSeverities.Medium;
            return await SaveAndReturnEventAsync(vehicleId, eventType, severity,
                Math.Abs(accelY), speedKph, latitude, longitude, timestamp);
        }
        
        // 4. Speed bump (Z positive + speed > 30 km/h)
        if (accelZ > MEMS_SPEED_BUMP_G && speedKph > SPEED_BUMP_MIN_SPEED)
        {
            return await SaveAndReturnEventAsync(vehicleId, DrivingEventTypes.SpeedBump, DrivingEventSeverities.Medium,
                accelZ, speedKph, latitude, longitude, timestamp);
        }
        
        // 5. Pothole (Z negative)
        if (accelZ < MEMS_POTHOLE_G)
        {
            return await SaveAndReturnEventAsync(vehicleId, DrivingEventTypes.Pothole, DrivingEventSeverities.Low,
                Math.Abs(accelZ), speedKph, latitude, longitude, timestamp);
        }
        
        return null;
    }

    /// <summary>
    /// Detect speed bump from vertical acceleration
    /// </summary>
    public async Task<DrivingEvent?> DetectSpeedBumpAsync(
        int vehicleId,
        double accelZ,
        double speedKph,
        double latitude,
        double longitude,
        DateTime timestamp)
    {
        if (accelZ <= MEMS_SPEED_BUMP_G || speedKph <= SPEED_BUMP_MIN_SPEED)
            return null;

        var severity = accelZ > 0.8 ? DrivingEventSeverities.High : DrivingEventSeverities.Medium;
        return await SaveAndReturnEventAsync(vehicleId, DrivingEventTypes.SpeedBump, severity,
            accelZ, speedKph, latitude, longitude, timestamp);
    }

    /// <summary>
    /// Detect pothole from negative vertical acceleration
    /// </summary>
    public async Task<DrivingEvent?> DetectPotholeAsync(
        int vehicleId,
        double accelZ,
        double latitude,
        double longitude,
        DateTime timestamp)
    {
        if (accelZ >= MEMS_POTHOLE_G)
            return null;

        var severity = accelZ < -0.8 ? DrivingEventSeverities.High : DrivingEventSeverities.Medium;
        return await SaveAndReturnEventAsync(vehicleId, DrivingEventTypes.Pothole, severity,
            Math.Abs(accelZ), null, latitude, longitude, timestamp);
    }

    /// <summary>
    /// Detect high RPM events
    /// </summary>
    public async Task<DrivingEvent?> DetectHighRpmAsync(
        int vehicleId,
        int rpm,
        double latitude,
        double longitude,
        DateTime timestamp)
    {
        if (rpm <= HIGH_RPM_THRESHOLD)
            return null;

        var severity = rpm switch
        {
            > 5000 => DrivingEventSeverities.Critical,
            > 4000 => DrivingEventSeverities.High,
            _ => DrivingEventSeverities.Medium
        };

        var drivingEvent = new DrivingEvent
        {
            VehicleId = vehicleId,
            Type = DrivingEventTypes.HighRpm,
            Severity = severity,
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp,
            Metadata = new Dictionary<string, object> { { "rpm", rpm } }
        };

        await SaveDrivingEventAsync(drivingEvent);
        return drivingEvent;
    }

    /// <summary>
    /// Detect ignition state changes
    /// </summary>
    public async Task<DrivingEvent?> DetectIgnitionChangeAsync(
        int vehicleId,
        bool ignitionOn,
        double latitude,
        double longitude,
        DateTime timestamp)
    {
        var eventType = ignitionOn ? DrivingEventTypes.IgnitionOn : DrivingEventTypes.IgnitionOff;
        
        var drivingEvent = new DrivingEvent
        {
            VehicleId = vehicleId,
            Type = eventType,
            Severity = DrivingEventSeverities.Low,
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp
        };

        await SaveDrivingEventAsync(drivingEvent);
        return drivingEvent;
    }

    /// <summary>
    /// Detect low battery events
    /// </summary>
    public async Task<DrivingEvent?> DetectLowBatteryAsync(
        int vehicleId,
        int powerVoltage,
        double latitude,
        double longitude,
        DateTime timestamp)
    {
        if (powerVoltage >= LOW_BATTERY_THRESHOLD)
            return null;

        var severity = powerVoltage < 64 ? DrivingEventSeverities.Critical : DrivingEventSeverities.High;

        var drivingEvent = new DrivingEvent
        {
            VehicleId = vehicleId,
            Type = DrivingEventTypes.LowBattery,
            Severity = severity,
            Latitude = latitude,
            Longitude = longitude,
            Timestamp = timestamp,
            Metadata = new Dictionary<string, object> { { "voltage", powerVoltage } }
        };

        await SaveDrivingEventAsync(drivingEvent);
        return drivingEvent;
    }

    /// <summary>
    /// Helper to create and save a driving event
    /// </summary>
    private async Task<DrivingEvent> SaveAndReturnEventAsync(
        int vehicleId, string eventType, string severity,
        double gForce, double? speedKph,
        double latitude, double longitude, DateTime timestamp)
    {
        var drivingEvent = new DrivingEvent
        {
            VehicleId = vehicleId,
            Type = eventType,
            Severity = severity,
            GForce = gForce,
            SpeedKph = speedKph,
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
    // Direction changes
    public const string TurnLeft = "turn_left";
    public const string TurnRight = "turn_right";
    public const string Cornering = "cornering";
    
    // Acceleration events (MEMS X-axis)
    public const string HarshAcceleration = "harsh_acceleration";
    public const string HarshBraking = "harsh_braking";
    
    // Speed events
    public const string Speeding = "speeding";
    public const string ExcessiveIdling = "excessive_idling";
    
    // Road conditions (MEMS Z-axis)
    public const string SpeedBump = "speed_bump";
    public const string Pothole = "pothole";
    
    // Engine events
    public const string HighRpm = "high_rpm";
    public const string IgnitionOff = "ignition_off";
    public const string IgnitionOn = "ignition_on";
    
    // Power events
    public const string LowBattery = "low_battery";
    public const string PowerDisconnect = "power_disconnect";
    
    // Emergency
    public const string Sos = "sos";
    public const string Towing = "towing";
    
    // Geofence
    public const string GeofenceEntry = "geofence_entry";
    public const string GeofenceExit = "geofence_exit";
}

public static class DrivingEventSeverities
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";
}

#endregion
