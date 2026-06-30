using Microsoft.Extensions.DependencyInjection;
using ModulithReliabilityKit.BuildingBlocks.Application.Events;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Events;

/// <summary>
/// Bridges the singleton <see cref="IEventsBus"/> to a scoped integration-event handler. The bus holds
/// handler instances for the process lifetime, but consumer handlers usually depend on a scoped
/// <c>DbContext</c>; this wrapper opens a fresh scope per delivery and resolves the real handler from it.
/// </summary>
public sealed class ScopedIntegrationEventHandler<TEvent, THandler> : IIntegrationEventHandler<TEvent>
    where TEvent : IntegrationEvent
    where THandler : class, IIntegrationEventHandler<TEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScopedIntegrationEventHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task Handle(TEvent @event, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<THandler>();
        await handler.Handle(@event, cancellationToken);
    }
}
