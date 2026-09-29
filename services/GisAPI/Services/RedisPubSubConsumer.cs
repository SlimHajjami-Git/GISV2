using System.Text.Json;
using System.Threading.Channels;
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
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ILogger<RedisPubSubConsumer> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private IConnectionMultiplexer? _redis;
    private ISubscriber? _subscriber;
    // Bounded channel decouples message receipt (non-blocking) from processing (parallel workers)
    private readonly Channel<string> _messageChannel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });
    private const int WorkerCount = 4;

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

        // Start worker pool BEFORE subscribing so no messages are lost
        var workers = new Task[WorkerCount];
        for (int i = 0; i < WorkerCount; i++)
        {
            var workerId = i;
            workers[i] = Task.Run(async () =>
            {
                await foreach (var msg in _messageChannel.Reader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        await ProcessRedisMessage(msg);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Redis PubSub worker {WorkerId} error", workerId);
                    }
                }
            }, stoppingToken);
        }
        _logger.LogInformation("📡 Started {WorkerCount} Redis PubSub processing workers", WorkerCount);

        // Subscribe — callback only enqueues to channel (non-blocking, <1ms)
        var pattern = new RedisChannel("vehicle:updates:*", RedisChannel.PatternMode.Pattern);
        await _subscriber.SubscribeAsync(pattern, (channel, message) =>
        {
            if (message.IsNullOrEmpty) return;
            // TryWrite is non-blocking; if channel is full, oldest message is dropped
            if (!_messageChannel.Writer.TryWrite(message.ToString()))
            {
                _logger.LogWarning("Redis PubSub channel full — dropping oldest message");
            }
        });

        _logger.LogInformation("📡 Subscribed to Redis PubSub pattern: vehicle:updates:*");

        // Keep the service running until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }

        _messageChannel.Writer.Complete();
        await Task.WhenAll(workers);
    }

    private async Task ProcessRedisMessage(string message)
    {
        try
        {
            var position = JsonSerializer.Deserialize<RedisPositionMessage>(message, _jsonOptions);

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
                AlertType: null,
                FuelRaw: position.FuelRaw,
                BatteryVoltage: position.BatteryVoltage,
                BatteryPercent: position.BatteryPercent,
                TemperatureC: position.TemperatureC
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
    [System.Text.Json.Serialization.JsonPropertyName("deviceUid")]
    public string DeviceUid { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("vehicleId")]
    public int? VehicleId { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("companyId")]
    public int CompanyId { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("latitude")]
    public double Latitude { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("longitude")]
    public double Longitude { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("speedKph")]
    public double SpeedKph { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("headingDeg")]
    public double HeadingDeg { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("ignitionOn")]
    public bool IgnitionOn { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("recordedAt")]
    public string RecordedAt { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("cachedAt")]
    public string CachedAt { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("fuelRaw")]
    public int? FuelRaw { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("batteryVoltage")]
    public double? BatteryVoltage { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("batteryPercent")]
    public int? BatteryPercent { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("temperatureC")]
    public int? TemperatureC { get; set; }
}
