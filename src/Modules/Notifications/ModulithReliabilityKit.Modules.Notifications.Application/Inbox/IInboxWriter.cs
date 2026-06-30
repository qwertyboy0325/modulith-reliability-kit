using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

/// <summary>
/// Persists an incoming integration event into the module inbox.
/// Implementations MUST be idempotent on <c>(logical_id, occurred_on_utc)</c> so that
/// at-least-once delivery from the bus does not create duplicate inbox rows.
/// </summary>
public interface IInboxWriter
{
    Task IngestAsync(InboxMessage message, CancellationToken cancellationToken = default);
}
