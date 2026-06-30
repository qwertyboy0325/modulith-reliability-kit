using ModulithReliabilityKit.BuildingBlocks.Domain;

namespace ModulithReliabilityKit.BuildingBlocks.Application.Events;

public abstract class DomainNotificationBase<TDomainEvent> : IDomainEventNotification<TDomainEvent>
    where TDomainEvent : IDomainEvent
{
    protected DomainNotificationBase(TDomainEvent domainEvent, Guid id)
    {
        DomainEvent = domainEvent;
        Id = id;
    }

    public TDomainEvent DomainEvent { get; }

    public Guid Id { get; }
}
