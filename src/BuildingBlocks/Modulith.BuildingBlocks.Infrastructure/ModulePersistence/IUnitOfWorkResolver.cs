using Modulith.BuildingBlocks.Application;

namespace Modulith.BuildingBlocks.Infrastructure.ModulePersistence;

public interface IUnitOfWorkResolver
{
    IUnitOfWork Resolve(Type requestType);
}
