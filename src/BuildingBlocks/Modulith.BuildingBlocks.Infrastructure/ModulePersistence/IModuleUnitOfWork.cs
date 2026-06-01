using Microsoft.EntityFrameworkCore;
using Modulith.BuildingBlocks.Application;

namespace Modulith.BuildingBlocks.Infrastructure.ModulePersistence;

public interface IModuleUnitOfWork<TContext> : IUnitOfWork
    where TContext : DbContext;
