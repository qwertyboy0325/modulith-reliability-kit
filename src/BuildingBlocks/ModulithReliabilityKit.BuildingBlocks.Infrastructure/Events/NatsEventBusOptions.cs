namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Events;

/// <summary>
/// Configuration for the JetStream-backed <see cref="NatsEventBus"/>. Bound from
/// <c>Messaging:Nats</c> when the host opts into the NATS transport.
/// </summary>
public sealed class NatsEventBusOptions
{
    /// <summary>NATS server URL, e.g. <c>nats://localhost:4222</c>.</summary>
    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>JetStream stream that persists every integration event.</summary>
    public string StreamName { get; set; } = "INTEGRATION_EVENTS";

    /// <summary>
    /// Subject prefix; each event type is published to <c>{SubjectPrefix}.{EventTypeName}</c> and the
    /// stream captures <c>{SubjectPrefix}.&gt;</c>.
    /// </summary>
    public string SubjectPrefix { get; set; } = "integration-events";

    /// <summary>
    /// Durable-consumer name prefix; each subscribed type gets a durable consumer
    /// <c>{DurablePrefix}-{EventTypeName}</c> so delivery survives a subscriber restart.
    /// </summary>
    public string DurablePrefix { get; set; } = "modulith-kit";
}
