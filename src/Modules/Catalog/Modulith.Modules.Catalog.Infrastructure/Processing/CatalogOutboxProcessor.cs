using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modulith.BuildingBlocks.Application.Events;
using Modulith.BuildingBlocks.Application.Outbox;
using Modulith.BuildingBlocks.Infrastructure.Processing;
using Modulith.Modules.Catalog.IntegrationEvents;

namespace Modulith.Modules.Catalog.Infrastructure.Processing;

/// <summary>
/// Drains the Catalog outbox: reads unprocessed rows, rehydrates the integration event
/// from its stored type name, publishes it on the bus, then marks the row processed.
/// Inherits the at-least-once / mark-after-publish semantics from the base processor.
/// </summary>
internal sealed class CatalogOutboxProcessor : OutboxProcessorBase
{
    private const int BatchSize = 50;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly Assembly ContractAssembly = typeof(ProductCreatedIntegrationEvent).Assembly;

    private static readonly MethodInfo PublishMethod = typeof(IEventsBus)
        .GetMethod(nameof(IEventsBus.Publish))!;

    private readonly CatalogContext _context;
    private readonly IEventsBus _eventsBus;

    public CatalogOutboxProcessor(CatalogContext context, IEventsBus eventsBus, ILogger<CatalogOutboxProcessor> logger)
        : base(logger)
    {
        _context = context;
        _eventsBus = eventsBus;
    }

    protected override async Task<IReadOnlyCollection<OutboxMessage>> GetPendingBatchAsync(CancellationToken cancellationToken)
        => await _context.OutboxMessages
            .Where(x => x.ProcessedOnUtc == null)
            .OrderBy(x => x.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

    protected override async Task ProcessMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var eventType = ContractAssembly.GetType(message.Type)
            ?? throw new InvalidOperationException($"Unknown integration event type '{message.Type}'.");

        var integrationEvent = (IntegrationEvent)JsonSerializer.Deserialize(message.Payload, eventType, SerializerOptions)!;

        var publish = PublishMethod.MakeGenericMethod(eventType);
        await (Task)publish.Invoke(_eventsBus, [integrationEvent, cancellationToken])!;
    }

    protected override async Task MarkAsProcessedAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        message.ProcessedOnUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
