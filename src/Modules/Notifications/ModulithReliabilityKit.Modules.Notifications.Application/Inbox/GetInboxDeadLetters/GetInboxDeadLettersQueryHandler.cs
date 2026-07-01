using ModulithReliabilityKit.BuildingBlocks.Application.Queries;

namespace ModulithReliabilityKit.Modules.Notifications.Application.Inbox.GetInboxDeadLetters;

public sealed class GetInboxDeadLettersQueryHandler
    : IQueryHandler<GetInboxDeadLettersQuery, IReadOnlyCollection<InboxDeadLetterDto>>
{
    private readonly IInboxDeadLetterReadStore _readStore;

    public GetInboxDeadLettersQueryHandler(IInboxDeadLetterReadStore readStore)
    {
        _readStore = readStore;
    }

    public Task<IReadOnlyCollection<InboxDeadLetterDto>> Handle(
        GetInboxDeadLettersQuery request,
        CancellationToken cancellationToken)
        => _readStore.GetAsync(request.IncludeResolved, cancellationToken);
}
