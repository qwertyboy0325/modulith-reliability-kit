using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.Inbox;

/// <summary>
/// Recovers a dead-lettered message. The reset of the original inbox row and the resolution of the
/// dead-letter record are staged on the same <see cref="NotificationsContext"/> and committed by a
/// single <c>SaveChanges</c>, so recovery is atomic: the message is never left both dead-lettered and
/// requeued. The actual re-application happens later, on the normal inbox drain, which stays idempotent.
/// </summary>
internal sealed class InboxDeadLetterReprocessor : IInboxDeadLetterReprocessor
{
    private const string ResolvedByReprocess = "reprocessed";
    private const string ResolvedAlreadyApplied = "resolved";

    private readonly NotificationsContext _context;

    public InboxDeadLetterReprocessor(NotificationsContext context)
    {
        _context = context;
    }

    public async Task<ReprocessDeadLetterResult> ReprocessAsync(
        Guid deadLetterId,
        string requestedBy,
        CancellationToken cancellationToken = default)
    {
        var deadLetter = await _context.InboxDeadLetters
            .FirstOrDefaultAsync(x => x.Id == deadLetterId, cancellationToken);

        if (deadLetter is null)
        {
            return new ReprocessDeadLetterResult(ReprocessDeadLetterOutcome.NotFound, deadLetterId, null);
        }

        if (deadLetter.ResolvedOnUtc is not null)
        {
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
