using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Diagnostics;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.Inbox;

/// <summary>
/// Recovers a dead-lettered message. The dead-letter row is claimed with a blocking <c>FOR UPDATE</c>
/// lock, then the reset of the original inbox row and the resolution of the dead-letter record are
/// committed in the same transaction, so recovery is atomic <b>and</b> concurrency-safe: two operators
/// reprocessing the same dead-letter serialize on the row lock, so it is requeued exactly once (the
/// second request observes it already resolved and is a no-op). The actual re-application happens later,
/// on the normal inbox drain, which stays idempotent.
/// </summary>
internal sealed class InboxDeadLetterReprocessor : IInboxDeadLetterReprocessor
{
    private const string ResolvedByReprocess = "reprocessed";
    private const string ResolvedAlreadyApplied = "resolved";

    private const string ModuleName = "notifications";

    private readonly NotificationsContext _context;
    private readonly ReliabilityMetrics _metrics;

    public InboxDeadLetterReprocessor(NotificationsContext context, ReliabilityMetrics metrics)
    {
        _context = context;
        _metrics = metrics;
    }

    public async Task<ReprocessDeadLetterResult> ReprocessAsync(
        Guid deadLetterId,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Claim the dead-letter row with a blocking Postgres row lock so two concurrent operator
        // reprocess requests for the same dead-letter serialize: the first requeues and resolves it;
        // the second blocks on the lock, then re-reads it as resolved and becomes a no-op. Kept as
        // verbatim SQL (ToList, no LINQ composition) so EF does not wrap it in a sub-select, which
        // Postgres disallows for locking clauses. Schema/table are compile-time constants; {0} is a
        // FromSqlRaw placeholder (parameterized), not C# interpolation.
        var claimed = await _context.InboxDeadLetters
            .FromSqlRaw(
                $"SELECT * FROM \"{NotificationsContext.Schema}\".\"inbox_dead_letters\" "
                + "WHERE \"id\" = {0} "
                + "FOR UPDATE",
                deadLetterId)
            .ToListAsync(cancellationToken);

        var deadLetter = claimed.Count > 0 ? claimed[0] : null;

        if (deadLetter is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new ReprocessDeadLetterResult(ReprocessDeadLetterOutcome.NotFound, deadLetterId, null);
        }

        if (deadLetter.ResolvedOnUtc is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new ReprocessDeadLetterResult(
                ReprocessDeadLetterOutcome.AlreadyResolved,
                deadLetterId,
                deadLetter.OriginalMessageId);
        }

        var message = await _context.InboxMessages.FirstOrDefaultAsync(
            x => x.LogicalId == deadLetter.OriginalMessageId && x.OccurredOnUtc == deadLetter.OccurredOnUtc,
            cancellationToken);

        // The effect already landed (e.g. a prior reprocess succeeded but the record was not closed):
        // just resolve the dead-letter, never re-run the effect.
        if (message is { ProcessedOnUtc: not null })
        {
            Resolve(deadLetter, requestedBy, ResolvedAlreadyApplied, "Effect already applied; dead-letter closed.");
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ReprocessDeadLetterResult(
                ReprocessDeadLetterOutcome.AlreadyResolved,
                deadLetterId,
                deadLetter.OriginalMessageId);
        }

        if (message is null)
        {
            // The original inbox row was purged; rebuild it from the durable dead-letter copy.
            message = new InboxMessage(
                deadLetter.OriginalMessageId,
                deadLetter.OccurredOnUtc,
                deadLetter.Type,
                deadLetter.Payload);
            _context.InboxMessages.Add(message);
        }

        message.Status = "pending";
        message.RetryCount = 0;
        message.NextRetryOnUtc = null;
        message.LastError = null;
        message.LastRetryOnUtc = null;
        message.ProcessedOnUtc = null;

        Resolve(deadLetter, requestedBy, ResolvedByReprocess, "Requeued for reprocessing.");

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _metrics.DeadLetterReprocessed(ModuleName);

        return new ReprocessDeadLetterResult(
            ReprocessDeadLetterOutcome.Requeued,
            deadLetterId,
            deadLetter.OriginalMessageId);
    }

    private static void Resolve(InboxDeadLetterMessage deadLetter, string requestedBy, string status, string notes)
    {
        deadLetter.ResolutionStatus = status;
        deadLetter.ResolvedOnUtc = DateTime.UtcNow;
        deadLetter.ResolvedBy = requestedBy;
        deadLetter.ResolutionNotes = notes;
    }
}
