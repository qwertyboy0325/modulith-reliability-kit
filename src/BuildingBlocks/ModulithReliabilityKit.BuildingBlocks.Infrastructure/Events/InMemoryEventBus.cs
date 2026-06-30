using ModulithReliabilityKit.BuildingBlocks.Application.Events;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Events;

public sealed class InMemoryEventBus : IEventsBus
{
    private readonly Dictionary<Type, List<IIntegrationEventHandler>> _handlers = [];
    private readonly object _lock = new();

    public void Subscribe<TIntegrationEvent>(IIntegrationEventHandler<TIntegrationEvent> handler)
        where TIntegrationEvent : IntegrationEvent
    {
        lock (_lock)
        {
            var eventType = typeof(TIntegrationEvent);
            if (!_handlers.TryGetValue(eventType, out var handlers))
            {
                handlers = [];
                _handlers[eventType] = handlers;
            }

            handlers.Add(handler);
        }
    }

    public async Task Publish<TIntegrationEvent>(
        TIntegrationEvent @event,
        CancellationToken cancellationToken = default)
        where TIntegrationEvent : IntegrationEvent
    {
        List<IIntegrationEventHandler> handlers;
        lock (_lock)
        {
            if (!_handlers.TryGetValue(@event.GetType(), out handlers!))
            {
                return;
            }

            handlers = handlers.ToList();
        }

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (handler is IIntegrationEventHandler<TIntegrationEvent> typedHandler)
            {
                await typedHandler.Handle(@event, cancellationToken);
            }
        }
    }

    public void Dispose()
    {
    }
}
