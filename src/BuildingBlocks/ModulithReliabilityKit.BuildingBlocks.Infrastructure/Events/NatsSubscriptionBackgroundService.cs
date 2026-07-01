using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Diagnostics;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Events;

/// <summary>
/// Runs the consuming side of <see cref="NatsEventBus"/>. For every subscription registered at startup it
/// binds a <b>durable</b> JetStream consumer and dispatches messages to the handler, acking only on
/// success and nacking on failure so JetStream redelivers. Redelivery is safe because the durable
/// consumer's target is the idempotent inbox.
/// </summary>
internal sealed class NatsSubscriptionBackgroundService : BackgroundService
{
    private readonly NatsEventBus _bus;
    private readonly ILogger<NatsSubscriptionBackgroundService> _logger;

    public NatsSubscriptionBackgroundService(NatsEventBus bus, ILogger<NatsSubscriptionBackgroundService> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _bus.EnsureStreamAsync(stoppingToken);

        var subscriptions = _bus.GetSubscriptions();
        if (subscriptions.Count == 0)
        {
            _logger.LogWarning("NATS transport is active but no integration-event subscriptions were registered.");
            return;
        }

        await Task.WhenAll(subscriptions.Select(subscription => ConsumeLoopAsync(subscription, stoppingToken)));
    }

    private async Task ConsumeLoopAsync(NatsSubscription subscription, CancellationToken stoppingToken)
    {
        var consumer = await _bus.JetStream.CreateOrUpdateConsumerAsync(
            _bus.Options.StreamName,
            new ConsumerConfig(subscription.DurableName)
            {
                FilterSubject = subscription.Subject,
                AckPolicy = ConsumerConfigAckPolicy.Explicit,
            },
            stoppingToken);

        _logger.LogInformation(
            "NATS durable consumer {Durable} bound to subject {Subject}",
            subscription.DurableName,
            subscription.Subject);

        await foreach (var message in consumer.ConsumeAsync<string>(cancellationToken: stoppingToken))
        {
            using var activity = ReliabilityInstrumentation.ActivitySource.StartActivity("nats.consume");
            activity?.SetTag("messaging.system", "nats");
            activity?.SetTag("messaging.destination", subscription.Subject);

            try
            {
                await subscription.Handle(message.Data!, stoppingToken);
                await message.AckAsync(cancellationToken: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                _bus.Metrics.TransportRedelivered("nats");

                _logger.LogError(
                    ex,
                    "Handling NATS message on {Subject} failed; nacking for redelivery",
                    subscription.Subject);

                await message.NakAsync(cancellationToken: stoppingToken);
            }
        }
    }
}
