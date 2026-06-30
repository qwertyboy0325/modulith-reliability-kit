using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements.GetProductAnnouncements;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.ProductAnnouncements;

internal sealed class ProductAnnouncementReadStore : IProductAnnouncementReadStore
{
    private readonly NotificationsContext _context;

    public ProductAnnouncementReadStore(NotificationsContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyCollection<ProductAnnouncementDto>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.ProductAnnouncements
            .AsNoTracking()
            .OrderByDescending(x => x.AnnouncedOnUtc)
            .Select(x => new ProductAnnouncementDto(x.ProductId, x.Name, x.Price, x.Currency, x.AnnouncedOnUtc))
            .ToListAsync(cancellationToken);
}
