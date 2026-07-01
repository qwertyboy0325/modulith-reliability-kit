using ModulithReliabilityKit.BuildingBlocks.Application.Queries;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Application.Contracts;

/// <summary>
/// Notifications facade contract. The module reacts to integration events; its synchronous surface is
/// querying what it has recorded plus a small operational action to recover dead-lettered messages.
/// API talks to the module only through this boundary.
/// </summary>
public interface INotificationsModule
{
    Task<TResult> ExecuteQueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requeues a dead-lettered inbox message for another drain and marks the dead-letter resolved.
    /// Idempotent: reprocessing an already-resolved or already-applied message is a no-op.
    /// </summary>
    Task<ReprocessDeadLetterResult> ReprocessDeadLetterAsync(
        Guid deadLetterId,
        string requestedBy,
        CancellationToken cancellationToken = default);
}
