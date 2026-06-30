namespace ModulithReliabilityKit.BuildingBlocks.Application.Events;

public interface IIntegrationEventHandler
{
}

public interface IIntegrationEventHandler<in TIntegrationEvent> : IIntegrationEventHandler
    where TIntegrationEvent : IntegrationEvent
{
    Task Handle(TIntegrationEvent @event, CancellationToken cancellationToken = default);
}
