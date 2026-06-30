using ModulithReliabilityKit.BuildingBlocks.Application;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.ExecutionContext;

public sealed class NullExecutionContextAccessor : IExecutionContextAccessor
{
    public Guid CorrelationId => Guid.Empty;

    public Guid? UserId => null;

    public Guid? TenantId => null;

    public bool IsAvailable => false;
}
