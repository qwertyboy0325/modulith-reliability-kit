using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.Processing;

/// <summary>
/// Periodically drains the Notifications inbox in its own scope. A hosted service keeps the skeleton
/// self-contained; a production deployment would typically use a scheduler such as Quartz.
/// </summary>
internal sealed class NotificationsInboxBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationsInboxBackgroundService> _logger;

    public NotificationsInboxBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationsInboxBackgroundService> logger)
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
                var processor = scope.ServiceProvider.GetRequiredService<NotificationsInboxProcessor>();
                await processor.ProcessAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notifications inbox processing tick failed");
            }
        }
    }
}
