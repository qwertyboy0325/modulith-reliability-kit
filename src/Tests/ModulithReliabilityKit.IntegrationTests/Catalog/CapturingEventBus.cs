using ModulithReliabilityKit.BuildingBlocks.Application.Events;

namespace ModulithReliabilityKit.IntegrationTests.Catalog;

/// <summary>
/// Test double for <see cref="IEventsBus"/> that records every published event instead of
/// dispatching it, so the outbox processor's publish behavior can be asserted directly.
/// </summary>
internal sealed class CapturingEventBus : IEventsBus
{
    public List<IntegrationEvent> Published { get; } = [];

    public Task Publish<TIntegrationEvent>(TIntegrationEvent @event, CancellationToken cancellationToken = default)
        where TIntegrationEvent : IntegrationEvent
    {
        Published.Add(@event);
        return Task.CompletedTask;
    }

    public void Subscribe<TIntegrationEvent>(IIntegrationEventHandler<TIntegrationEvent> handler)
        where TIntegrationEvent : IntegrationEvent
    {
    }

    public void Dispose()
    {
    }
}
