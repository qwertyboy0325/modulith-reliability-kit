using System.Text.Json;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using ModulithReliabilityKit.BuildingBlocks.Application.Events;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Events;

/// <summary>
/// A durable, cross-process <see cref="IEventsBus"/> backed by NATS JetStream.
/// </summary>
/// <remarks>
/// Chosen over Core NATS deliberately: the reliability story requires that a publish is not reported
/// as delivered until the message is <b>persisted</b>. With JetStream, <see cref="Publish{T}"/> awaits a
/// server <c>PubAck</c> before returning, so the outbox only marks a row processed once the message is
/// durably stored — a subscriber being offline never loses it. Delivery to a subscriber is at-least-once
/// via a durable consumer (see <see cref="NatsSubscriptionBackgroundService"/>), which the idempotent
/// inbox deduplicates. This bus therefore preserves the same guarantees as the in-process default while
/// making them hold across separate processes.
/// </remarks>
public sealed class NatsEventBus : IEventsBus
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly NatsEventBusOptions _options;
    private readonly ILogger<NatsEventBus> _logger;
    private readonly NatsConnection _connection;
    private readonly INatsJSContext _jetStream;

    private readonly List<NatsSubscription> _subscriptions = [];
    private readonly object _subscriptionsGate = new();

    private readonly SemaphoreSlim _streamGate = new(1, 1);
    private volatile bool _streamReady;

    public NatsEventBus(NatsEventBusOptions options, ILogger<NatsEventBus> logger)
    {
        _options = options;
        _logger = logger;
        _connection = new NatsConnection(new NatsOpts { Url = options.Url });
        _jetStream = new NatsJSContext(_connection);
    }

    internal NatsEventBusOptions Options => _options;

    internal INatsJSContext JetStream => _jetStream;

    internal IReadOnlyList<NatsSubscription> GetSubscriptions()
    {
        lock (_subscriptionsGate)
        {
            return _subscriptions.ToArray();
        }
    }

    public void Subscribe<TIntegrationEvent>(IIntegrationEventHandler<TIntegrationEvent> handler)
        where TIntegrationEvent : IntegrationEvent
    {
        var eventName = typeof(TIntegrationEvent).Name;

        // Capture the typed handler in a closure so the background consumer can dispatch without
        // reflection: it only ever hands us the JSON payload for this subject.
        Func<string, CancellationToken, Task> pipeline = async (json, ct) =>
        {
            var @event = JsonSerializer.Deserialize<TIntegrationEvent>(json, JsonOptions)
                ?? throw new InvalidOperationException($"NATS payload for '{eventName}' deserialized to null.");
            await handler.Handle(@event, ct);
        };

        var subscription = new NatsSubscription(
            Subject: $"{_options.SubjectPrefix}.{eventName}",
            DurableName: $"{_options.DurablePrefix}-{eventName}",
            Handle: pipeline);

        lock (_subscriptionsGate)
        {
            _subscriptions.Add(subscription);
        }
    }

    public async Task Publish<TIntegrationEvent>(TIntegrationEvent @event, CancellationToken cancellationToken = default)
        where TIntegrationEvent : IntegrationEvent
    {
        await EnsureStreamAsync(cancellationToken);

        var subject = $"{_options.SubjectPrefix}.{typeof(TIntegrationEvent).Name}";
        var json = JsonSerializer.Serialize(@event, JsonOptions);
        var headers = new NatsHeaders
        {
            { "Nats-Event-Type", typeof(TIntegrationEvent).FullName ?? typeof(TIntegrationEvent).Name },
        };

        // Awaiting the PubAck is the whole point: the outbox marks the row processed only after this
        // returns, so a message can never be "published" without being durably stored.
        var ack = await _jetStream.PublishAsync(subject, json, headers: headers, cancellationToken: cancellationToken);
        ack.EnsureSuccess();
    }

    /// <summary>
    /// Idempotently ensures the JetStream stream that captures every integration-event subject exists.
    /// Safe to call from both the publish path and the consumer startup.
    /// </summary>
    internal async Task EnsureStreamAsync(CancellationToken cancellationToken)
    {
        if (_streamReady)
        {
            return;
        }

        await _streamGate.WaitAsync(cancellationToken);
        try
        {
            if (_streamReady)
            {
                return;
            }

            // Idempotent: creates the stream if missing, otherwise reuses/updates it. Safe when several
            // instances race on startup.
            var config = new StreamConfig(_options.StreamName, [$"{_options.SubjectPrefix}.>"]);
            await _jetStream.CreateOrUpdateStreamAsync(config, cancellationToken);
            _streamReady = true;

            _logger.LogInformation(
                "JetStream stream {Stream} ready (capturing {Prefix}.>)",
                _options.StreamName,
                _options.SubjectPrefix);
        }
        finally
        {
            _streamGate.Release();
        }
    }

    public void Dispose()
    {
        _streamGate.Dispose();
        _connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>A registered consumer: the subject to bind, its durable name, and the dispatch pipeline.</summary>
internal sealed record NatsSubscription(
    string Subject,
    string DurableName,
    Func<string, CancellationToken, Task> Handle);
