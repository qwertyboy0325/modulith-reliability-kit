using MediatR;
using ModulithReliabilityKit.BuildingBlocks.Application.Queries;
using ModulithReliabilityKit.Modules.Notifications.Application.Contracts;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure;

internal sealed class NotificationsModuleFacade : INotificationsModule
{
    private readonly ISender _sender;
    private readonly IInboxDeadLetterReprocessor _reprocessor;

    public NotificationsModuleFacade(ISender sender, IInboxDeadLetterReprocessor reprocessor)
    {
        _sender = sender;
        _reprocessor = reprocessor;
    }

    public Task<TResult> ExecuteQueryAsync<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default)
        => _sender.Send(query, cancellationToken);

    // Operational recovery action; runs its own atomic transaction rather than the MediatR/UoW
    // command pipeline, matching how the module's inbox processing manages its own transactions.
    public Task<ReprocessDeadLetterResult> ReprocessDeadLetterAsync(
        Guid deadLetterId,
        string requestedBy,
        CancellationToken cancellationToken = default)
        => _reprocessor.ReprocessAsync(deadLetterId, requestedBy, cancellationToken);
}
