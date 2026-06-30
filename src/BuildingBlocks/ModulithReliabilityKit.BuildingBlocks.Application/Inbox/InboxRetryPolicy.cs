namespace ModulithReliabilityKit.BuildingBlocks.Application.Inbox;

/// <summary>
/// Decides what happens when processing an inbox message throws: retry with a backoff delay, or move
/// it to the dead-letter table after a bounded number of attempts. Keeping this as a pure, injectable
/// policy (no clock, no I/O) makes the retry/dead-letter boundary explicit and directly unit-testable.
/// </summary>
public sealed class InboxRetryPolicy
{
    private readonly Func<int, TimeSpan> _backoff;

    public InboxRetryPolicy(int maxAttempts, Func<int, TimeSpan> backoff)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "maxAttempts must be at least 1.");
        }

        MaxAttempts = maxAttempts;
        _backoff = backoff ?? throw new ArgumentNullException(nameof(backoff));
    }

    public int MaxAttempts { get; }

    /// <summary>
    /// Default policy: up to 3 attempts. First retry is immediate, then +1 minute, then +5 minutes,
    /// mirroring the reference inbox (ADR-004) before dead-lettering.
    /// </summary>
    public static InboxRetryPolicy Default { get; } = new(
        maxAttempts: 3,
        backoff: attempt => attempt switch
        {
            <= 1 => TimeSpan.Zero,
            2 => TimeSpan.FromMinutes(1),
            _ => TimeSpan.FromMinutes(5),
        });

    /// <summary>
    /// Given how many times the message has already failed (<paramref name="currentRetryCount"/>),
    /// decide the next step.
    /// </summary>
    public InboxFailureDecision OnFailure(int currentRetryCount)
    {
        var attemptsMade = currentRetryCount + 1;

        return attemptsMade >= MaxAttempts
            ? InboxFailureDecision.DeadLetter(attemptsMade)
            : InboxFailureDecision.Retry(attemptsMade, _backoff(attemptsMade));
    }
}
