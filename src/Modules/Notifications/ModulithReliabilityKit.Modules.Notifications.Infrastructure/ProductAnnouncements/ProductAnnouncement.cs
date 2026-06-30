namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.ProductAnnouncements;

/// <summary>
/// The Notifications module's local projection of "a product was created". Keyed by the source
/// product id so recording it twice (at-least-once redelivery) is a no-op.
/// </summary>
internal sealed class ProductAnnouncement
{
    public ProductAnnouncement(Guid productId, string name, decimal price, string currency, DateTime announcedOnUtc)
    {
        ProductId = productId;
        Name = name;
        Price = price;
        Currency = currency;
        AnnouncedOnUtc = announcedOnUtc;
    }

    private ProductAnnouncement()
    {
    }

    public Guid ProductId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public decimal Price { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public DateTime AnnouncedOnUtc { get; private set; }
}
