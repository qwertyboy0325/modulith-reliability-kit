using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithReliabilityKit.BuildingBlocks.Application.Events;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Events;
using ModulithReliabilityKit.Modules.Catalog.IntegrationEvents;
using ModulithReliabilityKit.Modules.Notifications.Application;
using ModulithReliabilityKit.Modules.Notifications.Application.Contracts;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;
using ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements;
using ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements.GetProductAnnouncements;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.Inbox;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.Processing;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.ProductAnnouncements;

namespace ModulithReliabilityKit.Modules.Notifications.Infrastructure.Configuration;

/// <summary>
/// Notifications module composition root (MS.DI-first). This is the sanctioned place that wires the
/// foreign integration-event subscription; the module's own Application/Infrastructure code never
/// reaches into another module beyond its public IntegrationEvents contract.
/// </summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<NotificationsContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<INotificationsModule, NotificationsModuleFacade>();
        services.AddScoped<IInboxWriter, InboxWriter>();
        services.AddScoped<IInboxDispatcher, ProductCreatedInboxDispatcher>();
        services.AddScoped<IInboxDeadLetterReadStore, InboxDeadLetterReadStore>();
        services.AddScoped<IInboxDeadLetterReprocessor, InboxDeadLetterReprocessor>();
        services.AddScoped<IProductAnnouncementStore, ProductAnnouncementStore>();
        services.AddScoped<IProductAnnouncementReadStore, ProductAnnouncementReadStore>();

        // Concrete bus handler, resolved per delivery inside a fresh scope by the bus subscription.
        services.AddScoped<ProductCreatedIngestHandler>();

        services.AddScoped<NotificationsInboxProcessor>();
        services.AddSingleton(InboxRetryPolicy.Default);

        services.AddHostedService<NotificationsInboxBackgroundService>();
        services.AddHostedService<NotificationsDatabaseInitializerHostedService>();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(NotificationsApplicationAssembly.Assembly));

        return services;
    }

    /// <summary>
    /// Subscribes the module's durable ingest handler to the bus. Call once at startup after the
    /// provider is built (the bus is a process-wide singleton).
    /// </summary>
    public static void SubscribeIntegrationEvents(IServiceProvider serviceProvider)
    {
        var bus = serviceProvider.GetRequiredService<IEventsBus>();
        var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

        bus.Subscribe(
            new ScopedIntegrationEventHandler<ProductCreatedIntegrationEvent, ProductCreatedIngestHandler>(scopeFactory));
    }
}
