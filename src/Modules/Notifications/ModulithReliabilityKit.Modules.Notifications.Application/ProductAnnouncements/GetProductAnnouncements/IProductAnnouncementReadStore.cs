namespace ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements.GetProductAnnouncements;

public interface IProductAnnouncementReadStore
{
    Task<IReadOnlyCollection<ProductAnnouncementDto>> GetAllAsync(CancellationToken cancellationToken = default);
}
