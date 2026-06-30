using ModulithReliabilityKit.BuildingBlocks.Domain;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public interface IDomainEventsAccessor
{
    IReadOnlyCollection<IDomainEvent> GetAllDomainEvents();

    void ClearAllDomainEvents();
}
