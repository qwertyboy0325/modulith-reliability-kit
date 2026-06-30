using ModulithReliabilityKit.BuildingBlocks.Application.Outbox;

namespace ModulithReliabilityKit.Modules.Catalog.Infrastructure.Outbox;

internal sealed class OutboxAccessor : IOutbox
{
    private readonly CatalogContext _context;

    public OutboxAccessor(CatalogContext context)
    {
        _context = context;
    }

    // Add only: the message is flushed by the same SaveChanges that persists the
    // aggregate change, so the write + outbox enqueue are one atomic transaction.
    public void Add(OutboxMessage message) => _context.OutboxMessages.Add(message);
}
