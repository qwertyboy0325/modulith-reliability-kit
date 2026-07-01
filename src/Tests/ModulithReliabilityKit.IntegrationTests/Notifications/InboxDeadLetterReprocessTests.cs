using Microsoft.EntityFrameworkCore;
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
/// Closes the reliability loop: a message that exhausted its retries and dead-lettered can be
/// recovered by an operator once the downstream cause is fixed, and the recovery applies the effect
/// exactly once without leaving the dead-letter unresolved.
/// </summary>
public sealed class InboxDeadLetterReprocessTests : IClassFixture<NotificationsDatabaseFixture>
{
    private static readonly InboxRetryPolicy ImmediatePolicy = new(maxAttempts: 3, backoff: _ => TimeSpan.Zero);

    private readonly NotificationsDatabaseFixture _fixture;

    public InboxDeadLetterReprocessTests(NotificationsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Reprocessing_A_Dead_Letter_Requeues_It_And_Applies_The_Effect_Exactly_Once()
    {
        await _fixture.ResetAsync();
        var productId = Guid.NewGuid();
        var @event = NewEvent(productId);

        await IngestAsync(@event);
        await DeadLetterAsync(@event);

        var deadLetterId = await SingleDeadLetterIdAsync(@event.Id);

        // Reprocess: the poisoned message is requeued and the dead-letter is marked resolved atomically.
        await using (var context = _fixture.CreateContext())
        {
            var result = await new InboxDeadLetterReprocessor(context, new ReliabilityMetrics())
                .ReprocessAsync(deadLetterId, "operator@test");

            Assert.Equal(ReprocessDeadLetterOutcome.Requeued, result.Outcome);
        }

        await using (var afterRequeue = _fixture.CreateContext())
        {
            var message = await afterRequeue.InboxMessages.SingleAsync(x => x.LogicalId == @event.Id);
            Assert.Equal("pending", message.Status);
            Assert.Equal(0, message.RetryCount);
            Assert.Null(message.ProcessedOnUtc);

            var deadLetter = await afterRequeue.InboxDeadLetters.SingleAsync(x => x.Id == deadLetterId);
            Assert.NotNull(deadLetter.ResolvedOnUtc);
            Assert.Equal("reprocessed", deadLetter.ResolutionStatus);
            Assert.Equal("operator@test", deadLetter.ResolvedBy);
        }

        // The downstream is healthy now: the next drain applies the effect exactly once.
        await DrainWithRealDispatcherAsync();

        Assert.Equal(1, await CountAnnouncementsAsync(productId));
        Assert.Equal("processed", await InboxStatusAsync(@event.Id));
    }

    [Fact]
    public async Task Reprocessing_An_Already_Resolved_Dead_Letter_Is_A_No_Op()
    {
        await _fixture.ResetAsync();
        var productId = Guid.NewGuid();
        var @event = NewEvent(productId);

        await IngestAsync(@event);
        await DeadLetterAsync(@event);
        var deadLetterId = await SingleDeadLetterIdAsync(@event.Id);

        await ReprocessAsync(deadLetterId);
        await DrainWithRealDispatcherAsync();
        Assert.Equal(1, await CountAnnouncementsAsync(productId));

        // A second reprocess of the same (now resolved) dead-letter must not requeue or re-apply.
        await using (var context = _fixture.CreateContext())
        {
            var result = await new InboxDeadLetterReprocessor(context, new ReliabilityMetrics()).ReprocessAsync(deadLetterId, "operator@test");
            Assert.Equal(ReprocessDeadLetterOutcome.AlreadyResolved, result.Outcome);
        }

        await DrainWithRealDispatcherAsync();
        Assert.Equal(1, await CountAnnouncementsAsync(productId));
    }

    [Fact]
    public async Task Reprocessing_An_Unknown_Dead_Letter_Reports_Not_Found()
    {
        await _fixture.ResetAsync();

        await using var context = _fixture.CreateContext();
        var result = await new InboxDeadLetterReprocessor(context, new ReliabilityMetrics()).ReprocessAsync(Guid.NewGuid(), "operator@test");

        Assert.Equal(ReprocessDeadLetterOutcome.NotFound, result.Outcome);
    }

    private static ProductCreatedIntegrationEvent NewEvent(Guid productId) => new(
        id: Guid.NewGuid(),
        occurredOnUtc: DateTime.UtcNow,
        productId: productId,
        name: "Reprocess Test Product",
        price: 42.00m,
        currency: "USD");

    private async Task IngestAsync(ProductCreatedIntegrationEvent @event)
    {
        await using var context = _fixture.CreateContext();
        var handler = new ProductCreatedIngestHandler(new InboxWriter(context));
        await handler.Handle(@event);
    }

    private async Task DeadLetterAsync(ProductCreatedIntegrationEvent @event)
    {
        // MaxAttempts = 3 against a permanently failing downstream drives the message to dead-letter.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var context = _fixture.CreateContext();
            await BuildProcessor(context, new AlwaysThrowingDispatcher()).ProcessAsync();
        }

        await using var assertContext = _fixture.CreateContext();
        var message = await assertContext.InboxMessages.SingleAsync(x => x.LogicalId == @event.Id);
        Assert.Equal("dead_letter", message.Status);
    }

    private async Task ReprocessAsync(Guid deadLetterId)
    {
        await using var context = _fixture.CreateContext();
        await new InboxDeadLetterReprocessor(context, new ReliabilityMetrics()).ReprocessAsync(deadLetterId, "operator@test");
    }

    private async Task DrainWithRealDispatcherAsync()
    {
        await using var context = _fixture.CreateContext();
        var dispatcher = new ProductCreatedInboxDispatcher(new ProductAnnouncementStore(context));
        await BuildProcessor(context, dispatcher).ProcessAsync();
    }

    private static NotificationsInboxProcessor BuildProcessor(NotificationsContext context, IInboxDispatcher dispatcher)
        => new(context, dispatcher, ImmediatePolicy, new ReliabilityMetrics(), NullLogger<NotificationsInboxProcessor>.Instance);

    private async Task<Guid> SingleDeadLetterIdAsync(Guid logicalId)
    {
        await using var context = _fixture.CreateContext();
        return await context.InboxDeadLetters
            .Where(x => x.OriginalMessageId == logicalId)
            .Select(x => x.Id)
            .SingleAsync();
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
}
