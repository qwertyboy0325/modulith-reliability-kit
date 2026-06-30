using System.Text.Json;
using ModulithReliabilityKit.BuildingBlocks.Application.Events;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;
using ModulithReliabilityKit.Modules.Catalog.IntegrationEvents;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements;

/// <summary>
/// Bus subscriber for <see cref="ProductCreatedIntegrationEvent"/>. It does NOT apply the business
/// effect inline; it only persists the event to the durable inbox (idempotently). The inbox processor
/// drains it later with retry + dead-letter. This keeps the bus hop best-effort while delivery stays
/// durable, and isolates consumer failures from the publisher.
/// </summary>
public sealed class ProductCreatedIngestHandler : IIntegrationEventHandler<ProductCreatedIntegrationEvent>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IInboxWriter _inboxWriter;

    public ProductCreatedIngestHandler(IInboxWriter inboxWriter)
    {
        _inboxWriter = inboxWriter;
    }

    public Task Handle(ProductCreatedIntegrationEvent @event, CancellationToken cancellationToken = default)
    {
        var message = new InboxMessage(
            @event.Id,
            @event.OccurredOnUtc,
            @event.GetType().FullName!,
            JsonSerializer.Serialize(@event, @event.GetType(), SerializerOptions));

        return _inboxWriter.IngestAsync(message, cancellationToken);
    }
}
