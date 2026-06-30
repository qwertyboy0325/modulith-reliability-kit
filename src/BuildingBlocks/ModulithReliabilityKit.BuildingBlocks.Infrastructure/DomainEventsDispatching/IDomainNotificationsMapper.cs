using ModulithReliabilityKit.BuildingBlocks.Application.Events;
using ModulithReliabilityKit.BuildingBlocks.Domain;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public interface IDomainNotificationsMapper
{
    bool TryMap(IDomainEvent domainEvent, out IDomainEventNotification notification);

    void Register<TDomainEvent>(Func<TDomainEvent, IDomainEventNotification> factory)
        where TDomainEvent : IDomainEvent;
}
