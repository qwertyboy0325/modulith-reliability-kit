using Modulith.BuildingBlocks.Application.Events;
using Modulith.Modules.Catalog.Domain.Products.Events;

namespace Modulith.Modules.Catalog.Application.Products.Events;

/// <summary>
/// MediatR-facing wrapper for <see cref="ProductCreatedDomainEvent"/>.
/// The domain stays MediatR-free; this adapter is what the dispatcher publishes.
/// </summary>
public sealed class ProductCreatedNotification : DomainNotificationBase<ProductCreatedDomainEvent>
{
    public ProductCreatedNotification(ProductCreatedDomainEvent domainEvent, Guid id)
        : base(domainEvent, id)
    {
    }
}
