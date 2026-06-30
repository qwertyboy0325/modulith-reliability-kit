using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ModulithReliabilityKit.Modules.Catalog.Infrastructure.Configuration;

internal sealed class CatalogContextFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CATALOG_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=modulith_reliability_kit;Username=modulith_reliability_kit;Password=modulith_reliability_kit";

        var optionsBuilder = new DbContextOptionsBuilder<CatalogContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new CatalogContext(optionsBuilder.Options);
    }
}
