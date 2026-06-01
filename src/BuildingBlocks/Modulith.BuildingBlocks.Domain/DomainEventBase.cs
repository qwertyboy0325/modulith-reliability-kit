namespace Modulith.BuildingBlocks.Domain;

public abstract class DomainEventBase : IDomainEvent
{
    protected DomainEventBase()
    {
        Id = Guid.NewGuid();
        OccurredOnUtc = DateTime.UtcNow;
    }

    public Guid Id { get; }

    public DateTime OccurredOnUtc { get; }
}
