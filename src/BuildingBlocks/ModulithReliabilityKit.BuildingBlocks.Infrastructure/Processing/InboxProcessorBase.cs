using Microsoft.Extensions.Logging;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Processing;

public abstract class InboxProcessorBase
{
    private readonly ILogger _logger;

    protected InboxProcessorBase(ILogger logger)
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
                _logger.LogError(ex, "Failed to process inbox message {InboxMessageId}", message.Id);
                await HandleFailureAsync(message, ex, cancellationToken);
            }
        }
    }

    protected abstract Task<IReadOnlyCollection<InboxMessage>> GetPendingBatchAsync(CancellationToken cancellationToken);

    protected abstract Task ProcessMessageAsync(InboxMessage message, CancellationToken cancellationToken);

    protected abstract Task MarkAsProcessedAsync(InboxMessage message, CancellationToken cancellationToken);

    protected abstract Task HandleFailureAsync(InboxMessage message, Exception exception, CancellationToken cancellationToken);
}
