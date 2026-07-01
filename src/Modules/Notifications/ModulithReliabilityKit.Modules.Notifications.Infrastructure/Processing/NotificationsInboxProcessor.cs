using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Diagnostics;
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

    // List (not array) so `.Contains` binds to the instance method and never the span-based
    // MemoryExtensions.Contains overload, which breaks EF Core's parameter evaluation on some SDKs.
    private static readonly List<string> PendingStatuses = ["pending", "retrying"];

    private const string ModuleName = "notifications";

    private readonly NotificationsContext _context;
    private readonly IInboxDispatcher _dispatcher;
    private readonly InboxRetryPolicy _retryPolicy;
    private readonly ReliabilityMetrics _metrics;
    private readonly ILogger<NotificationsInboxProcessor> _logger;

    public NotificationsInboxProcessor(
        NotificationsContext context,
        IInboxDispatcher dispatcher,
        InboxRetryPolicy retryPolicy,
        ReliabilityMetrics metrics,
        ILogger<NotificationsInboxProcessor> logger)
    {
        _context = context;
        _dispatcher = dispatcher;
        _retryPolicy = retryPolicy;
        _metrics = metrics;
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
        using var activity = ReliabilityInstrumentation.ActivitySource.StartActivity("inbox.process");
        activity?.SetTag("module", ModuleName);

        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await ProcessOneCoreAsync(id, activity, cancellationToken);
        }
        finally
        {
            _metrics.RecordInboxProcessDuration(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, ModuleName);
        }
    }

    private async Task ProcessOneCoreAsync(long id, Activity? activity, CancellationToken cancellationToken)
    {
        Exception failure;

        await using (var transaction = await _context.Database.BeginTransactionAsync(cancellationToken))
        {
            // Claim the row with a Postgres row lock so multiple processor instances (multi-instance
            // deployment) never apply the same message concurrently. FOR UPDATE SKIP LOCKED means a
            // second instance skips a row already claimed by the first instead of blocking on it or
            // double-dispatching. Kept as verbatim SQL (ToList, no LINQ composition) so EF does not
            // wrap it in a sub-select, which Postgres disallows for locking clauses.
            // Schema/table are compile-time constants (kept literal); only the id is a bound parameter
            // ({0} is a FromSqlRaw placeholder, not C# interpolation, so it is parameterized safely).
            var claimed = await _context.InboxMessages
                .FromSqlRaw(
                    $"SELECT * FROM \"{NotificationsContext.Schema}\".\"inbox_messages\" "
                    + "WHERE \"id\" = {0} AND \"processed_on_utc\" IS NULL "
                    + "FOR UPDATE SKIP LOCKED",
                    id)
                .ToListAsync(cancellationToken);

            var message = claimed.Count > 0 ? claimed[0] : null;

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
                _metrics.InboxProcessed(ModuleName);
                return;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
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

            _metrics.InboxDeadLettered(ModuleName);

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

            _metrics.InboxRetried(ModuleName);

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
