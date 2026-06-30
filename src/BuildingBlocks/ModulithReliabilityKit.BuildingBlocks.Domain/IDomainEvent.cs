namespace ModulithReliabilityKit.BuildingBlocks.Domain;

public interface IDomainEvent
{
    Guid Id { get; }

    DateTime OccurredOnUtc { get; }
}
