namespace ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

/// <summary>
/// Recovers a dead-lettered message by requeueing its original inbox row as pending so the normal
/// inbox drain re-applies the effect, and marking the dead-letter record resolved. The reset and the
/// resolution are committed atomically. Reprocessing an already-resolved (or already-applied) message
/// is a safe no-op.
/// </summary>
public interface IInboxDeadLetterReprocessor
{
    Task<ReprocessDeadLetterResult> ReprocessAsync(
        Guid deadLetterId,
        string requestedBy,
        CancellationToken cancellationToken = default);
}
