using Modulith.BuildingBlocks.Domain;
using Modulith.Modules.Catalog.Domain.Products.Events;
using Modulith.Modules.Catalog.Domain.Products.Rules;

namespace Modulith.Modules.Catalog.Domain.Products;

public sealed class Product : Entity, IAggregateRoot
{
    private Product()
    {
        // EF Core materialization constructor.
    }

    private Product(ProductId id, string name, Money price)
    {
        Id = id;
        Name = name;
        Price = price;
        IsActive = true;
        CreatedOnUtc = DateTime.UtcNow;

        AddDomainEvent(new ProductCreatedDomainEvent(id, name, price.Amount, price.Currency));
    }

    public ProductId Id { get; private set; } = null!;

    public string Name { get; private set; } = string.Empty;

    public Money Price { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public static Product Create(string name, Money price)
    {
        CheckRule(new ProductNameMustNotBeEmptyRule(name));

        return new Product(new ProductId(Guid.NewGuid()), name.Trim(), price);
    }

    public void Rename(string newName)
    {
        CheckRule(new ProductNameMustNotBeEmptyRule(newName));

        var trimmed = newName.Trim();
        if (trimmed == Name)
        {
            return;
        }

        var oldName = Name;
        Name = trimmed;
        AddDomainEvent(new ProductRenamedDomainEvent(Id, oldName, trimmed));
    }

    public void ChangePrice(Money newPrice)
    {
        Price = newPrice;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
