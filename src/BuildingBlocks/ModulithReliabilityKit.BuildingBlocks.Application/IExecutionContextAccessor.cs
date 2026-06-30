namespace ModulithReliabilityKit.BuildingBlocks.Application;

public interface IExecutionContextAccessor
{
    Guid CorrelationId { get; }

    Guid? UserId { get; }

    Guid? TenantId { get; }

    bool IsAvailable { get; }
}
