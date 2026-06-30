namespace ModulithReliabilityKit.BuildingBlocks.Application;

public sealed class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string entityName, object entityId)
        : base($"{entityName} with id '{entityId}' was not found.")
    {
        EntityName = entityName;
        EntityId = entityId;
    }

    public string EntityName { get; }

    public object EntityId { get; }
}
