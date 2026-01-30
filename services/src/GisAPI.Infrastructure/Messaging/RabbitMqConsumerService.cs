using System.Text;
using System.Text.Json;
using GisAPI.Domain.Events;
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
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqConsumerService(
        IOptions<RabbitMqSettings> settings,
        ILogger<RabbitMqConsumerService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
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

            // Declare exchanges first
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

            // Declare queues and bind to exchanges
            await _channel.QueueDeclareAsync(
                queue: _settings.GpsPositionsQueue,
                durable: true,
                exclusive: false,
                autoDelete: false,
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
                cancellationToken: stoppingToken);

            await _channel.QueueBindAsync(
                queue: _settings.AlertsQueue,
                exchange: _settings.AlertsExchange,
                routingKey: "",
                cancellationToken: stoppingToken);

            // Set up consumer for GPS positions
            var gpsConsumer = new AsyncEventingBasicConsumer(_channel);
            gpsConsumer.ReceivedAsync += async (_, ea) =>
            {
                try
                {
                    var body = ea.Body.ToArray();
                    var message = Encoding.UTF8.GetString(body);
                    
                    _logger.LogDebug("Received GPS message: {Message}", message);
                    
                    // Process GPS position - could trigger geofence checks, alerts, etc.
                    var position = JsonSerializer.Deserialize<GpsPositionReceivedEvent>(message, 
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    
                    if (position != null)
                    {
                        await ProcessGpsPositionAsync(position, stoppingToken);
                    }

                    await _channel.BasicAckAsync(ea.DeliveryTag, false, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing GPS message");
                    await _channel.BasicNackAsync(ea.DeliveryTag, false, true, stoppingToken);
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

    private async Task ProcessGpsPositionAsync(GpsPositionReceivedEvent position, CancellationToken ct)
    {
        // This is where you would:
        // 1. Check geofence boundaries
        // 2. Check speed limits
        // 3. Generate alerts if needed
        // 4. Update vehicle last known position
        // 5. Broadcast to SignalR for real-time updates
        
        _logger.LogInformation(
            "Processing GPS position for device {DeviceUid}: ({Lat}, {Lng}) @ {Speed} km/h",
            position.DeviceUid, position.Latitude, position.Longitude, position.Speed);

        await Task.CompletedTask;
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


