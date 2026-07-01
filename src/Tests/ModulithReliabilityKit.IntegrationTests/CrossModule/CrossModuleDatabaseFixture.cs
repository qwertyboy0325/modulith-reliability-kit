using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure;
using Testcontainers.PostgreSql;

namespace ModulithReliabilityKit.IntegrationTests.CrossModule;

/// <summary>
/// A single PostgreSQL instance hosting both module schemas (<c>catalog</c> + <c>notifications</c>),
/// so the full publish → bus → consume chain can be exercised the way it runs in the host. Each
/// context keeps its own migration history in its own schema, so migrating both is conflict-free.
/// </summary>
public sealed class CrossModuleDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var catalog = CreateCatalogContext();
        await catalog.Database.MigrateAsync();

        await using var notifications = CreateNotificationsContext();
        await notifications.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public CatalogContext CreateCatalogContext()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new CatalogContext(options);
    }

    public NotificationsContext CreateNotificationsContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new NotificationsContext(options);
    }

    public async Task ResetAsync()
    {
        await using var context = CreateCatalogContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE catalog.outbox_messages, catalog.products, "
            + "notifications.inbox_messages, notifications.inbox_dead_letters, "
            + "notifications.product_announcements RESTART IDENTITY CASCADE;");
    }
}
