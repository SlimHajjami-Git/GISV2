using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GisAPI.Application.Features.Gps.Commands.BroadcastPosition;
using GisAPI.Domain.Events;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace GisAPI.Infrastructure.Messaging;

public class RabbitMqConsumerService : BackgroundService
{
    private readonly RabbitMqSettings _settings;
    private readonly ILogger<RabbitMqConsumerService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqConsumerService(
        IOptions<RabbitMqSettings> settings,
        ILogger<RabbitMqConsumerService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _settings = settings.Value;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RabbitMQ Consumer Service starting...");

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _settings.HostName,
                Port = _settings.Port,
                UserName = _settings.UserName,
                Password = _settings.Password,
                VirtualHost = _settings.VirtualHost
            };

            _connection = await factory.CreateConnectionAsync(stoppingToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            // Declare Dead Letter Exchange + Queue
            await _channel.ExchangeDeclareAsync(
                exchange: _settings.DeadLetterExchange,
                type: ExchangeType.Fanout,
                durable: true,
                autoDelete: false,
                cancellationToken: stoppingToken);

            await _channel.QueueDeclareAsync(
                queue: _settings.DeadLetterQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: stoppingToken);

            await _channel.QueueBindAsync(
                queue: _settings.DeadLetterQueue,
                exchange: _settings.DeadLetterExchange,
                routingKey: "",
                cancellationToken: stoppingToken);

            // Declare main exchanges
            await _channel.ExchangeDeclareAsync(
                exchange: _settings.GpsExchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: stoppingToken);

            await _channel.ExchangeDeclareAsync(
                exchange: _settings.AlertsExchange,
                type: ExchangeType.Fanout,
                durable: true,
                autoDelete: false,
                cancellationToken: stoppingToken);

            // Queue args: route rejected messages to DLX
            var queueArgs = new Dictionary<string, object?>
            {
                { "x-dead-letter-exchange", _settings.DeadLetterExchange }
            };

            // Declare queues with DLX and bind to exchanges
            await _channel.QueueDeclareAsync(
                queue: _settings.GpsPositionsQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: queueArgs,
                cancellationToken: stoppingToken);

            await _channel.QueueBindAsync(
                queue: _settings.GpsPositionsQueue,
                exchange: _settings.GpsExchange,
                routingKey: "gps.#",
                cancellationToken: stoppingToken);

            await _channel.QueueDeclareAsync(
                queue: _settings.AlertsQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: queueArgs,
                cancellationToken: stoppingToken);

            await _channel.QueueBindAsync(
                queue: _settings.AlertsQueue,
                exchange: _settings.AlertsExchange,
                routingKey: "",
                cancellationToken: stoppingToken);

            // Set prefetch to avoid overwhelming the consumer
            await _channel.BasicQosAsync(0, 50, false, stoppingToken);

            // Set up consumer for GPS positions
            var gpsConsumer = new AsyncEventingBasicConsumer(_channel);
            gpsConsumer.ReceivedAsync += async (_, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    
                    _logger.LogDebug("Received GPS message: {Message}", message);
                    
                    // Deserialize Rust GPS message (snake_case JSON)
                    var options = new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                        PropertyNameCaseInsensitive = true 
                    };
                    var gpsMessage = JsonSerializer.Deserialize<RabbitMqGpsMessage>(message, options);
                    
                    if (gpsMessage != null)
                    {
                        await ProcessGpsMessageAsync(gpsMessage, stoppingToken);
                    }

                    await _channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                }
                catch (Exception ex)
                {
                    var retryCount = GetRetryCount(ea.BasicProperties);
                    if (retryCount >= _settings.MaxRetries)
                    {
                        _logger.LogError(ex,
                            "GPS message failed after {MaxRetries} retries, sending to DLQ. DeliveryTag={DeliveryTag}",
                            _settings.MaxRetries, ea.DeliveryTag);
                        // Reject without requeue → goes to DLX → DLQ
                        await _channel.BasicNackAsync(ea.DeliveryTag, false, false, stoppingToken);
                    }
                    else
                    {
                        _logger.LogWarning(ex,
                            "GPS message processing failed (attempt {Attempt}/{MaxRetries}), requeuing. DeliveryTag={DeliveryTag}",
                            retryCount + 1, _settings.MaxRetries, ea.DeliveryTag);
                        await _channel.BasicNackAsync(ea.DeliveryTag, false, true, stoppingToken);
                    }
                }
            };

            await _channel.BasicConsumeAsync(
                queue: _settings.GpsPositionsQueue,
                autoAck: false,
                consumer: gpsConsumer,
                cancellationToken: stoppingToken);

            _logger.LogInformation("RabbitMQ Consumer Service started successfully");

            // Keep running until cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("RabbitMQ Consumer Service stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RabbitMQ Consumer Service");
        }
    }

    private async Task ProcessGpsMessageAsync(RabbitMqGpsMessage msg, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var command = new BroadcastPositionCommand(
                DeviceUid: msg.DeviceUid,
                Latitude: msg.Latitude,
                Longitude: msg.Longitude,
                SpeedKph: msg.SpeedKph,
                CourseDeg: msg.HeadingDeg,
                IgnitionOn: msg.IgnitionOn,
                RecordedAt: msg.RecordedAt
            );

            var result = await mediator.Send(command, ct);

            if (result.Broadcasted)
            {
                _logger.LogDebug(
                    "📡 Broadcasted position for device {DeviceUid} → Vehicle {VehicleId}",
                    msg.DeviceUid, result.VehicleId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting position for device {DeviceUid}", msg.DeviceUid);
        }
    }

    private static int GetRetryCount(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers == null || !properties.Headers.TryGetValue("x-death", out var deathObj))
            return 0;

        if (deathObj is List<object> deathList && deathList.Count > 0)
        {
            if (deathList[0] is Dictionary<string, object> deathEntry &&
                deathEntry.TryGetValue("count", out var countObj))
            {
                return Convert.ToInt32(countObj);
            }
        }

        return 0;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("RabbitMQ Consumer Service stopping...");

        if (_channel != null)
        {
            await _channel.CloseAsync(cancellationToken);
            await _channel.DisposeAsync();
        }
        if (_connection != null)
        {
            await _connection.CloseAsync(cancellationToken);
            await _connection.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}

/// <summary>
/// DTO matching the Rust GPS ingest JSON format (snake_case)
/// </summary>
public class RabbitMqGpsMessage
{
    public string DeviceUid { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
    public DateTime IngestedAt { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double SpeedKph { get; set; }
    public double HeadingDeg { get; set; }
    public bool IgnitionOn { get; set; }
    public int FuelRaw { get; set; }
    public double PowerVoltage { get; set; }
    public string? RawPayload { get; set; }
}
