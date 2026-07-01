using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using ModulithReliabilityKit.BuildingBlocks.Application.Events;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Diagnostics;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Events;

namespace ModulithReliabilityKit.IntegrationTests.Messaging;

/// <summary>
/// Cross-process reliability of the JetStream-backed <see cref="NatsEventBus"/> against a real NATS
/// server (Testcontainers, JetStream enabled). These pin the two properties that make the transport a
/// safe replacement for the in-process bus: a publish is durable even if no subscriber is running, and
/// delivery is at-least-once (a failed handler causes redelivery).
/// </summary>
public sealed class NatsCrossProcessReliabilityTests : IAsyncLifetime
{
    private readonly IContainer _nats = new ContainerBuilder()
        .WithImage("nats:2.10-alpine")
        .WithCommand("-js")
        .WithPortBinding(4222, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server is ready"))
        .Build();

    public Task InitializeAsync() => _nats.StartAsync();

    public Task DisposeAsync() => _nats.DisposeAsync().AsTask();

    [Fact]
    public async Task A_Message_Published_With_No_Subscriber_Running_Is_Delivered_Once_A_Subscriber_Starts()
    {
        var options = NewOptions();
        var received = new TaskCompletionSource<DemoIntegrationEvent>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var bus = new NatsEventBus(options, new ReliabilityMetrics(), NullLogger<NatsEventBus>.Instance);
        bus.Subscribe(new CapturingHandler(received));

        var sent = new DemoIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, "durable-payload");

        // Publish BEFORE any consumer runs. JetStream persists it against a PubAck, so it must survive
        // until a subscriber is available — this is the guarantee the in-memory bus cannot make.
        await bus.Publish(sent);

        var subscriber = new NatsSubscriptionBackgroundService(bus, NullLogger<NatsSubscriptionBackgroundService>.Instance);
        await subscriber.StartAsync(CancellationToken.None);
        try
        {
            var delivered = await WaitAsync(received.Task, TimeSpan.FromSeconds(20));
            Assert.Equal(sent.Id, delivered.Id);
            Assert.Equal("durable-payload", delivered.Payload);
        }
        finally
        {
            await subscriber.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_Failed_Handler_Causes_Redelivery_Until_It_Succeeds()
    {
        var options = NewOptions();
        var attempts = 0;
        var succeeded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var bus = new NatsEventBus(options, new ReliabilityMetrics(), NullLogger<NatsEventBus>.Instance);
        bus.Subscribe(new FlakyHandler(() =>
        {
            // Fail the first delivery (nack → redeliver), succeed on the retry.
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("transient downstream failure");
            }

            succeeded.TrySetResult();
        }));

        var subscriber = new NatsSubscriptionBackgroundService(bus, NullLogger<NatsSubscriptionBackgroundService>.Instance);
        await subscriber.StartAsync(CancellationToken.None);
        try
        {
            await bus.Publish(new DemoIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow, "redelivered-payload"));

            await WaitAsync(succeeded.Task, TimeSpan.FromSeconds(30));
            Assert.True(attempts >= 2, $"expected at least one redelivery, saw {attempts} attempt(s)");
        }
        finally
        {
            await subscriber.StopAsync(CancellationToken.None);
        }
    }

    // Unique stream/subject per test so a shared server never leaks state between runs.
    private NatsEventBusOptions NewOptions() => new()
    {
        Url = $"nats://{_nats.Hostname}:{_nats.GetMappedPublicPort(4222)}",
        StreamName = "IT_" + Guid.NewGuid().ToString("N"),
        SubjectPrefix = "it-" + Guid.NewGuid().ToString("N"),
        DurablePrefix = "it",
    };

    private static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
        {
            throw new TimeoutException("Timed out waiting for NATS delivery.");
        }

        return await task;
    }

    private static async Task WaitAsync(Task task, TimeSpan timeout)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
        {
            throw new TimeoutException("Timed out waiting for NATS delivery.");
        }

        await task;
    }

    private sealed class CapturingHandler : IIntegrationEventHandler<DemoIntegrationEvent>
    {
        private readonly TaskCompletionSource<DemoIntegrationEvent> _received;

        public CapturingHandler(TaskCompletionSource<DemoIntegrationEvent> received) => _received = received;

        public Task Handle(DemoIntegrationEvent @event, CancellationToken cancellationToken = default)
        {
            _received.TrySetResult(@event);
            return Task.CompletedTask;
        }
    }

    private sealed class FlakyHandler : IIntegrationEventHandler<DemoIntegrationEvent>
    {
        private readonly Action _onHandle;

        public FlakyHandler(Action onHandle) => _onHandle = onHandle;

        public Task Handle(DemoIntegrationEvent @event, CancellationToken cancellationToken = default)
        {
            _onHandle();
            return Task.CompletedTask;
        }
    }

    public sealed class DemoIntegrationEvent : IntegrationEvent
    {
        public DemoIntegrationEvent(Guid id, DateTime occurredOnUtc, string payload)
            : base(id, occurredOnUtc)
        {
            Payload = payload;
        }

        public string Payload { get; }
    }
}
