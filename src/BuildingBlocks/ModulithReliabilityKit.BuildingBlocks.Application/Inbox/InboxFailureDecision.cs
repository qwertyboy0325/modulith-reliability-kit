namespace ModulithReliabilityKit.BuildingBlocks.Application.Inbox;

/// <summary>
/// The outcome of applying an <see cref="InboxRetryPolicy"/> to a failed inbox message:
/// either schedule another retry after <see cref="RetryDelay"/>, or give up and dead-letter it.
/// Pure data so the decision is unit-testable without a database or clock.
/// </summary>
public readonly record struct InboxFailureDecision(bool ShouldDeadLetter, int RetryCount, TimeSpan RetryDelay)
{
    public static InboxFailureDecision Retry(int retryCount, TimeSpan delay) => new(false, retryCount, delay);

    public static InboxFailureDecision DeadLetter(int retryCount) => new(true, retryCount, TimeSpan.Zero);
}
