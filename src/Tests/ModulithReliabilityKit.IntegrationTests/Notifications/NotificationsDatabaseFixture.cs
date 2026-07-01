using Microsoft.EntityFrameworkCore;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure;
using Testcontainers.PostgreSql;

namespace ModulithReliabilityKit.IntegrationTests.Notifications;

/// <summary>
/// Spins up a throwaway PostgreSQL (matching the project's <c>postgres:16-alpine</c> image), applies
/// the Notifications migrations once, and hands out fresh <see cref="NotificationsContext"/> instances.
/// A fresh context per logical step mirrors the per-scope <c>DbContext</c> lifetime in production and
/// lets a test simulate a process restart by simply opening a new context.
/// </summary>
public sealed class NotificationsDatabaseFixture : IAsyncLifetime
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

    public NotificationsContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new NotificationsContext(options);
    }

    /// <summary>Clears all module tables so each test starts from a known-empty state.</summary>
    public async Task ResetAsync()
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE notifications.inbox_messages, "
            + "notifications.inbox_dead_letters, "
            + "notifications.product_announcements RESTART IDENTITY CASCADE;");
    }
}
