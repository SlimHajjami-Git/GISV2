using System.Text;
using System.Text.Json;
using MediatR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using GisAPI.Application.Features.Gps.Commands.BroadcastPosition;

namespace GisAPI.Services;

/// <summary>
/// Background service that consumes GPS telemetry events from RabbitMQ
/// and broadcasts them via SignalR for real-time updates using CQRS pattern
/// </summary>
public class GpsTelemetryConsumer : BackgroundService
{
    private readonly ILogger<GpsTelemetryConsumer> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private IConnection? _connection;
    private IChannel? _channel;

    public GpsTelemetryConsumer(
        ILogger<GpsTelemetryConsumer> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rabbitHost = _configuration["RabbitMQ:Host"];
        if (string.IsNullOrEmpty(rabbitHost))
        {
            _logger.LogWarning("RabbitMQ:Host not configured, GPS telemetry consumer disabled");
            return;
        }

        await Task.Delay(5000, stoppingToken); // Wait for RabbitMQ to be ready

        try
        {
            await ConnectToRabbitMQ(stoppingToken);
            await ConsumeMessages(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPS Telemetry Consumer failed");
        }
    }

    private async Task ConnectToRabbitMQ(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _configuration["RabbitMQ:Host"] ?? "localhost",
            Port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672"),
            UserName = _configuration["RabbitMQ:Username"] ?? "guest",
            Password = _configuration["RabbitMQ:Password"] ?? "guest"
        };

        var retryCount = 0;
        const int maxRetries = 10;

        while (!stoppingToken.IsCancellationRequested && retryCount < maxRetries)
        {
            try
            {
                _connection = await factory.CreateConnectionAsync(stoppingToken);
                _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

                var exchange = _configuration["RabbitMQ:Exchange"] ?? "telemetry.raw";
                var queue = _configuration["RabbitMQ:Queue"] ?? "gps.telemetry.dotnet";
                var routingKey = _configuration["RabbitMQ:RoutingKey"] ?? "hh";

                await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
                await _channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
                await _channel.QueueBindAsync(queue, exchange, routingKey, cancellationToken: stoppingToken);

                _logger.LogInformation("Connected to RabbitMQ, consuming from queue: {Queue}", queue);
                return;
            }
            catch (Exception ex)
            {
                retryCount++;
                _logger.LogWarning(ex, "Failed to connect to RabbitMQ (attempt {Attempt}/{MaxRetries})", retryCount, maxRetries);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        throw new Exception("Failed to connect to RabbitMQ after maximum retries");
    }

    private async Task ConsumeMessages(CancellationToken stoppingToken)
    {
        if (_channel == null) return;

        var queue = _configuration["RabbitMQ:Queue"] ?? "gps.telemetry.dotnet";
        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (model, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                
                await ProcessTelemetryMessage(message);
                
                await _channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing telemetry message");
                await _channel.BasicNackAsync(ea.DeliveryTag, false, true);
            }
        };

        await _channel.BasicConsumeAsync(queue, autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        // Keep the service running
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task ProcessTelemetryMessage(string message)
    {
        try
        {
            var telemetry = JsonSerializer.Deserialize<TelemetryMessage>(message, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (telemetry == null)
            {
                _logger.LogWarning("Failed to deserialize telemetry message");
                return;
            }

            _logger.LogDebug("Processing telemetry for device: {DeviceUid}", telemetry.DeviceUid);

            // Use MediatR to handle the broadcast with CQRS pattern
            // This includes adaptive interval logic based on vehicle state
            using var scope = _serviceProvider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var command = new BroadcastPositionCommand(
                DeviceUid: telemetry.DeviceUid,
                Latitude: telemetry.Latitude,
                Longitude: telemetry.Longitude,
                SpeedKph: telemetry.SpeedKph,
                CourseDeg: telemetry.CourseDeg,
                IgnitionOn: telemetry.IgnitionOn,
                RecordedAt: telemetry.RecordedAt,
                AlertType: telemetry.AlertType
            );

            var result = await mediator.Send(command);

            if (result.Broadcasted)
            {
                _logger.LogDebug("Position broadcasted for device: {DeviceUid}, Vehicle: {VehicleId}",
                    telemetry.DeviceUid, result.VehicleId);
            }
            else
            {
                _logger.LogDebug("Position skipped for device: {DeviceUid}, Reason: {Reason}",
                    telemetry.DeviceUid, result.SkipReason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing telemetry message: {Message}", message);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel != null)
        {
            await _channel.CloseAsync(cancellationToken);
        }
        if (_connection != null)
        {
            await _connection.CloseAsync(cancellationToken);
        }
        await base.StopAsync(cancellationToken);
    }
}

public class TelemetryMessage
{
    [System.Text.Json.Serialization.JsonPropertyName("device_uid")]
    public string DeviceUid { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("protocol")]
    public string Protocol { get; set; } = string.Empty;
    
    [System.Text.Json.Serialization.JsonPropertyName("latitude")]
    public double Latitude { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("longitude")]
    public double Longitude { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("speed_kph")]
    public double? SpeedKph { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("heading_deg")]
    public double? CourseDeg { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("ignition_on")]
    public bool? IgnitionOn { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("recorded_at")]
    public DateTime RecordedAt { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("alert_type")]
    public string? AlertType { get; set; }
}
