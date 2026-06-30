using System.Reflection;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.ModulePersistence;

public sealed record ModulePersistenceRegistration(Assembly RequestAssembly, Type DbContextType);
