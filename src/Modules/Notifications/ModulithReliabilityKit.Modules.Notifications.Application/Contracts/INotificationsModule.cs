using ModulithReliabilityKit.BuildingBlocks.Application.Queries;

namespace ModulithReliabilityKit.Modules.Notifications.Application.Contracts;

/// <summary>
/// Notifications facade contract. The module reacts to integration events; its only synchronous
/// surface is querying what it has recorded. API talks to the module only through this boundary.
/// </summary>
public interface INotificationsModule
{
    Task<TResult> ExecuteQueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}
