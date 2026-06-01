namespace Modulith.BuildingBlocks.Application.Outbox;

public sealed class OutboxMessage
{
    public OutboxMessage(Guid logicalId, DateTime occurredOnUtc, string type, string payload)
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

    private OutboxMessage()
    {
    }

    public long Id { get; private set; }

    public Guid LogicalId { get; private set; }

    public DateTime OccurredOnUtc { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTime? ProcessedOnUtc { get; set; }
}
