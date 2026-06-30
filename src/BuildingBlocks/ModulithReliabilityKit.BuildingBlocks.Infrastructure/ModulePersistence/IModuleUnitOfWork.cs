using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.BuildingBlocks.Application;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.ModulePersistence;

public interface IModuleUnitOfWork<TContext> : IUnitOfWork
    where TContext : DbContext;
