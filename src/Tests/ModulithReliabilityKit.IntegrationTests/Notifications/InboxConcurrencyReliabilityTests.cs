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
/// Multi-instance concurrency safety of the Notifications inbox against a real PostgreSQL instance.
/// A production deployment can run more than one API instance, so more than one
/// <see cref="NotificationsInboxProcessor"/> can drain the same inbox at the same time. These tests
/// pin the row-claim contract (<c>FOR UPDATE SKIP LOCKED</c>): a message is applied exactly once and
/// a concurrent worker skips a claimed row instead of double-dispatching it or recording a spurious
/// failure.
/// </summary>
public sealed class InboxConcurrencyReliabilityTests : IClassFixture<NotificationsDatabaseFixture>
{
    private static readonly InboxRetryPolicy ImmediatePolicy = new(maxAttempts: 3, backoff: _ => TimeSpan.Zero);

    private readonly NotificationsDatabaseFixture _fixture;

    public InboxConcurrencyReliabilityTests(NotificationsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_Row_Claimed_By_One_Processor_Is_Skipped_By_A_Concurrent_Processor()
    {
        await _fixture.ResetAsync();
        var productId = Guid.NewGuid();
        var @event = NewEvent(productId);
        await IngestAsync(@event);

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Processor A claims (and row-locks) the message, then blocks inside dispatch while still
        // holding its open transaction. The row is locked for the whole time A is parked here.
        await using var contextA = _fixture.CreateContext();
        var blocking = new BlockingDispatcher(RealDispatcher(contextA), entered, release.Task);
        var runA = BuildProcessor(contextA, blocking).ProcessAsync();

        // Proceed once A has claimed the row and entered dispatch. If A instead faults before entering
        // (e.g. the claim query fails), surface that immediately rather than blocking on entered.Task.
        await Task.WhenAny(entered.Task, runA);
        if (runA.IsCompleted)
        {
            await runA; // rethrow the fault
        }

        // Processor B runs to completion while A holds the lock. SKIP LOCKED must make B skip the
        // claimed row entirely: no dispatch, no effect, no failure bookkeeping.
        await using (var contextB = _fixture.CreateContext())
        {
            await BuildProcessor(contextB, RealDispatcher(contextB)).ProcessAsync();
        }

        // A has not committed yet and B skipped the row, so nothing is applied and the row is untouched.
        Assert.Equal(0, await CountAnnouncementsAsync(productId));
        Assert.Equal("pending", await InboxStatusAsync(@event.Id));
        Assert.Equal(0, await InboxRetryCountAsync(@event.Id));

        // Release A: it dispatches and commits the effect + processed mark atomically.
        release.SetResult();
        await runA;

        Assert.Equal(1, await CountAnnouncementsAsync(productId));
        Assert.Equal("processed", await InboxStatusAsync(@event.Id));
        Assert.Equal(0, await InboxRetryCountAsync(@event.Id));
        Assert.Equal(0, await DeadLetterCountAsync(@event.Id));
    }

    [Fact]
    public async Task Concurrent_Processors_Drain_Distinct_Messages_Without_Dropping_Any()
    {
        await _fixture.ResetAsync();
        var first = NewEvent(Guid.NewGuid());
        var second = NewEvent(Guid.NewGuid());
        await IngestAsync(first);
        await IngestAsync(second);

        // Two processors drain the same pending set at once. SKIP LOCKED must serialize access to each
        // row without losing work: every message is applied exactly once regardless of interleaving.
        await using var contextA = _fixture.CreateContext();
        await using var contextB = _fixture.CreateContext();
        await Task.WhenAll(
            BuildProcessor(contextA, RealDispatcher(contextA)).ProcessAsync(),
            BuildProcessor(contextB, RealDispatcher(contextB)).ProcessAsync());

        foreach (var @event in new[] { first, second })
        {
            Assert.Equal(1, await CountAnnouncementsAsync(@event.ProductId));
            Assert.Equal("processed", await InboxStatusAsync(@event.Id));
            Assert.Equal(0, await InboxRetryCountAsync(@event.Id));
            Assert.Equal(0, await DeadLetterCountAsync(@event.Id));
        }
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

    private static IInboxDispatcher RealDispatcher(NotificationsContext context)
        => new ProductCreatedInboxDispatcher(new ProductAnnouncementStore(context));

    private static NotificationsInboxProcessor BuildProcessor(NotificationsContext context, IInboxDispatcher dispatcher)
        => new(context, dispatcher, ImmediatePolicy, NullLogger<NotificationsInboxProcessor>.Instance);

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

    private async Task<int> DeadLetterCountAsync(Guid logicalId)
    {
        await using var context = _fixture.CreateContext();
        return await context.InboxDeadLetters.CountAsync(x => x.OriginalMessageId == logicalId);
    }

    /// <summary>
    /// Dispatcher that parks the caller (with its row lock held) until released, so a test can hold a
    /// claimed inbox row open and observe how a second concurrent processor behaves.
    /// </summary>
    private sealed class BlockingDispatcher : IInboxDispatcher
    {
        private readonly IInboxDispatcher _inner;
        private readonly TaskCompletionSource _entered;
        private readonly Task _release;

        public BlockingDispatcher(IInboxDispatcher inner, TaskCompletionSource entered, Task release)
        {
            _inner = inner;
            _entered = entered;
            _release = release;
        }

        public async Task DispatchAsync(InboxMessage message, CancellationToken cancellationToken = default)
        {
            _entered.TrySetResult();
            await _release;
            await _inner.DispatchAsync(message, cancellationToken);
        }
    }
}
