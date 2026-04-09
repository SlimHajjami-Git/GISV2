using System.Text;
using System.Text.Json;
using MediatR;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using GisAPI.Application.Features.Notifications.Events;

namespace GisAPI.Services;

public class DeviceEventConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ILogger<DeviceEventConsumer> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private IConnection? _connection;
    private IChannel? _channel;

    public DeviceEventConsumer(
        ILogger<DeviceEventConsumer> logger,
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
            _logger.LogWarning("RabbitMQ:Host not configured, device event consumer disabled");
            return;
        }

        await Task.Delay(2000, stoppingToken);

        try
        {
            await ConnectToRabbitMQ(stoppingToken);
            await ConsumeMessages(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device Event Consumer failed");
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
                const string queue = "gps.device-events.dotnet";
                const string routingKey = "device.event";

                await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
                await _channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
                await _channel.QueueBindAsync(queue, exchange, routingKey, cancellationToken: stoppingToken);
                await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 5, global: false, cancellationToken: stoppingToken);

                _logger.LogInformation("Device Event Consumer connected, queue: {Queue}", queue);
                return;
            }
            catch (Exception ex)
            {
                retryCount++;
                _logger.LogWarning(ex, "Device Event Consumer: failed to connect (attempt {Attempt}/{Max})", retryCount, maxRetries);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        throw new Exception("Device Event Consumer: failed to connect to RabbitMQ after max retries");
    }

    private async Task ConsumeMessages(CancellationToken stoppingToken)
    {
        if (_channel == null) return;

        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (_, ea) =>
        {
            try
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);
                await ProcessDeviceEvent(json);
                await _channel.BasicAckAsync(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process device event message");
                await _channel.BasicNackAsync(ea.DeliveryTag, false, false);
            }
        };

        await _channel.BasicConsumeAsync("gps.device-events.dotnet", autoAck: false, consumer: consumer, cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken);
        }
    }

    private async Task ProcessDeviceEvent(string json)
    {
        var msg = JsonSerializer.Deserialize<DeviceEventMessage>(json, _jsonOptions);
        if (msg == null)
        {
            _logger.LogWarning("Could not deserialize device event message");
            return;
        }

        _logger.LogInformation(
            "Device event: device_id={DeviceId} type={EventType} offline={OfflineSecs}s",
            msg.DeviceId, msg.EventType, msg.OfflineDurationSecs);

        // Look up vehicle name
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<GisAPI.Application.Common.Interfaces.IGisDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        string? vehicleName = null;
        if (msg.VehicleId.HasValue)
        {
            vehicleName = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(
                    context.Vehicles
                        .Where(v => v.Id == msg.VehicleId.Value)
                        .Select(v => v.Name ?? v.Plate));
        }

        await mediator.Publish(new PowerCutNotificationEvent(
            CompanyId: msg.CompanyId,
            VehicleId: msg.VehicleId,
            VehicleName: vehicleName,
            OfflineDurationSecs: msg.OfflineDurationSecs ?? 0,
            LastKnownLat: msg.LastKnownLat,
            LastKnownLon: msg.LastKnownLon,
            EventType: msg.EventType ?? "restart",
            EventAt: msg.EventAt ?? DateTime.UtcNow
        ));
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}

public class DeviceEventMessage
{
    public string? MessageType { get; set; }
    public int DeviceId { get; set; }
    public int? VehicleId { get; set; }
    public int CompanyId { get; set; }
    public string? EventType { get; set; }
    public DateTime? EventAt { get; set; }
    public int? OfflineDurationSecs { get; set; }
    public double? LastKnownLat { get; set; }
    public double? LastKnownLon { get; set; }
    public bool WasMoving { get; set; }
}
