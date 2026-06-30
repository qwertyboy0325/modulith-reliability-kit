using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.ProductAnnouncements;

/// <summary>
/// Stages an idempotent product-announcement insert. It deliberately does NOT call
/// <c>SaveChanges</c>: the inbox processor owns the transaction so the business effect and the
/// inbox "processed" mark commit atomically.
/// </summary>
internal sealed class ProductAnnouncementStore : IProductAnnouncementStore
{
    private readonly NotificationsContext _context;

    public ProductAnnouncementStore(NotificationsContext context)
    {
        _context = context;
    }

    public async Task RecordAsync(
        Guid productId,
        string name,
        decimal price,
        string currency,
        DateTime occurredOnUtc,
        CancellationToken cancellationToken = default)
    {
        var alreadyRecorded = await _context.ProductAnnouncements
            .AsNoTracking()
            .AnyAsync(x => x.ProductId == productId, cancellationToken);

        if (alreadyRecorded)
        {
            return;
        }

        _context.ProductAnnouncements.Add(
            new ProductAnnouncement(productId, name, price, currency, occurredOnUtc));
    }
}
