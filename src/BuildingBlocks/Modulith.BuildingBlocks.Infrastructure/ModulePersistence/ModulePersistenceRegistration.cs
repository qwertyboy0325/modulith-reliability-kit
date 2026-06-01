using System.Reflection;

namespace Modulith.BuildingBlocks.Infrastructure.ModulePersistence;

public sealed record ModulePersistenceRegistration(Assembly RequestAssembly, Type DbContextType);
