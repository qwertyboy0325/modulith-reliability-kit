namespace ModulithReliabilityKit.BuildingBlocks.Application.Events;

public enum IntegrationEventReliability
{
    Durable = 1,
    BestEffort = 2
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventReliabilityAttribute : Attribute
{
    public IntegrationEventReliabilityAttribute(IntegrationEventReliability reliability)
    {
        Reliability = reliability;
    }

    public IntegrationEventReliability Reliability { get; }
}
