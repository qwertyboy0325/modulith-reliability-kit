using ModulithReliabilityKit.BuildingBlocks.Domain;
using ModulithReliabilityKit.Modules.Catalog.Domain.Products.Rules;

namespace ModulithReliabilityKit.Modules.Catalog.Domain.Products;

public sealed class Money : ValueObject
{
    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Of(decimal amount, string currency)
    {
        CheckRule(new MoneyMustBeNonNegativeRule(amount));
        CheckRule(new CurrencyMustBeIso4217Rule(currency));

        return new Money(amount, currency.ToUpperInvariant());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }

    // Mirrors Entity.CheckRule so value objects can self-validate without an Entity base.
    private static void CheckRule(IBusinessRule rule)
    {
        if (rule.IsBroken())
        {
            throw new BusinessRuleValidationException(rule);
        }
    }
}
