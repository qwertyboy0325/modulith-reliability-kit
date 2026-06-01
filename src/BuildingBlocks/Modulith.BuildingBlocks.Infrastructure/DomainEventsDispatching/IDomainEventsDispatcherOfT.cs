using Microsoft.EntityFrameworkCore;

namespace Modulith.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public interface IDomainEventsDispatcher<TContext> : IDomainEventsDispatcher
    where TContext : DbContext;
