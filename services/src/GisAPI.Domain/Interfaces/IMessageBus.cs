namespace GisAPI.Domain.Interfaces;

public interface IMessageBus
{
    Task PublishAsync<T>(T message, string? routingKey = null, CancellationToken ct = default) where T : class;
    Task PublishAsync<T>(T message, string exchange, string routingKey, CancellationToken ct = default) where T : class;
}

public interface IEventHandler<in TEvent> where TEvent : class
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}


