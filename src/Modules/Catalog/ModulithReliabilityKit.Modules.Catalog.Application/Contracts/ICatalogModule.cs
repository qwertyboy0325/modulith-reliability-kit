using ModulithReliabilityKit.BuildingBlocks.Application.Commands;
using ModulithReliabilityKit.BuildingBlocks.Application.Queries;

namespace ModulithReliabilityKit.Modules.Catalog.Application.Contracts;

/// <summary>
/// Catalog facade contract. API and other adapters talk to the module only through this boundary.
/// </summary>
public interface ICatalogModule
{
    Task<TResult> ExecuteCommandAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    Task ExecuteCommandAsync(ICommand command, CancellationToken cancellationToken = default);

    Task<TResult> ExecuteQueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}
