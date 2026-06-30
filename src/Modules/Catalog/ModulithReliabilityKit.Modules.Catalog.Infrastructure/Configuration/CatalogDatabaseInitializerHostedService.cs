using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ModulithReliabilityKit.Modules.Catalog.Infrastructure.Configuration;

/// <summary>
/// Applies Catalog schema/migrations on startup for local/demo environments.
/// </summary>
internal sealed class CatalogDatabaseInitializerHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CatalogDatabaseInitializerHostedService> _logger;

    public CatalogDatabaseInitializerHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<CatalogDatabaseInitializerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogContext>();

        _logger.LogInformation("Applying Catalog database migrations");
        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
