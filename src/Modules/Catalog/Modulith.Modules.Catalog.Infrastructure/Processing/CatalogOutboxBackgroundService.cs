using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Modulith.Modules.Catalog.Infrastructure.Processing;

/// <summary>
/// Periodically runs the Catalog outbox processor in its own scope.
/// In production this is typically a Quartz job; a hosted service keeps the skeleton
/// self-contained without adding a scheduler dependency.
/// </summary>
internal sealed class CatalogOutboxBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CatalogOutboxBackgroundService> _logger;

    public CatalogOutboxBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<CatalogOutboxBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<CatalogOutboxProcessor>();
                await processor.ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Catalog outbox processing tick failed");
            }
        }
    }
}
