namespace ModulithReliabilityKit.BuildingBlocks.Domain;

public interface IAsyncBusinessRule : IBusinessRule
{
    Task<bool> IsSatisfiedAsync(CancellationToken cancellationToken = default);
}
