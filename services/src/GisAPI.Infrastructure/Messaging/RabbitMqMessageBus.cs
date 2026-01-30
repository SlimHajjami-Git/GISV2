using System.Text;
using System.Text.Json;
using GisAPI.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace GisAPI.Infrastructure.Messaging;

public class RabbitMqMessageBus : IMessageBus, IAsyncDisposable
{
    private readonly RabbitMqSettings _settings;
    private readonly ILogger<RabbitMqMessageBus> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public RabbitMqMessageBus(IOptions<RabbitMqSettings> settings, ILogger<RabbitMqMessageBus> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var factory = new ConnectionFactory
            {
                HostName = _settings.HostName,
                Port = _settings.Port,
                UserName = _settings.UserName,
                Password = _settings.Password,
                VirtualHost = _settings.VirtualHost
            };

            _connection = await factory.CreateConnectionAsync(ct);
            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

            // Declare exchanges
            await _channel.ExchangeDeclareAsync(_settings.GpsExchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
            await _channel.ExchangeDeclareAsync(_settings.AlertsExchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);
            await _channel.ExchangeDeclareAsync(_settings.EventsExchange, ExchangeType.Topic, durable: true, cancellationToken: ct);

            _initialized = true;
            _logger.LogInformation("RabbitMQ connection initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize RabbitMQ connection");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task PublishAsync<T>(T message, string? routingKey = null, CancellationToken ct = default) where T : class
    {
        var exchange = GetExchangeForType<T>();
        var key = routingKey ?? GetRoutingKeyForType<T>();
        await PublishAsync(message, exchange, key, ct);
    }

    public async Task PublishAsync<T>(T message, string exchange, string routingKey, CancellationToken ct = default) where T : class
    {
        try
        {
            await EnsureInitializedAsync(ct);

            var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            var body = Encoding.UTF8.GetBytes(json);

            var properties = new BasicProperties
            {
                Persistent = true,
                ContentType = "application/json",
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                MessageId = Guid.NewGuid().ToString()
            };

            await _channel!.BasicPublishAsync(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: ct);

            _logger.LogDebug("Published message to {Exchange}/{RoutingKey}: {MessageType}", 
                exchange, routingKey, typeof(T).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message to RabbitMQ");
            // Don't throw - we don't want messaging failures to break the app
        }
    }

    private string GetExchangeForType<T>()
    {
        var typeName = typeof(T).Name;
        return typeName switch
        {
            var n when n.Contains("Gps") || n.Contains("Position") => _settings.GpsExchange,
            var n when n.Contains("Alert") => _settings.AlertsExchange,
            _ => _settings.EventsExchange
        };
    }

    private static string GetRoutingKeyForType<T>()
    {
        var typeName = typeof(T).Name;
        // Convert PascalCase to dot.separated.lowercase
        return string.Concat(typeName.Select((c, i) => 
            i > 0 && char.IsUpper(c) ? "." + char.ToLower(c) : char.ToLower(c).ToString()));
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel != null)
        {
            await _channel.CloseAsync();
            await _channel.DisposeAsync();
        }
        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
        _initLock.Dispose();
    }
}


