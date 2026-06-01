namespace Modulith.BuildingBlocks.Application.Events;

public interface IEventsBus : IDisposable
{
    Task Publish<TIntegrationEvent>(TIntegrationEvent @event, CancellationToken cancellationToken = default)
        where TIntegrationEvent : IntegrationEvent;

    void Subscribe<TIntegrationEvent>(IIntegrationEventHandler<TIntegrationEvent> handler)
        where TIntegrationEvent : IntegrationEvent;
}
