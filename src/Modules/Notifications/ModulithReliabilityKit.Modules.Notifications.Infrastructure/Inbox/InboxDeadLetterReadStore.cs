using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.Inbox;

internal sealed class InboxDeadLetterReadStore : IInboxDeadLetterReadStore
{
    private readonly NotificationsContext _context;

    public InboxDeadLetterReadStore(NotificationsContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyCollection<InboxDeadLetterDto>> GetAsync(
        bool includeResolved,
        CancellationToken cancellationToken = default)
    {
        var query = _context.InboxDeadLetters.AsNoTracking();

        if (!includeResolved)
        {
            query = query.Where(x => x.ResolvedOnUtc == null);
        }

        return await query
            .OrderByDescending(x => x.MovedToDeadLetterOnUtc)
            .Select(x => new InboxDeadLetterDto(
                x.Id,
                x.OriginalMessageId,
                x.Type,
                x.OccurredOnUtc,
                x.RetryCount,
                x.LastError,
                x.MovedToDeadLetterOnUtc,
                x.ResolutionStatus,
                x.ResolvedOnUtc,
                x.ResolvedBy))
            .ToListAsync(cancellationToken);
    }
}
