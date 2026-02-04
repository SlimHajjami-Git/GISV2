using System.Text.Json;
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
    public string DeviceUid { get; set; } = string.Empty;
    public int? VehicleId { get; set; }
    public int CompanyId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double SpeedKph { get; set; }
    public double HeadingDeg { get; set; }
    public bool IgnitionOn { get; set; }
    public bool IsValid { get; set; }
    public int FuelRaw { get; set; }
    public int PowerVoltage { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime CachedAt { get; set; }
}

public class RedisCacheService : IRedisCacheService, IDisposable
{
    private readonly ILogger<RedisCacheService> _logger;
    private readonly ConnectionMultiplexer? _redis;
    private readonly IDatabase? _db;
    private readonly bool _isConnected;

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

            return JsonSerializer.Deserialize<VehiclePositionCache>(value!, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
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
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: "vehicle:position:*").ToArray();

            foreach (var key in keys)
            {
                var value = await _db.StringGetAsync(key);
                if (value.IsNullOrEmpty) continue;

                var position = JsonSerializer.Deserialize<VehiclePositionCache>(value!, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (position != null && position.CompanyId == companyId)
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
                    var position = JsonSerializer.Deserialize<VehiclePositionCache>(message!, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

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
