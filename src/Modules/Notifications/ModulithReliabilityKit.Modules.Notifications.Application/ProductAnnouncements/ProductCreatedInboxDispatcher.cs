using System.Text.Json;
using ModulithReliabilityKit.BuildingBlocks.Application.Inbox;
using ModulithReliabilityKit.Modules.Catalog.IntegrationEvents;
using ModulithReliabilityKit.Modules.Notifications.Application.Inbox;

namespace ModulithReliabilityKit.Modules.Notifications.Application.ProductAnnouncements;

/// <summary>
/// Maps a stored inbox row back to its integration-event type and applies the business effect.
/// This is the single place that knows the foreign contract type, keeping the Infrastructure-layer
/// inbox processor generic and free of cross-module IntegrationEvents references.
/// </summary>
public sealed class ProductCreatedInboxDispatcher : IInboxDispatcher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly string ProductCreatedType = typeof(ProductCreatedIntegrationEvent).FullName!;

    private readonly IProductAnnouncementStore _store;

    public ProductCreatedInboxDispatcher(IProductAnnouncementStore store)
    {
        _store = store;
    }

    public Task DispatchAsync(InboxMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Type != ProductCreatedType)
        {
            throw new InvalidOperationException($"No inbox dispatcher registered for type '{message.Type}'.");
        }

        var @event = JsonSerializer.Deserialize<ProductCreatedIntegrationEvent>(message.Payload, SerializerOptions)
            ?? throw new InvalidOperationException($"Inbox payload for '{message.Type}' deserialized to null.");

        return _store.RecordAsync(
            @event.ProductId,
            @event.Name,
            @event.Price,
            @event.Currency,
            @event.OccurredOnUtc,
            cancellationToken);
    }
}
