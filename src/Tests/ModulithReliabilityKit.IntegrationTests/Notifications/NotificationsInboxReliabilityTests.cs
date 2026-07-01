using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;
using ModulithReliabilityKit.Modules.Catalog.IntegrationEvents;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;
using ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.Inbox;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.Processing;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.ProductAnnouncements;

namespace ModulithReliabilityKit.IntegrationTests.Notifications;

/// <summary>
/// End-to-end reliability guarantees of the Notifications inbox against a real PostgreSQL instance.
/// Each test drives the production ingest path (<see cref="ProductCreatedIngestHandler"/> +
/// <see cref="InboxWriter"/>) and the production drain path
/// (<see cref="NotificationsInboxProcessor"/>) directly, so the durable-delivery, exactly-once, and
/// dead-letter claims are verified through the same code that runs in the host.
/// </summary>
public sealed class NotificationsInboxReliabilityTests : IClassFixture<NotificationsDatabaseFixture>
{
    // Zero backoff so a scheduled retry is always immediately due in real time.
    private static readonly InboxRetryPolicy ImmediatePolicy = new(maxAttempts: 3, backoff: _ => TimeSpan.Zero);

    private readonly NotificationsDatabaseFixture _fixture;

    public NotificationsInboxReliabilityTests(NotificationsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Duplicate_Delivery_Produces_Exactly_One_Inbox_Row_And_One_Effect()
    {
        await _fixture.ResetAsync();
        var productId = Guid.NewGuid();
        var @event = NewEvent(productId);

        // At-least-once bus delivery: the same event arrives twice on separate deliveries.
        await IngestAsync(@event);
        await IngestAsync(@event);

        Assert.Equal(1, await CountInboxAsync(@event.Id));

        await DrainWithRealDispatcherAsync();

        Assert.Equal(1, await CountAnnouncementsAsync(productId));
        Assert.Equal("processed", await InboxStatusAsync(@event.Id));

        // A redelivery after the message was already processed must not duplicate either side.
        await IngestAsync(@event);
        await DrainWithRealDispatcherAsync();

        Assert.Equal(1, await CountInboxAsync(@event.Id));
        Assert.Equal(1, await CountAnnouncementsAsync(productId));
    }

    [Fact]
    public async Task Crash_After_Staging_Effect_Rolls_Back_And_Recovers_Exactly_Once()
    {
        await _fixture.ResetAsync();
        var productId = Guid.NewGuid();
        var @event = NewEvent(productId);

        await IngestAsync(@event);

        // First lifetime: the effect is staged inside the transaction, then the process "crashes"
        // before commit. The rollback must leave no announcement and no processed mark behind.
        await using (var context = _fixture.CreateContext())
        {
            var crashing = new StageThenCrashDispatcher(RealDispatcher(context));
            await BuildProcessor(context, crashing).ProcessAsync();
        }

        Assert.Equal(0, await CountAnnouncementsAsync(productId));
        Assert.Equal("retrying", await InboxStatusAsync(@event.Id));
        Assert.Equal(1, await InboxRetryCountAsync(@event.Id));

        // Second lifetime (recovery): the durable inbox row is still pending and is applied once.
        await DrainWithRealDispatcherAsync();

        Assert.Equal(1, await CountAnnouncementsAsync(productId));
        Assert.Equal("processed", await InboxStatusAsync(@event.Id));
    }

    [Fact]
    public async Task Repeated_Failures_Dead_Letter_The_Message_After_Max_Attempts()
    {
        await _fixture.ResetAsync();
        var productId = Guid.NewGuid();
        var @event = NewEvent(productId);

        await IngestAsync(@event);

        // MaxAttempts = 3: three drains against a permanently failing downstream.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var context = _fixture.CreateContext();
            await BuildProcessor(context, new AlwaysThrowingDispatcher()).ProcessAsync();
        }

        await using var assertContext = _fixture.CreateContext();

        var message = await assertContext.InboxMessages.SingleAsync(x => x.LogicalId == @event.Id);
        Assert.Equal("dead_letter", message.Status);
        Assert.Equal(3, message.RetryCount);

        var deadLetter = await assertContext.InboxDeadLetters.SingleAsync(x => x.OriginalMessageId == @event.Id);
        Assert.Equal(3, deadLetter.RetryCount);

        Assert.Equal(0, await assertContext.ProductAnnouncements.CountAsync(x => x.ProductId == productId));

        // A dead-lettered message must drop out of the pending set, so further drains are no-ops.
        await DrainWithRealDispatcherAsync();
        Assert.Equal(0, await CountAnnouncementsAsync(productId));
    }

    private static ProductCreatedIntegrationEvent NewEvent(Guid productId) => new(
        id: Guid.NewGuid(),
        occurredOnUtc: DateTime.UtcNow,
        productId: productId,
        name: "Integration Test Product",
        price: 19.99m,
        currency: "USD");

    private async Task IngestAsync(ProductCreatedIntegrationEvent @event)
    {
        await using var context = _fixture.CreateContext();
        var handler = new ProductCreatedIngestHandler(new InboxWriter(context));
        await handler.Handle(@event);
    }

    private async Task DrainWithRealDispatcherAsync()
    {
        await using var context = _fixture.CreateContext();
        await BuildProcessor(context, RealDispatcher(context)).ProcessAsync();
    }

    private static IInboxDispatcher RealDispatcher(NotificationsContext context)
        => new ProductCreatedInboxDispatcher(new ProductAnnouncementStore(context));

    private static NotificationsInboxProcessor BuildProcessor(NotificationsContext context, IInboxDispatcher dispatcher)
        => new(context, dispatcher, ImmediatePolicy, NullLogger<NotificationsInboxProcessor>.Instance);

    private async Task<int> CountInboxAsync(Guid logicalId)
    {
        await using var context = _fixture.CreateContext();
        return await context.InboxMessages.CountAsync(x => x.LogicalId == logicalId);
    }

    private async Task<int> CountAnnouncementsAsync(Guid productId)
    {
        await using var context = _fixture.CreateContext();
        return await context.ProductAnnouncements.CountAsync(x => x.ProductId == productId);
    }

    private async Task<string> InboxStatusAsync(Guid logicalId)
    {
        await using var context = _fixture.CreateContext();
        return await context.InboxMessages.Where(x => x.LogicalId == logicalId).Select(x => x.Status).SingleAsync();
    }

    private async Task<int> InboxRetryCountAsync(Guid logicalId)
    {
        await using var context = _fixture.CreateContext();
        return await context.InboxMessages.Where(x => x.LogicalId == logicalId).Select(x => x.RetryCount).SingleAsync();
    }
}
