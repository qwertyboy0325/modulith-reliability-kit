using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure;
using Testcontainers.PostgreSql;

namespace ModulithReliabilityKit.IntegrationTests.Catalog;

/// <summary>
/// Throwaway PostgreSQL for the Catalog outbox tests. Applies the Catalog migrations once and hands
/// out fresh <see cref="CatalogContext"/> instances; a new context models a fresh process lifetime,
/// which is how the outbox crash-recovery test simulates a restart.
/// </summary>
public sealed class CatalogDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public string ConnectionString => _container.GetConnectionString();

    public CatalogContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new CatalogContext(options);
    }

    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE catalog.outbox_messages, catalog.products RESTART IDENTITY CASCADE;");
    }
}
