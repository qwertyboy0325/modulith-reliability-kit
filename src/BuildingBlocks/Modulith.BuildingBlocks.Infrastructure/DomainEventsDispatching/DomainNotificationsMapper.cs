using Modulith.BuildingBlocks.Application.Events;
using Modulith.BuildingBlocks.Domain;

namespace Modulith.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public sealed class DomainNotificationsMapper : IDomainNotificationsMapper
{
    private readonly Dictionary<Type, Func<IDomainEvent, IDomainEventNotification>> _factories = new();

    public void Register<TDomainEvent>(Func<TDomainEvent, IDomainEventNotification> factory)
        where TDomainEvent : IDomainEvent
    {
        _factories[typeof(TDomainEvent)] = @event => factory((TDomainEvent)@event);
    }

    public bool TryMap(IDomainEvent domainEvent, out IDomainEventNotification notification)
    {
        if (_factories.TryGetValue(domainEvent.GetType(), out var factory))
        {
            notification = factory(domainEvent);
            return true;
        }

        notification = null!;
        return false;
    }
}
