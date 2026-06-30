using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.ModulePersistence;

public sealed class ModuleUnitOfWork<TContext> : IModuleUnitOfWork<TContext>
    where TContext : DbContext
{
    private readonly TContext _context;
    private readonly IDomainEventsDispatcher<TContext> _domainEventsDispatcher;

    public ModuleUnitOfWork(
        TContext context,
        IDomainEventsDispatcher<TContext> domainEventsDispatcher)
    {
        _context = context;
        _domainEventsDispatcher = domainEventsDispatcher;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _domainEventsDispatcher.DispatchEventsAsync(cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
