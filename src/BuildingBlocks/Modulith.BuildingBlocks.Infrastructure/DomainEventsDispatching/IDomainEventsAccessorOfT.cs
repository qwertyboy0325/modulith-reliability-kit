using Microsoft.EntityFrameworkCore;

namespace Modulith.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public interface IDomainEventsAccessor<TContext> : IDomainEventsAccessor
    where TContext : DbContext;
