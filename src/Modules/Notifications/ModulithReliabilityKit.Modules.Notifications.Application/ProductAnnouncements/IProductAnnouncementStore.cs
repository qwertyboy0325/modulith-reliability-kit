namespace ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements;

/// <summary>
/// Records that a product was announced to this module. Implementations MUST be idempotent on
/// <paramref name="productId"/> so that an at-least-once inbox redelivery does not duplicate the effect.
/// </summary>
public interface IProductAnnouncementStore
{
    Task RecordAsync(
        Guid productId,
        string name,
        decimal price,
        string currency,
        DateTime occurredOnUtc,
        CancellationToken cancellationToken = default);
}
