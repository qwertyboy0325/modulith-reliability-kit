using Microsoft.Extensions.Logging;
using Modulith.BuildingBlocks.Application.Outbox;

namespace Modulith.BuildingBlocks.Infrastructure.Processing;

public abstract class OutboxProcessorBase
{
    private readonly ILogger _logger;

    protected OutboxProcessorBase(ILogger logger)
    {
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        var batch = await GetPendingBatchAsync(cancellationToken);
        foreach (var message in batch)
        {
            try
            {
                await ProcessMessageAsync(message, cancellationToken);
                await MarkAsProcessedAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process outbox message {OutboxMessageId}", message.Id);
            }
        }
    }

    protected abstract Task<IReadOnlyCollection<OutboxMessage>> GetPendingBatchAsync(CancellationToken cancellationToken);

    protected abstract Task ProcessMessageAsync(OutboxMessage message, CancellationToken cancellationToken);

    protected abstract Task MarkAsProcessedAsync(OutboxMessage message, CancellationToken cancellationToken);
}
