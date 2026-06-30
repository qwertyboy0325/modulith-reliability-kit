using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.BuildingBlocks.Application;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly DbContext _dbContext;
    private readonly IDomainEventsDispatcher _domainEventsDispatcher;

    public UnitOfWork(
        DbContext dbContext,
        IDomainEventsDispatcher domainEventsDispatcher)
    {
        _dbContext = dbContext;
        _domainEventsDispatcher = domainEventsDispatcher;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _domainEventsDispatcher.DispatchEventsAsync(cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
