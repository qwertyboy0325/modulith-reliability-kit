using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public sealed class DomainEventsDispatcher<TContext> : IDomainEventsDispatcher<TContext>
    where TContext : DbContext
{
    private readonly IDomainEventsAccessor<TContext> _domainEventsAccessor;
    private readonly IDomainNotificationsMapper _domainNotificationsMapper;
    private readonly IMediator _mediator;

    public DomainEventsDispatcher(
        IDomainEventsAccessor<TContext> domainEventsAccessor,
        IDomainNotificationsMapper domainNotificationsMapper,
        IMediator mediator)
    {
        _domainEventsAccessor = domainEventsAccessor;
        _domainNotificationsMapper = domainNotificationsMapper;
        _mediator = mediator;
    }

    public async Task DispatchEventsAsync(CancellationToken cancellationToken = default)
    {
        var domainEvents = _domainEventsAccessor.GetAllDomainEvents();
        _domainEventsAccessor.ClearAllDomainEvents();

        foreach (var domainEvent in domainEvents)
        {
            if (_domainNotificationsMapper.TryMap(domainEvent, out var notification))
            {
                await _mediator.Publish(notification, cancellationToken);
            }
        }
    }
}
