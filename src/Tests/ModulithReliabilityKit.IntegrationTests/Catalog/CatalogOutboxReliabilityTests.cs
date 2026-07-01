using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ModulithReliabilityKit.BuildingBlocks.Application.Outbox;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Diagnostics;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure.Processing;
using ModulithReliabilityKit.Modules.Catalog.IntegrationEvents;

namespace ModulithReliabilityKit.IntegrationTests.Catalog;

/// <summary>
/// Publisher-side reliability of the Catalog outbox against a real PostgreSQL instance.
/// A committed-but-unpublished outbox row models a crash between the business transaction and the
/// bus hop; on the next process lifetime the production <see cref="CatalogOutboxProcessor"/> must
/// republish it exactly once and mark it processed so it never re-publishes in steady state.
/// </summary>
[Collection(CatalogDatabaseCollection.Name)]
public sealed class CatalogOutboxReliabilityTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly CatalogDatabaseFixture _fixture;

    public CatalogOutboxReliabilityTests(CatalogDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Unpublished_Outbox_Row_Is_Republished_Exactly_Once_After_Restart()
    {
        await _fixture.ResetAsync();
        var @event = NewEvent();
        await SeedUnprocessedOutboxAsync(@event);

        var bus = new CapturingEventBus();

        // First lifetime after the "crash": drain the outbox.
        await DrainAsync(bus);

        var published = Assert.Single(bus.Published);
        var productCreated = Assert.IsType<ProductCreatedIntegrationEvent>(published);
        Assert.Equal(@event.Id, productCreated.Id);
        Assert.Equal(@event.ProductId, productCreated.ProductId);

        await using (var context = _fixture.CreateContext())
        {
            var row = await context.OutboxMessages.SingleAsync(x => x.LogicalId == @event.Id);
            Assert.NotNull(row.ProcessedOnUtc);
        }

        // Second lifetime: the processed row has left the pending set, so nothing is republished.
        bus.Published.Clear();
        await DrainAsync(bus);
        Assert.Empty(bus.Published);
    }

    private static ProductCreatedIntegrationEvent NewEvent() => new(
        id: Guid.NewGuid(),
        occurredOnUtc: DateTime.UtcNow,
        productId: Guid.NewGuid(),
        name: "Integration Test Product",
        price: 19.99m,
        currency: "USD");

    private async Task SeedUnprocessedOutboxAsync(ProductCreatedIntegrationEvent @event)
    {
        await using var context = _fixture.CreateContext();
        context.OutboxMessages.Add(new OutboxMessage(
            @event.Id,
            @event.OccurredOnUtc,
            @event.GetType().FullName!,
            JsonSerializer.Serialize(@event, @event.GetType(), SerializerOptions)));
        await context.SaveChangesAsync();
    }

    private async Task DrainAsync(CapturingEventBus bus)
    {
        await using var context = _fixture.CreateContext();
        var processor = new CatalogOutboxProcessor(context, bus, new ReliabilityMetrics(), NullLogger<CatalogOutboxProcessor>.Instance);
        await processor.ProcessAsync();
    }
}
