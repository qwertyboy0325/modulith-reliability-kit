namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public interface IDomainEventsDispatcher
{
    Task DispatchEventsAsync(CancellationToken cancellationToken = default);
}
