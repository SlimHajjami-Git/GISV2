using System.Text.Json;
using MediatR;
using StackExchange.Redis;
using GisAPI.Application.Features.Gps.Commands.BroadcastPosition;

namespace GisAPI.Services;

/// <summary>
/// Background service that subscribes to Redis PubSub for real-time GPS updates.
/// This provides LOWER LATENCY than RabbitMQ by receiving updates directly from the Rust ingestion service.
/// Works in parallel with RabbitMQ consumer for redundancy.
/// </summary>
public class RedisPubSubConsumer : BackgroundService
{
    private readonly ILogger<RedisPubSubConsumer> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private IConnectionMultiplexer? _redis;
    private ISubscriber? _subscriber;

    public RedisPubSubConsumer(
        ILogger<RedisPubSubConsumer> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var redisConnection = _configuration["Redis:ConnectionString"];
        if (string.IsNullOrEmpty(redisConnection))
        {
            _logger.LogWarning("Redis:ConnectionString not configured, Redis PubSub consumer disabled");
            return;
        }

        await Task.Delay(2000, stoppingToken); // Wait for Redis to be ready

        try
        {
            await ConnectToRedis(stoppingToken);
            await SubscribeToUpdates(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis PubSub Consumer failed");
        }
    }

    private async Task ConnectToRedis(CancellationToken stoppingToken)
    {
        var connectionString = _configuration["Redis:ConnectionString"] ?? "localhost:6379";
        var retryCount = 0;
        const int maxRetries = 10;

        while (!stoppingToken.IsCancellationRequested && retryCount < maxRetries)
        {
            try
            {
                _redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
                _subscriber = _redis.GetSubscriber();
                _logger.LogInformation("🚀 Connected to Redis PubSub at {ConnectionString}", connectionString);
                return;
            }
            catch (Exception ex)
            {
                retryCount++;
                _logger.LogWarning(ex, "Failed to connect to Redis (attempt {Attempt}/{MaxRetries})", retryCount, maxRetries);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }

        throw new Exception("Failed to connect to Redis after maximum retries");
    }

    private async Task SubscribeToUpdates(CancellationToken stoppingToken)
    {
        if (_subscriber == null) return;

        // Subscribe to all company update channels using pattern
        var pattern = new RedisChannel("vehicle:updates:*", RedisChannel.PatternMode.Pattern);
        
        await _subscriber.SubscribeAsync(pattern, async (channel, message) =>
        {
            try
            {
                if (message.IsNullOrEmpty) return;
                
                var messageStr = message.ToString();
                await ProcessRedisMessage(messageStr);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Redis PubSub message");
            }
        });

        _logger.LogInformation("📡 Subscribed to Redis PubSub pattern: vehicle:updates:*");

        // Keep the service running
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task ProcessRedisMessage(string message)
    {
        try
        {
            var position = JsonSerializer.Deserialize<RedisPositionMessage>(message, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (position == null)
            {
                _logger.LogWarning("Failed to deserialize Redis position message");
                return;
            }

            // Calculate latency (Redis path is faster than RabbitMQ)
            var now = DateTime.UtcNow;
            var cachedAt = DateTime.Parse(position.CachedAt);
            var latencyMs = (now - cachedAt).TotalMilliseconds;

            _logger.LogDebug(
                "⚡ Redis PubSub: Device={DeviceUid}, Latency={Latency:F0}ms",
                position.DeviceUid, latencyMs);

            // Use MediatR to broadcast via SignalR
            using var scope = _serviceProvider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var recordedAt = DateTime.Parse(position.RecordedAt);
            
            var command = new BroadcastPositionCommand(
                DeviceUid: position.DeviceUid,
                Latitude: position.Latitude,
                Longitude: position.Longitude,
                SpeedKph: position.SpeedKph,
                CourseDeg: position.HeadingDeg,
                IgnitionOn: position.IgnitionOn,
                RecordedAt: recordedAt,
                AlertType: null
            );

            var result = await mediator.Send(command);

            if (result.Broadcasted)
            {
                _logger.LogDebug("⚡ Position broadcasted via Redis PubSub for device: {DeviceUid}", position.DeviceUid);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Redis position message");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscriber != null)
        {
            await _subscriber.UnsubscribeAllAsync();
        }
        if (_redis != null)
        {
            await _redis.CloseAsync();
        }
        await base.StopAsync(cancellationToken);
    }
}

public class RedisPositionMessage
{
    [System.Text.Json.Serialization.JsonPropertyName("device_uid")]
    public string DeviceUid { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("vehicle_id")]
    public int? VehicleId { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("company_id")]
    public int CompanyId { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("latitude")]
    public double Latitude { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("longitude")]
    public double Longitude { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("speed_kph")]
    public double SpeedKph { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("heading_deg")]
    public double HeadingDeg { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("ignition_on")]
    public bool IgnitionOn { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("recorded_at")]
    public string RecordedAt { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("cached_at")]
    public string CachedAt { get; set; } = string.Empty;
}
