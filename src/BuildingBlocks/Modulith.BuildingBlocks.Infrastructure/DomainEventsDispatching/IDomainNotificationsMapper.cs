using Modulith.BuildingBlocks.Application.Events;
using Modulith.BuildingBlocks.Domain;

namespace Modulith.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public interface IDomainNotificationsMapper
{
    bool TryMap(IDomainEvent domainEvent, out IDomainEventNotification notification);

    void Register<TDomainEvent>(Func<TDomainEvent, IDomainEventNotification> factory)
        where TDomainEvent : IDomainEvent;
}
