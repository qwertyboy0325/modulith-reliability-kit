using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.Processing;

/// <summary>
/// Durable consumer side of the reliability story. Drains the inbox one message at a time:
/// <list type="number">
///   <item>The business effect and the "processed" mark commit in a single transaction (no partial state).</item>
///   <item>On failure the transaction is rolled back and the change tracker cleared, so a failed attempt
///   leaves no trace except the retry bookkeeping.</item>
///   <item>Retry/dead-letter is decided by <see cref="InboxRetryPolicy"/>; after the bounded attempts the
///   message is copied to the dead-letter table and removed from the pending set.</item>
/// </list>
/// </summary>
internal sealed class NotificationsInboxProcessor
{
    private const int BatchSize = 50;
    private const int MaxErrorLength = 4000;

    private static readonly string[] PendingStatuses = ["pending", "retrying"];

    private readonly NotificationsContext _context;
    private readonly IInboxDispatcher _dispatcher;
    private readonly InboxRetryPolicy _retryPolicy;
    private readonly ILogger<NotificationsInboxProcessor> _logger;

    public NotificationsInboxProcessor(
        NotificationsContext context,
        IInboxDispatcher dispatcher,
        InboxRetryPolicy retryPolicy,
        ILogger<NotificationsInboxProcessor> logger)
    {
        _context = context;
        _dispatcher = dispatcher;
        _retryPolicy = retryPolicy;
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var dueIds = await _context.InboxMessages
            .AsNoTracking()
            .Where(x => PendingStatuses.Contains(x.Status)
                && (x.NextRetryOnUtc == null || x.NextRetryOnUtc <= now))
            .OrderBy(x => x.OccurredOnUtc)
            .Take(BatchSize)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in dueIds)
        {
            await ProcessOneAsync(id, cancellationToken);
        }
    }

    private async Task ProcessOneAsync(long id, CancellationToken cancellationToken)
    {
        Exception failure;

        await using (var transaction = await _context.Database.BeginTransactionAsync(cancellationToken))
        {
            var message = await _context.InboxMessages
                .FirstOrDefaultAsync(x => x.Id == id && x.ProcessedOnUtc == null, cancellationToken);

            if (message is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            try
            {
                await _dispatcher.DispatchAsync(message, cancellationToken);

                message.ProcessedOnUtc = DateTime.UtcNow;
                message.Status = "processed";
                message.LastError = null;
                message.NextRetryOnUtc = null;

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                failure = ex;
            }
        }

        // Discard any business changes staged by the failed attempt before recording retry state.
        _context.ChangeTracker.Clear();
        await RecordFailureAsync(id, failure, cancellationToken);
    }

    private async Task RecordFailureAsync(long id, Exception failure, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var message = await _context.InboxMessages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (message is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var decision = _retryPolicy.OnFailure(message.RetryCount);
        var error = Truncate(failure.ToString());

        message.RetryCount = decision.RetryCount;
        message.LastError = error;
        message.LastRetryOnUtc = DateTime.UtcNow;

        if (decision.ShouldDeadLetter)
        {
            message.Status = "dead_letter";
            message.NextRetryOnUtc = null;

            _context.InboxDeadLetters.Add(new InboxDeadLetterMessage(
                message.LogicalId,
                message.Type,
                message.Payload,
                message.OccurredOnUtc,
                message.RetryCount,
                error));

            _logger.LogError(
                failure,
                "Inbox message {InboxMessageId} ({LogicalId}) dead-lettered after {RetryCount} attempts",
                message.Id,
                message.LogicalId,
                message.RetryCount);
        }
        else
        {
            message.Status = "retrying";
            message.NextRetryOnUtc = DateTime.UtcNow + decision.RetryDelay;

            _logger.LogWarning(
                failure,
                "Inbox message {InboxMessageId} ({LogicalId}) failed; retry {RetryCount} scheduled at {NextRetryOnUtc:o}",
                message.Id,
                message.LogicalId,
                message.RetryCount,
                message.NextRetryOnUtc);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string Truncate(string value)
        => value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
}
