using Microsoft.Extensions.DependencyInjection;
using ModulithReliabilityKit.BuildingBlocks.Application.Events;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.Events;

/// <summary>
/// Opt-in registration for the JetStream-backed transport. Register this <b>after</b>
/// <c>AddModulithReliabilityKitBuildingBlocks</c>: the building blocks register the in-memory bus with
/// <c>TryAdd</c>, and this adds <see cref="NatsEventBus"/> as the last <see cref="IEventsBus"/>
/// registration, so it wins when resolved. The default (in-memory) path is unchanged when this is not called.
/// </summary>
public static class NatsEventBusServiceCollectionExtensions
{
    public static IServiceCollection AddNatsEventBus(
        this IServiceCollection services,
        Action<NatsEventBusOptions>? configure = null)
    {
        var options = new NatsEventBusOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<NatsEventBus>();
        services.AddSingleton<IEventsBus>(sp => sp.GetRequiredService<NatsEventBus>());
        services.AddHostedService<NatsSubscriptionBackgroundService>();

        return services;
    }
}
