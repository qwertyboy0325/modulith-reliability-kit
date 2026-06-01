using Microsoft.EntityFrameworkCore;
using Modulith.BuildingBlocks.Application.Outbox;
using Modulith.BuildingBlocks.Infrastructure.DataAccess;
using Modulith.Modules.Catalog.Domain.Products;

namespace Modulith.Modules.Catalog.Infrastructure;

public sealed class CatalogContext : DbContext
{
    public const string Schema = "catalog";

    public CatalogContext(DbContextOptions<CatalogContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogContext).Assembly);
        modelBuilder.ApplyStronglyTypedIdConverters();
    }
}
