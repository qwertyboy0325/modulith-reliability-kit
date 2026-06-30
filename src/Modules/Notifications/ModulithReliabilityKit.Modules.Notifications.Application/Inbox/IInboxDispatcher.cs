using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

/// <summary>
/// Resolves a stored <see cref="InboxMessage"/> back to its integration-event type and applies the
/// module's business effect. Owns the foreign integration-event contract knowledge so the transport
/// (inbox processor) stays generic and the Infrastructure layer never references another module's
/// IntegrationEvents assembly directly.
/// </summary>
public interface IInboxDispatcher
{
    Task DispatchAsync(InboxMessage message, CancellationToken cancellationToken = default);
}
