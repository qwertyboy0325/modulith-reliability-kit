using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.Configuration;

/// <summary>
/// Applies Notifications schema/migrations on startup for local/demo environments.
/// </summary>
internal sealed class NotificationsDatabaseInitializerHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationsDatabaseInitializerHostedService> _logger;

    public NotificationsDatabaseInitializerHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationsDatabaseInitializerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationsContext>();

        _logger.LogInformation("Applying Notifications database migrations");
        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
