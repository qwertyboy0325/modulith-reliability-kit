namespace ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

/// <summary>
/// Read-side port over the inbox dead-letter table. Kept separate from the write path so the query
/// handler depends only on a projection, never on the <c>DbContext</c>.
/// </summary>
public interface IInboxDeadLetterReadStore
{
    Task<IReadOnlyCollection<InboxDeadLetterDto>> GetAsync(
        bool includeResolved,
        CancellationToken cancellationToken = default);
}
