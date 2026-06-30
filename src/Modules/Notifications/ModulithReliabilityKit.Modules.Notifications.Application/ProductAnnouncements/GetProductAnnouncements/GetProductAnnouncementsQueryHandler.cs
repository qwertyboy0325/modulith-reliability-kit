using ModulithReliabilityKit.BuildingBlocks.Application.Queries;

namespace ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements.GetProductAnnouncements;

public sealed class GetProductAnnouncementsQueryHandler
    : IQueryHandler<GetProductAnnouncementsQuery, IReadOnlyCollection<ProductAnnouncementDto>>
{
    private readonly IProductAnnouncementReadStore _readStore;

    public GetProductAnnouncementsQueryHandler(IProductAnnouncementReadStore readStore)
    {
        _readStore = readStore;
    }

    public Task<IReadOnlyCollection<ProductAnnouncementDto>> Handle(
        GetProductAnnouncementsQuery request,
        CancellationToken cancellationToken)
        => _readStore.GetAllAsync(cancellationToken);
}
