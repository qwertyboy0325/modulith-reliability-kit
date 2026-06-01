using Microsoft.EntityFrameworkCore;
using Modulith.BuildingBlocks.Domain;

namespace Modulith.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public sealed class DomainEventsAccessor<TContext> : IDomainEventsAccessor<TContext>
    where TContext : DbContext
{
    private readonly TContext _context;

    public DomainEventsAccessor(TContext context)
    {
        _context = context;
    }

    public IReadOnlyCollection<IDomainEvent> GetAllDomainEvents()
    {
        return _context.ChangeTracker
            .Entries<Entity>()
            .Where(x => x.Entity.DomainEvents.Count > 0)
            .SelectMany(x => x.Entity.DomainEvents)
            .ToList();
    }

    public void ClearAllDomainEvents()
    {
        var domainEntities = _context.ChangeTracker
            .Entries<Entity>()
            .Where(x => x.Entity.DomainEvents.Count > 0)
            .ToList();

        foreach (var domainEntity in domainEntities)
        {
            domainEntity.Entity.ClearDomainEvents();
        }
    }
}
