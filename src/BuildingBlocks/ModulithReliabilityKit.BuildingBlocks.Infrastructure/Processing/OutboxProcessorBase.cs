using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModulithReliabilityKit.BuildingBlocks.Application.Outbox;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Diagnostics;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Processing;

public abstract class OutboxProcessorBase
{
    private readonly ILogger _logger;
    private readonly ReliabilityMetrics _metrics;
    private readonly string _moduleName;

    protected OutboxProcessorBase(ILogger logger, ReliabilityMetrics metrics, string moduleName)
    {
        _logger = logger;
        _metrics = metrics;
        _moduleName = moduleName;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        var batch = await GetPendingBatchAsync(cancellationToken);
        foreach (var message in batch)
        {
            using var activity = ReliabilityInstrumentation.ActivitySource.StartActivity("outbox.publish");
            activity?.SetTag("module", _moduleName);
            activity?.SetTag("messaging.message_type", message.Type);

            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                await ProcessMessageAsync(message, cancellationToken);
                await MarkAsProcessedAsync(message, cancellationToken);
                _metrics.OutboxPublished(_moduleName);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _metrics.OutboxPublishFailed(_moduleName);
                _logger.LogError(ex, "Failed to process outbox message {OutboxMessageId}", message.Id);
            }
            finally
            {
                _metrics.RecordOutboxProcessDuration(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, _moduleName);
            }
        }
    }

    protected abstract Task<IReadOnlyCollection<OutboxMessage>> GetPendingBatchAsync(CancellationToken cancellationToken);

    protected abstract Task ProcessMessageAsync(OutboxMessage message, CancellationToken cancellationToken);

    protected abstract Task MarkAsProcessedAsync(OutboxMessage message, CancellationToken cancellationToken);
}
