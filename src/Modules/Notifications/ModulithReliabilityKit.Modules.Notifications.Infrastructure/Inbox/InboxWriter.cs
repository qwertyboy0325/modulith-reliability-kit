using Microsoft.EntityFrameworkCore;
using Npgsql;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.Inbox;

/// <summary>
/// Idempotent inbox ingest. The unique index on <c>(logical_id, occurred_on_utc)</c> is the source of
/// truth; a duplicate delivery either short-circuits on the existence check or is rejected by the
/// index and swallowed here, so re-delivery never produces a second inbox row.
/// </summary>
internal sealed class InboxWriter : IInboxWriter
{
    private const string UniqueViolation = "23505";

    private readonly NotificationsContext _context;

    public InboxWriter(NotificationsContext context)
    {
        _context = context;
    }

    public async Task IngestAsync(InboxMessage message, CancellationToken cancellationToken = default)
    {
        var alreadyIngested = await _context.InboxMessages
            .AsNoTracking()
            .AnyAsync(
                x => x.LogicalId == message.LogicalId && x.OccurredOnUtc == message.OccurredOnUtc,
                cancellationToken);

        if (alreadyIngested)
        {
            return;
        }

        _context.InboxMessages.Add(message);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // Concurrent delivery won the race and inserted the same (logical_id, occurred_on) first.
            // The inbox is already in the desired state, so treat this as a successful idempotent ingest.
            _context.Entry(message).State = EntityState.Detached;
        }
    }
}
