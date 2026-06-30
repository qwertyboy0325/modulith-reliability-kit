using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModulithReliabilityKit.BuildingBlocks.Application;
using ModulithReliabilityKit.BuildingBlocks.Application.Events;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Events;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.ExecutionContext;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.ModulePersistence;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Pipeline;
using System.Reflection;

namespace ModulithReliabilityKit.BuildingBlocks.Infrastructure.DependencyInjection;

/// <summary>
/// MS.DI-first composition root for the Building Blocks layer.
/// </summary>
/// <remarks>
/// Design intent:
/// <list type="bullet">
///   <item>
///     <b>MS.DI-first:</b> all wiring goes through <see cref="IServiceCollection"/>. The Building
///     Blocks layer never references a concrete container (no Autofac <c>Module</c>/<c>ContainerBuilder</c>),
///     so the default host container works with zero extra setup.
///   </item>
///   <item>
///     <b>Autofac-compatible (design space reserved):</b> because registrations live on
///     <see cref="IServiceCollection"/>, a host may opt into Autofac via
///     <c>UseServiceProviderFactory(new AutofacServiceProviderFactory())</c> and these registrations are
///     copied verbatim by <c>builder.Populate(services)</c>. Open-generic behaviors below are also
///     supported by Autofac's populate, so swapping the host container requires no change here.
///   </item>
///   <item>
///     <b>Override seam:</b> swappable services use <c>TryAdd*</c> so a module or an Autofac
///     <c>ConfigureContainer</c> can replace any default (e.g. a real <see cref="IExecutionContextAccessor"/>
///     or a transport-backed <see cref="IEventsBus"/>) by registering its own implementation first.
///     Pipeline behaviors are intentionally additive (<c>AddTransient</c>) — order matters and all three run.
///   </item>
/// </list>
/// </remarks>
/// <param name="services">The host service collection.</param>
/// <param name="includePersistenceServices">
/// When <c>true</c> (default), registers services that require a module-scoped <c>DbContext</c>
/// (<see cref="IUnitOfWork"/>, domain-event dispatching, the UoW behavior). Set to <c>false</c> on a
/// composition host that owns no <c>DbContext</c> (e.g. the API host where each module registers its own).
/// </param>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddModulithReliabilityKitBuildingBlocks(
        this IServiceCollection services,
        bool includePersistenceServices = true)
    {
        // Swappable defaults: TryAdd keeps the override seam open for modules / host container.
        services.TryAddSingleton<IExecutionContextAccessor, NullExecutionContextAccessor>();
        services.TryAddSingleton<IEventsBus, InMemoryEventBus>();
        services.TryAddSingleton<IDomainNotificationsMapper, DomainNotificationsMapper>();

        // Cross-cutting MediatR pipeline. Additive on purpose: order = registration order.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        if (includePersistenceServices)
        {
            // Persistence-bound services; TryAdd so a module can supply a specialized implementation.
            services.TryAddScoped<IUnitOfWork, UnitOfWork>();
            services.TryAddScoped<IDomainEventsAccessor, DomainEventsAccessor>();
            services.TryAddScoped<IDomainEventsDispatcher, DomainEventsDispatcher>();
            services.AddUnitOfWorkBehavior();
        }

        return services;
    }

    public static IServiceCollection AddModulePersistence<TContext>(
        this IServiceCollection services,
        Assembly requestAssembly)
        where TContext : DbContext
    {
        services.AddSingleton(new ModulePersistenceRegistration(requestAssembly, typeof(TContext)));

        services.AddScoped<IDomainEventsAccessor<TContext>, DomainEventsAccessor<TContext>>();
        services.AddScoped<IDomainEventsDispatcher<TContext>, DomainEventsDispatcher<TContext>>();
        services.AddScoped<IModuleUnitOfWork<TContext>, ModuleUnitOfWork<TContext>>();

        services.TryAddScoped<IUnitOfWorkResolver, ModuleUnitOfWorkResolver>();
        services.AddUnitOfWorkBehavior();

        return services;
    }

    private static IServiceCollection AddUnitOfWorkBehavior(this IServiceCollection services)
    {
        services.TryAddEnumerable(
            ServiceDescriptor.Transient(typeof(IPipelineBehavior<,>), typeof(UnitOfWorkBehavior<,>)));

        services.TryAddScoped<IUnitOfWorkResolver, ModuleUnitOfWorkResolver>();

        return services;
    }
}
