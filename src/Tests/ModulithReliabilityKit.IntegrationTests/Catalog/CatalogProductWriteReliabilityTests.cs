using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DependencyInjection;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;
using ModulithReliabilityKit.Modules.Catalog.Application.Products.CreateProduct;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure.Configuration;
using ModulithReliabilityKit.Modules.Catalog.IntegrationEvents;

namespace ModulithReliabilityKit.IntegrationTests.Catalog;

/// <summary>
/// Verifies the publisher-side atomic-write guarantee end to end: sending a real
/// <see cref="CreateProductCommand"/> through the module's MediatR pipeline must persist the
/// aggregate and its outbox row in the same committed transaction (the outbox pattern's "no
/// dual-write"). The command is driven through the same composition the host uses, so the
/// <c>UnitOfWorkBehavior</c> that dispatches domain events into the outbox is exercised for real.
/// </summary>
[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogProductWriteReliabilityTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly CatalogDatabaseFixture _fixture;

    public CatalogProductWriteReliabilityTests(CatalogDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Creating_A_Product_Commits_The_Aggregate_And_Outbox_Row_Together()
    {
        await _fixture.ResetAsync();

        await using var provider = BuildCatalogProvider();
        CatalogModule.MapDomainNotifications(provider.GetRequiredService<IDomainNotificationsMapper>());

        Guid productId;
        using (var scope = provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            productId = await sender.Send(new CreateProductCommand("Reliability Widget", 9.99m, "USD"));
        }

        // Assert from a fresh connection so this reflects committed state, not tracked entities.
        await using var context = _fixture.CreateContext();

        Assert.Equal(1, await context.Products.CountAsync());

        var outbox = await context.OutboxMessages.SingleAsync();
        Assert.Equal(typeof(ProductCreatedIntegrationEvent).FullName, outbox.Type);
        Assert.Null(outbox.ProcessedOnUtc);

        var published = JsonSerializer.Deserialize<ProductCreatedIntegrationEvent>(outbox.Payload, SerializerOptions);
        Assert.NotNull(published);
        Assert.Equal(productId, published!.ProductId);
        Assert.Equal("Reliability Widget", published.Name);
        Assert.Equal(9.99m, published.Price);
        Assert.Equal("USD", published.Currency);
    }

    private ServiceProvider BuildCatalogProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Mirror the host composition for the Catalog module (host owns non-persistence building
        // blocks; the module registers its own DbContext-bound persistence + pipeline).
        services.AddModulithReliabilityKitBuildingBlocks(includePersistenceServices: false);
        services.AddCatalogModule(_fixture.ConnectionString);

        return services.BuildServiceProvider();
    }
}
