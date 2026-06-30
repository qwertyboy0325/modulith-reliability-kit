using Microsoft.EntityFrameworkCore;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;

public interface IDomainEventsDispatcher<TContext> : IDomainEventsDispatcher
    where TContext : DbContext;
