using ModulithReliabilityKit.BuildingBlocks.Domain;

namespace ModulithReliabilityKit.Modules.Catalog.Domain.Products.Rules;

public sealed class CurrencyMustBeIso4217Rule : IBusinessRule
{
    private readonly string? _currency;

    public CurrencyMustBeIso4217Rule(string? currency)
    {
        _currency = currency;
    }

    public string Message => "Currency must be a 3-letter ISO 4217 code.";

    public bool IsBroken() => string.IsNullOrWhiteSpace(_currency) || _currency.Trim().Length != 3;
}
