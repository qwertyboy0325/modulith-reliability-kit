using Modulith.BuildingBlocks.Application.Events;

namespace Modulith.Modules.Catalog.IntegrationEvents;

/// <summary>
/// Published after a product is created and its transaction has committed.
/// This is the module's public contract: other modules reference ONLY this assembly,
/// never Catalog.Domain/Application/Infrastructure.
/// Durable: it is persisted to the outbox and dispatched by the outbox processor.
/// </summary>
[IntegrationEventReliability(IntegrationEventReliability.Durable)]
public sealed class ProductCreatedIntegrationEvent : IntegrationEvent
{
    public ProductCreatedIntegrationEvent(
        Guid id,
        DateTime occurredOnUtc,
        Guid productId,
        string name,
        decimal price,
        string currency)
        : base(id, occurredOnUtc)
    {
        ProductId = productId;
        Name = name;
        Price = price;
        Currency = currency;
    }

    public Guid ProductId { get; }

    public string Name { get; }

    public decimal Price { get; }

    public string Currency { get; }
}
