using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Modulith.Modules.Catalog.Infrastructure.Configuration;

internal sealed class CatalogContextFactory : IDesignTimeDbContextFactory<CatalogContext>
{
    public CatalogContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CATALOG_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=modulith;Username=modulith;Password=modulith";

        var optionsBuilder = new DbContextOptionsBuilder<CatalogContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new CatalogContext(optionsBuilder.Options);
    }
}
