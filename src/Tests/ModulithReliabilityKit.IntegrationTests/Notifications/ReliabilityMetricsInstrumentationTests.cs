using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Diagnostics;
using ModulithReliabilityKit.Modules.Catalog.IntegrationEvents;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;
using ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.Inbox;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.Processing;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.ProductAnnouncements;

namespace ModulithReliabilityKit.IntegrationTests.Notifications;

/// <summary>
/// Proves the reliability metrics are actually emitted from the production drain path (not just defined):
/// a <see cref="MeterListener"/> attached to a test-isolated meter observes the processed / retried /
/// dead-lettered counters change as the real <see cref="NotificationsInboxProcessor"/> runs against a
/// real PostgreSQL instance. Keeps the project's "every claim has a test" property for observability.
/// </summary>
public sealed class ReliabilityMetricsInstrumentationTests : IClassFixture<NotificationsDatabaseFixture>
{
    private static readonly InboxRetryPolicy ImmediatePolicy = new(maxAttempts: 3, backoff: _ => TimeSpan.Zero);

    private readonly NotificationsDatabaseFixture _fixture;

    public ReliabilityMetricsInstrumentationTests(NotificationsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Inbox_Outcomes_Emit_Counters_From_The_Real_Drain_Path()
    {
        await _fixture.ResetAsync();

        // Unique meter name so this listener never sees measurements from tests running in parallel.
        var meterName = "test-" + Guid.NewGuid().ToString("N");
        using var metrics = new ReliabilityMetrics(meterName);

        var counters = new ConcurrentDictionary<string, long>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == meterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
            counters.AddOrUpdate(instrument.Name, measurement, (_, existing) => existing + measurement));
        listener.Start();

        // Success path: one event applied exactly once.
        await IngestAsync(NewEvent(Guid.NewGuid()));
        await DrainAsync(RealDispatcher, metrics);

        // Dead-letter path: a permanently failing event retried to exhaustion (maxAttempts = 3).
        await IngestAsync(NewEvent(Guid.NewGuid()));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await DrainAsync(_ => new AlwaysThrowingDispatcher(), metrics);
        }

        Assert.Equal(1, counters.GetValueOrDefault("messaging.inbox.processed"));
        Assert.Equal(1, counters.GetValueOrDefault("messaging.inbox.dead_lettered"));
        Assert.True(
            counters.GetValueOrDefault("messaging.inbox.retried") >= 1,
            "expected at least one retry to be counted before the dead-letter");
    }

    private static ProductCreatedIntegrationEvent NewEvent(Guid productId) => new(
        id: Guid.NewGuid(),
        occurredOnUtc: DateTime.UtcNow,
        productId: productId,
        name: "Metrics Test Product",
        price: 1.00m,
        currency: "USD");

    private async Task IngestAsync(ProductCreatedIntegrationEvent @event)
    {
        await using var context = _fixture.CreateContext();
        await new ProductCreatedIngestHandler(new InboxWriter(context)).Handle(@event);
    }

    private async Task DrainAsync(Func<NotificationsContext, IInboxDispatcher> dispatcher, ReliabilityMetrics metrics)
    {
        await using var context = _fixture.CreateContext();
        var processor = new NotificationsInboxProcessor(
            context,
            dispatcher(context),
            ImmediatePolicy,
            metrics,
            NullLogger<NotificationsInboxProcessor>.Instance);

        await processor.ProcessAsync();
    }

    private static IInboxDispatcher RealDispatcher(NotificationsContext context)
        => new ProductCreatedInboxDispatcher(new ProductAnnouncementStore(context));
}
