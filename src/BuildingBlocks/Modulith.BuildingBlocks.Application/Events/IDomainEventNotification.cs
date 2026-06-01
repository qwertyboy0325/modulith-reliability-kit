using MediatR;
using Modulith.BuildingBlocks.Domain;

namespace Modulith.BuildingBlocks.Application.Events;

public interface IDomainEventNotification : INotification
{
    Guid Id { get; }
}

public interface IDomainEventNotification<out TDomainEvent> : IDomainEventNotification
    where TDomainEvent : IDomainEvent
{
    TDomainEvent DomainEvent { get; }
}
