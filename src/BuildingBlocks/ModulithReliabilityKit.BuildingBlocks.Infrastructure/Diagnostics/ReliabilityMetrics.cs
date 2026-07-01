using System.Diagnostics.Metrics;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Diagnostics;

/// <summary>
/// Domain-specific reliability metrics for the outbox/inbox pipeline and the message transport. These are
/// the numbers an operator actually watches: publish throughput, inbox outcomes (processed / retried /
/// dead-lettered), operator recoveries, and transport redeliveries. Registered as a singleton and shared
/// by every processor; recording is cheap when no listener/exporter is attached.
/// </summary>
public sealed class ReliabilityMetrics : IDisposable
{
    private readonly Meter _meter;

    private readonly Counter<long> _outboxPublished;
    private readonly Counter<long> _outboxPublishFailures;
    private readonly Histogram<double> _outboxProcessDuration;

    private readonly Counter<long> _inboxProcessed;
    private readonly Counter<long> _inboxRetried;
    private readonly Counter<long> _inboxDeadLettered;
    private readonly Histogram<double> _inboxProcessDuration;

    private readonly Counter<long> _deadLetterReprocessed;

    private readonly Counter<long> _transportPublished;
    private readonly Counter<long> _transportRedelivered;

    public ReliabilityMetrics()
        : this(ReliabilityInstrumentation.MeterName)
    {
    }

    // Overload lets a test isolate its own meter (unique name) from parallel tests sharing the process.
    public ReliabilityMetrics(string meterName)
    {
        _meter = new Meter(meterName);

        _outboxPublished = _meter.CreateCounter<long>(
            "messaging.outbox.published", unit: "{message}", description: "Outbox messages published to the bus.");
        _outboxPublishFailures = _meter.CreateCounter<long>(
            "messaging.outbox.publish_failures", unit: "{message}", description: "Outbox publish attempts that threw.");
        _outboxProcessDuration = _meter.CreateHistogram<double>(
            "messaging.outbox.process.duration", unit: "ms", description: "Time to publish one outbox message.");

        _inboxProcessed = _meter.CreateCounter<long>(
            "messaging.inbox.processed", unit: "{message}", description: "Inbox messages applied exactly once.");
        _inboxRetried = _meter.CreateCounter<long>(
            "messaging.inbox.retried", unit: "{message}", description: "Inbox messages scheduled for retry after a failure.");
        _inboxDeadLettered = _meter.CreateCounter<long>(
            "messaging.inbox.dead_lettered", unit: "{message}", description: "Inbox messages moved to the dead-letter table.");
        _inboxProcessDuration = _meter.CreateHistogram<double>(
            "messaging.inbox.process.duration", unit: "ms", description: "Time to apply one inbox message.");

        _deadLetterReprocessed = _meter.CreateCounter<long>(
            "messaging.inbox.dead_letter.reprocessed", unit: "{message}", description: "Dead-letters requeued by an operator.");

        _transportPublished = _meter.CreateCounter<long>(
            "messaging.transport.published", unit: "{message}", description: "Events published on the durable transport.");
        _transportRedelivered = _meter.CreateCounter<long>(
            "messaging.transport.redelivered", unit: "{message}", description: "Transport deliveries nacked for redelivery.");
    }

    public void OutboxPublished(string module)
        => _outboxPublished.Add(1, Tag("module", module));

    public void OutboxPublishFailed(string module)
        => _outboxPublishFailures.Add(1, Tag("module", module));

    public void RecordOutboxProcessDuration(double milliseconds, string module)
        => _outboxProcessDuration.Record(milliseconds, Tag("module", module));

    public void InboxProcessed(string module)
        => _inboxProcessed.Add(1, Tag("module", module));

    public void InboxRetried(string module)
        => _inboxRetried.Add(1, Tag("module", module));

    public void InboxDeadLettered(string module)
        => _inboxDeadLettered.Add(1, Tag("module", module));

    public void RecordInboxProcessDuration(double milliseconds, string module)
        => _inboxProcessDuration.Record(milliseconds, Tag("module", module));

    public void DeadLetterReprocessed(string module)
        => _deadLetterReprocessed.Add(1, Tag("module", module));

    public void TransportPublished(string transport)
        => _transportPublished.Add(1, Tag("transport", transport));

    public void TransportRedelivered(string transport)
        => _transportRedelivered.Add(1, Tag("transport", transport));

    private static KeyValuePair<string, object?> Tag(string key, string value) => new(key, value);

    public void Dispose() => _meter.Dispose();
}
