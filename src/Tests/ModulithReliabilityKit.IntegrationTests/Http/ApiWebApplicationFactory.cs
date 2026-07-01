using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure.Processing;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.Processing;
using Testcontainers.PostgreSql;

namespace ModulithReliabilityKit.IntegrationTests.Http;

/// <summary>
/// Boots the real API host against a throwaway Postgres so the HTTP surface (create product /
/// read announcements) is exercised end to end. The periodic outbox/inbox background services are
/// removed so the test drives the processors deterministically instead of waiting on their timers;
/// the on-startup migration hosted services are kept so the container schema is created on boot.
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    async Task IAsyncLifetime.InitializeAsync()
    {
        await _container.StartAsync();

        // Program binds the module connection strings during service registration (before
        // ConfigureTestServices runs), so inject them as environment variables which
        // WebApplication.CreateBuilder reads eagerly when the host is first built.
        Environment.SetEnvironmentVariable("ConnectionStrings__Catalog", _container.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__Notifications", _container.GetConnectionString());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Catalog", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Notifications", null);
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            RemoveHostedService<CatalogOutboxBackgroundService>(services);
            RemoveHostedService<NotificationsInboxBackgroundService>(services);
        });
    }

    private static void RemoveHostedService<T>(IServiceCollection services)
    {
        foreach (var descriptor in services
                     .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T))
                     .ToList())
        {
            services.Remove(descriptor);
        }
    }
}
