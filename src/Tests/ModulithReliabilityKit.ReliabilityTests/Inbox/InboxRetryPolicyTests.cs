using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;

namespace ModulithReliabilityKit.ReliabilityTests.Inbox;

/// <summary>
/// The retry/dead-letter boundary is the core of the consumer-side reliability guarantee, so it is
/// pinned by direct unit tests independent of the database or a clock.
/// </summary>
public class InboxRetryPolicyTests
{
    [Fact]
    public void First_Failure_Schedules_An_Immediate_Retry()
    {
        var decision = InboxRetryPolicy.Default.OnFailure(currentRetryCount: 0);

        Assert.False(decision.ShouldDeadLetter);
        Assert.Equal(1, decision.RetryCount);
        Assert.Equal(TimeSpan.Zero, decision.RetryDelay);
    }

    [Fact]
    public void Second_Failure_Backs_Off_Before_Retrying()
    {
        var decision = InboxRetryPolicy.Default.OnFailure(currentRetryCount: 1);

        Assert.False(decision.ShouldDeadLetter);
        Assert.Equal(2, decision.RetryCount);
        Assert.Equal(TimeSpan.FromMinutes(1), decision.RetryDelay);
    }

    [Fact]
    public void Failure_At_Max_Attempts_Dead_Letters()
    {
        // Default MaxAttempts = 3: after the 2nd retry has already failed, the 3rd attempt dead-letters.
        var decision = InboxRetryPolicy.Default.OnFailure(currentRetryCount: 2);

        Assert.True(decision.ShouldDeadLetter);
        Assert.Equal(3, decision.RetryCount);
    }

    [Fact]
    public void A_Single_Attempt_Policy_Dead_Letters_On_First_Failure()
    {
        var policy = new InboxRetryPolicy(maxAttempts: 1, backoff: _ => TimeSpan.Zero);

        var decision = policy.OnFailure(currentRetryCount: 0);

        Assert.True(decision.ShouldDeadLetter);
        Assert.Equal(1, decision.RetryCount);
    }

    [Fact]
    public void Max_Attempts_Must_Be_Positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InboxRetryPolicy(0, _ => TimeSpan.Zero));
    }
}
