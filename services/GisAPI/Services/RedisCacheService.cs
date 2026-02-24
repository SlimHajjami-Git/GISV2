using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace GisAPI.Services;

public interface IRedisCacheService
{
    Task<VehiclePositionCache?> GetPositionAsync(string deviceUid);
    Task<List<VehiclePositionCache>> GetAllPositionsForCompanyAsync(int companyId);
    Task SubscribeToUpdatesAsync(int companyId, Action<VehiclePositionCache> onUpdate);
}

public class VehiclePositionCache
{
    [JsonPropertyName("deviceUid")]
    public string DeviceUid { get; set; } = string.Empty;
    [JsonPropertyName("vehicleId")]
    public int? VehicleId { get; set; }
    [JsonPropertyName("companyId")]
    public int CompanyId { get; set; }
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }
    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
    [JsonPropertyName("speedKph")]
    public double SpeedKph { get; set; }
    [JsonPropertyName("headingDeg")]
    public double HeadingDeg { get; set; }
    [JsonPropertyName("ignitionOn")]
    public bool IgnitionOn { get; set; }
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }
    [JsonPropertyName("fuelRaw")]
    public int FuelRaw { get; set; }
    [JsonPropertyName("powerVoltage")]
    public int PowerVoltage { get; set; }
    [JsonPropertyName("batteryVoltage")]
    public double? BatteryVoltage { get; set; }
    [JsonPropertyName("batteryPercent")]
    public int? BatteryPercent { get; set; }
    [JsonPropertyName("powerSourceRescue")]
    public bool PowerSourceRescue { get; set; }
    [JsonPropertyName("temperatureC")]
    public int? TemperatureC { get; set; }
    [JsonPropertyName("odometerKm")]
    public long? OdometerKm { get; set; }
    [JsonPropertyName("rpm")]
    public int? Rpm { get; set; }
    [JsonPropertyName("memsX")]
    public int? MemsX { get; set; }
    [JsonPropertyName("memsY")]
    public int? MemsY { get; set; }
    [JsonPropertyName("memsZ")]
    public int? MemsZ { get; set; }
    [JsonPropertyName("recordedAt")]
    public DateTime RecordedAt { get; set; }
    [JsonPropertyName("cachedAt")]
    public DateTime CachedAt { get; set; }
}

public class RedisCacheService : IRedisCacheService, IDisposable
{
    private readonly ILogger<RedisCacheService> _logger;
    private readonly ConnectionMultiplexer? _redis;
    private readonly IDatabase? _db;
    private readonly bool _isConnected;
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RedisCacheService(IConfiguration configuration, ILogger<RedisCacheService> logger)
    {
        _logger = logger;
        
        var connectionString = configuration["Redis:ConnectionString"];
        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogWarning("Redis:ConnectionString not configured, Redis cache disabled");
            _isConnected = false;
            return;
        }

        try
        {
            _redis = ConnectionMultiplexer.Connect(connectionString);
            _db = _redis.GetDatabase();
            _isConnected = true;
            _logger.LogInformation("Connected to Redis at {ConnectionString}", connectionString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Redis");
            _isConnected = false;
        }
    }

    public async Task<VehiclePositionCache?> GetPositionAsync(string deviceUid)
    {
        if (!_isConnected || _db == null) return null;

        try
        {
            var key = $"vehicle:position:{deviceUid}";
            var value = await _db.StringGetAsync(key);
            
            if (value.IsNullOrEmpty) return null;

            return JsonSerializer.Deserialize<VehiclePositionCache>(value!, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get position from Redis for device {DeviceUid}", deviceUid);
            return null;
        }
    }

    public async Task<List<VehiclePositionCache>> GetAllPositionsForCompanyAsync(int companyId)
    {
        if (!_isConnected || _db == null || _redis == null) 
            return new List<VehiclePositionCache>();

        var positions = new List<VehiclePositionCache>();

        try
        {
            // Use company device index (populated by Rust ingest) instead of KEYS pattern scan
            var devicesKey = $"company:{companyId}:devices";
            var deviceUids = await _db.SetMembersAsync(devicesKey);

            if (deviceUids.Length == 0)
                return positions;

            // MGET: single round-trip to fetch all positions
            var positionKeys = deviceUids
                .Where(uid => !uid.IsNullOrEmpty)
                .Select(uid => (RedisKey)$"vehicle:position:{uid}")
                .ToArray();

            var values = await _db.StringGetAsync(positionKeys);

            foreach (var value in values)
            {
                if (value.IsNullOrEmpty) continue;

                var position = JsonSerializer.Deserialize<VehiclePositionCache>(value!, _jsonOptions);

                if (position != null)
                {
                    positions.Add(position);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all positions from Redis for company {CompanyId}", companyId);
        }

        return positions;
    }

    public async Task SubscribeToUpdatesAsync(int companyId, Action<VehiclePositionCache> onUpdate)
    {
        if (!_isConnected || _redis == null) return;

        try
        {
            var subscriber = _redis.GetSubscriber();
            var channel = new RedisChannel($"vehicle:updates:{companyId}", RedisChannel.PatternMode.Literal);
            
            await subscriber.SubscribeAsync(channel, (ch, message) =>
            {
                if (message.IsNullOrEmpty) return;

                try
                {
                    var position = JsonSerializer.Deserialize<VehiclePositionCache>(message!, _jsonOptions);

                    if (position != null)
                    {
                        onUpdate(position);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize Redis pub/sub message");
                }
            });

            _logger.LogInformation("Subscribed to Redis channel vehicle:updates:{CompanyId}", companyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to Redis updates for company {CompanyId}", companyId);
        }
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
