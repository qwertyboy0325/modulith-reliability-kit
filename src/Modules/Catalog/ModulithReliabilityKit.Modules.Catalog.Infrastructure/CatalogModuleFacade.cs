using MediatR;
using ModulithReliabilityKit.BuildingBlocks.Application.Commands;
using ModulithReliabilityKit.BuildingBlocks.Application.Queries;
using ModulithReliabilityKit.Modules.Catalog.Application.Contracts;

namespace ModulithReliabilityKit.Modules.Catalog.Infrastructure;

internal sealed class CatalogModuleFacade : ICatalogModule
{
    private readonly ISender _sender;

    public CatalogModuleFacade(ISender sender)
    {
        _sender = sender;
    }

    public Task<TResult> ExecuteCommandAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
        => _sender.Send(command, cancellationToken);

    public Task ExecuteCommandAsync(ICommand command, CancellationToken cancellationToken = default)
        => _sender.Send(command, cancellationToken);

    public Task<TResult> ExecuteQueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        => _sender.Send(query, cancellationToken);
}
