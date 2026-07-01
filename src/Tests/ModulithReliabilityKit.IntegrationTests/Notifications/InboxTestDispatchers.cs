using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

namespace ModulithReliabilityKit.IntegrationTests.Notifications;

/// <summary>Always fails before staging any effect, modelling a permanently broken downstream.</summary>
internal sealed class AlwaysThrowingDispatcher : IInboxDispatcher
{
    public Task DispatchAsync(InboxMessage message, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Simulated permanent downstream failure.");
}

/// <summary>
/// Stages the real business effect (via the wrapped dispatcher) and then throws before the processor
/// can commit, modelling a crash after work was applied but before the transaction completed. The
/// processor must roll the staged effect back, leaving no partial state.
/// </summary>
internal sealed class StageThenCrashDispatcher : IInboxDispatcher
{
    private readonly IInboxDispatcher _inner;

    public StageThenCrashDispatcher(IInboxDispatcher inner)
    {
        _inner = inner;
    }

    public async Task DispatchAsync(InboxMessage message, CancellationToken cancellationToken = default)
    {
        await _inner.DispatchAsync(message, cancellationToken);
        throw new InvalidOperationException("Simulated crash after staging effect, before commit.");
    }
}
