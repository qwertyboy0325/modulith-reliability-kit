using Modulith.BuildingBlocks.Domain;

namespace Modulith.Modules.Catalog.Domain.Products.Events;

public sealed class ProductCreatedDomainEvent : DomainEventBase
{
    public ProductCreatedDomainEvent(ProductId productId, string name, decimal price, string currency)
    {
        ProductId = productId;
        Name = name;
        Price = price;
        Currency = currency;
    }

    public ProductId ProductId { get; }

    public string Name { get; }

    public decimal Price { get; }

    public string Currency { get; }
}
