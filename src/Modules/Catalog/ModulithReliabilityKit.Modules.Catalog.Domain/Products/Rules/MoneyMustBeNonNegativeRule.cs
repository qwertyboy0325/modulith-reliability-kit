using ModulithReliabilityKit.BuildingBlocks.Domain;

namespace ModulithReliabilityKit.Modules.Catalog.Domain.Products.Rules;

public sealed class MoneyMustBeNonNegativeRule : IBusinessRule
{
    private readonly decimal _amount;

    public MoneyMustBeNonNegativeRule(decimal amount)
    {
        _amount = amount;
    }

    public string Message => "Money amount cannot be negative.";

    public bool IsBroken() => _amount < 0m;
}
