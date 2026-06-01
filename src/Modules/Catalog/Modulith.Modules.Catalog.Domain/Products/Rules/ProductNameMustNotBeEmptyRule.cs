using Modulith.BuildingBlocks.Domain;

namespace Modulith.Modules.Catalog.Domain.Products.Rules;

public sealed class ProductNameMustNotBeEmptyRule : IBusinessRule
{
    private readonly string? _name;

    public ProductNameMustNotBeEmptyRule(string? name)
    {
        _name = name;
    }

    public string Message => "Product name must not be empty.";

    public bool IsBroken() => string.IsNullOrWhiteSpace(_name);
}
