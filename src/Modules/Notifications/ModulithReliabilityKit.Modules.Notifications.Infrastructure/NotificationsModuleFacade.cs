using MediatR;
using ModulithReliabilityKit.BuildingBlocks.Application.Queries;
using ModulithReliabilityKit.Modules.Notifications.Application.Contracts;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure;

internal sealed class NotificationsModuleFacade : INotificationsModule
{
    private readonly ISender _sender;

    public NotificationsModuleFacade(ISender sender)
    {
        _sender = sender;
    }

    public Task<TResult> ExecuteQueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        => _sender.Send(query, cancellationToken);
}
