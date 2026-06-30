using Microsoft.EntityFrameworkCore;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public interface IDomainEventsAccessor<TContext> : IDomainEventsAccessor
    where TContext : DbContext;
