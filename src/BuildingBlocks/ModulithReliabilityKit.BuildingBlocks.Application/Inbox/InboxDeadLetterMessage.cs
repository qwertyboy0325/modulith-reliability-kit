namespace ModulithReliabilityKit.BuildingBlocks.Application.Inbox;

public sealed class InboxDeadLetterMessage
{
    public InboxDeadLetterMessage(
        Guid originalMessageId,
        string type,
        string payload,
        DateTime occurredOnUtc,
        int retryCount,
        string lastError)
    {
        Id = Guid.NewGuid();
        OriginalMessageId = originalMessageId;
        Type = type;
        Payload = payload;
        OccurredOnUtc = occurredOnUtc;
        RetryCount = retryCount;
        LastError = lastError;
        MovedToDeadLetterOnUtc = DateTime.UtcNow;
        ResolutionStatus = "pending";
    }

    private InboxDeadLetterMessage()
    {
        ResolutionStatus = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid OriginalMessageId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTime OccurredOnUtc { get; private set; }

    public int RetryCount { get; private set; }

    public string LastError { get; private set; } = string.Empty;

    public DateTime MovedToDeadLetterOnUtc { get; private set; }

    public DateTime? ResolvedOnUtc { get; set; }

    public string? ResolvedBy { get; set; }

    public string? ResolutionNotes { get; set; }

    public string ResolutionStatus { get; set; }
}
