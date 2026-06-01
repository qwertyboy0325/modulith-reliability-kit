using System.Text.Json;
using MediatR;
using Modulith.BuildingBlocks.Application.Outbox;
using Modulith.Modules.Catalog.IntegrationEvents;

namespace Modulith.Modules.Catalog.Application.Products.Events;

/// <summary>
/// Translates the in-process domain event into a durable integration event and
/// enqueues it on the outbox WITHIN the same transaction as the aggregate change.
/// The outbox processor publishes it to the bus after commit (no dual-write).
/// </summary>
public sealed class ProductCreatedNotificationHandler : INotificationHandler<ProductCreatedNotification>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IOutbox _outbox;

    public ProductCreatedNotificationHandler(IOutbox outbox)
    {
        _outbox = outbox;
    }

    public Task Handle(ProductCreatedNotification notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;

        var integrationEvent = new ProductCreatedIntegrationEvent(
            Guid.NewGuid(),
            domainEvent.OccurredOnUtc,
            domainEvent.ProductId.Value,
            domainEvent.Name,
            domainEvent.Price,
            domainEvent.Currency);

        _outbox.Add(new OutboxMessage(
            integrationEvent.Id,
            integrationEvent.OccurredOnUtc,
            integrationEvent.GetType().FullName!,
            JsonSerializer.Serialize(integrationEvent, SerializerOptions)));

        return Task.CompletedTask;
    }
}
