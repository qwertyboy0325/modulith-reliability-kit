using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DependencyInjection;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;
using ModulithReliabilityKit.Modules.Catalog.Application.Products.CreateProduct;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure.Configuration;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure.Processing;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.Configuration;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.Processing;

namespace ModulithReliabilityKit.IntegrationTests.CrossModule;

/// <summary>
/// The end-to-end reliability story across two modules: creating a product in Catalog must reach
/// Notifications as exactly one announcement, and the durable-publish / at-least-once nature of the
/// outbox+bus hop must be absorbed by the idempotent inbox so a redelivery never doubles the effect.
/// The whole graph is composed exactly like the host (shared in-memory bus + module subscription).
/// </summary>
public sealed class CrossModuleReliabilityE2ETests : IClassFixture<CrossModuleDatabaseFixture>
{
    private readonly CrossModuleDatabaseFixture _fixture;

    public CrossModuleReliabilityE2ETests(CrossModuleDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Product_Creation_Flows_To_A_Single_Announcement()
    {
        await _fixture.ResetAsync();
        await using var provider = BuildHostLikeProvider();

        var productId = await CreateProductAsync(provider, "E2E Widget");

        await DrainOutboxAsync(provider);  // Catalog publishes -> bus -> Notifications ingests to inbox.
        await DrainInboxAsync(provider);   // Notifications applies the inbox message.

        await using var notifications = _fixture.CreateNotificationsContext();
        Assert.Equal(1, await notifications.ProductAnnouncements.CountAsync(x => x.ProductId == productId));
    }

    [Fact]
    public async Task Outbox_Redelivery_Is_Absorbed_Idempotently_By_The_Inbox()
    {
        await _fixture.ResetAsync();
        await using var provider = BuildHostLikeProvider();

        var productId = await CreateProductAsync(provider, "E2E Widget");

        await DrainOutboxAsync(provider);  // Publish #1.

        // Simulate a crash after publishing but before the outbox row was marked processed:
        // the row is still pending, so the next drain redelivers the same event (at-least-once).
        await using (var catalog = _fixture.CreateCatalogContext())
        {
            var row = await catalog.OutboxMessages.SingleAsync();
            row.ProcessedOnUtc = null;
            await catalog.SaveChangesAsync();
        }

        await DrainOutboxAsync(provider);  // Publish #2 (duplicate delivery).

        // Idempotent ingest collapses the duplicate to a single inbox row...
        await using (var notifications = _fixture.CreateNotificationsContext())
        {
            Assert.Equal(1, await notifications.InboxMessages.CountAsync());
        }

        await DrainInboxAsync(provider);

        // ...and therefore a single applied effect.
        await using (var notifications = _fixture.CreateNotificationsContext())
        {
            Assert.Equal(1, await notifications.ProductAnnouncements.CountAsync(x => x.ProductId == productId));
        }
    }

    private ServiceProvider BuildHostLikeProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModulithReliabilityKitBuildingBlocks(includePersistenceServices: false);
        services.AddCatalogModule(_fixture.ConnectionString);
        services.AddNotificationsModule(_fixture.ConnectionString);

        var provider = services.BuildServiceProvider();

        CatalogModule.MapDomainNotifications(provider.GetRequiredService<IDomainNotificationsMapper>());
        NotificationsModule.SubscribeIntegrationEvents(provider);

        return provider;
    }

    private static async Task<Guid> CreateProductAsync(IServiceProvider provider, string name)
    {
        using var scope = provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        return await sender.Send(new CreateProductCommand(name, 9.99m, "USD"));
    }

    private static async Task DrainOutboxAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CatalogOutboxProcessor>().ProcessAsync();
    }

    private static async Task DrainInboxAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotificationsInboxProcessor>().ProcessAsync();
    }
}
