namespace ModulithReliabilityKit.BuildingBlocks.Application.Events;

public abstract class IntegrationEvent
{
    protected IntegrationEvent(Guid id, DateTime occurredOnUtc)
    {
        Id = id;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid Id { get; }

    public DateTime OccurredOnUtc { get; }

    public virtual int Version => 1;
}
