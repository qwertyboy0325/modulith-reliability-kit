using ModulithReliabilityKit.BuildingBlocks.Application;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.ModulePersistence;

public interface IUnitOfWorkResolver
{
    IUnitOfWork Resolve(Type requestType);
}
