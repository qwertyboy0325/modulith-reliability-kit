namespace ModulithReliabilityKit.BuildingBlocks.Application.Inbox;

public sealed class InboxMessage
{
    public InboxMessage(Guid logicalId, DateTime occurredOnUtc, string type, string payload)
    {
        if (occurredOnUtc == default)
        {
            throw new ArgumentException("OccurredOnUtc cannot be default.", nameof(occurredOnUtc));
        }

        LogicalId = logicalId;
        OccurredOnUtc = occurredOnUtc;
        Type = type;
        Payload = payload;
    }

    private InboxMessage()
    {
    }

    public long Id { get; private set; }

    public Guid LogicalId { get; private set; }

    public DateTime OccurredOnUtc { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTime? ProcessedOnUtc { get; set; }

    public int RetryCount { get; set; }

    public DateTime? LastRetryOnUtc { get; set; }

    public string? LastError { get; set; }

    public DateTime? NextRetryOnUtc { get; set; }

    public string Status { get; set; } = "pending";
}
