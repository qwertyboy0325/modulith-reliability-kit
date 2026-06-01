using Modulith.BuildingBlocks.Domain;

namespace Modulith.Modules.Catalog.Domain.Products.Events;

public sealed class ProductRenamedDomainEvent : DomainEventBase
{
    public ProductRenamedDomainEvent(ProductId productId, string oldName, string newName)
    {
        ProductId = productId;
        OldName = oldName;
        NewName = newName;
    }

    public ProductId ProductId { get; }

    public string OldName { get; }

    public string NewName { get; }
}
