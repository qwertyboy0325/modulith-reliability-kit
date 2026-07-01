using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure.Processing;
using ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements.GetProductAnnouncements;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.Processing;

namespace ModulithReliabilityKit.IntegrationTests.Http;

/// <summary>
/// The full HTTP-level reliability story: a product created through the Catalog endpoint must reach
/// the Notifications module and be readable through its endpoint as exactly one announcement. The
/// request commits the aggregate + outbox row atomically; draining the (real) outbox publishes onto
/// the shared in-memory bus, the idempotent inbox ingests it, and draining the inbox applies it.
/// </summary>
public sealed class CatalogToNotificationsHttpE2ETests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public CatalogToNotificationsHttpE2ETests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Creating_A_Product_Over_Http_Surfaces_A_Single_Announcement()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            "/catalog/products/",
            new { name = "HTTP Widget", price = 12.50m, currency = "USD" });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedProduct>();
        Assert.NotNull(created);
        var productId = created!.Id;

        // Background drainers are removed in the test host, so drive the real processors here:
        // Catalog outbox publishes to the in-memory bus -> Notifications ingests to its inbox...
        await DrainAsync<CatalogOutboxProcessor>(p => p.ProcessAsync());
        // ...then the Notifications inbox applies the message, producing the announcement.
        await DrainAsync<NotificationsInboxProcessor>(p => p.ProcessAsync());

        var announcements = await client.GetFromJsonAsync<List<ProductAnnouncementDto>>(
            "/notifications/product-announcements");

        Assert.NotNull(announcements);
        Assert.Single(announcements!, a => a.ProductId == productId);
    }

    private async Task DrainAsync<TProcessor>(Func<TProcessor, Task> drain)
        where TProcessor : notnull
    {
        using var scope = _factory.Services.CreateScope();
        await drain(scope.ServiceProvider.GetRequiredService<TProcessor>());
    }

    private sealed record CreatedProduct(Guid Id);
}
