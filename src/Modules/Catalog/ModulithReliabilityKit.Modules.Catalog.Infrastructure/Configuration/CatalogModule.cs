using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithReliabilityKit.BuildingBlocks.Application.Outbox;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DependencyInjection;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.ModulePersistence;
using ModulithReliabilityKit.Modules.Catalog.Application;
using ModulithReliabilityKit.Modules.Catalog.Application.Contracts;
using ModulithReliabilityKit.Modules.Catalog.Application.Products.GetProduct;
using ModulithReliabilityKit.Modules.Catalog.Domain.Products;
using ModulithReliabilityKit.Modules.Catalog.Domain.Products.Events;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure.Domain.Products;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure.Outbox;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure.Processing;

namespace ModulithReliabilityKit.Modules.Catalog.Infrastructure.Configuration;

/// <summary>
/// Catalog module composition root (MS.DI-first).
/// </summary>
/// <remarks>
/// The module owns its <see cref="CatalogContext"/> and binds persistence through
/// typed Building Blocks adapters (<see cref="IModuleUnitOfWork{TContext}"/>).
/// No generic <c>DbContext</c> forwarding is used, so there is no "last registration wins" trap
/// when multiple modules are added.
/// </remarks>
public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CatalogContext>(options => options.UseNpgsql(connectionString));

        // Bind the Building Blocks pipeline to Catalog persistence through reusable typed module adapters.
        services.AddModulePersistence<CatalogContext>(CatalogApplicationAssembly.Assembly);

        // Module services.
        services.AddScoped<ICatalogModule, CatalogModuleFacade>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IProductReadStore, ProductReadStore>();
        services.AddScoped<IOutbox, OutboxAccessor>();

        // Outbox draining.
        services.AddScoped<CatalogOutboxProcessor>();
        services.AddHostedService<CatalogOutboxBackgroundService>();
        services.AddHostedService<CatalogDatabaseInitializerHostedService>();

        // Module handlers + validators.
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(CatalogApplicationAssembly.Assembly));
        services.AddValidatorsFromAssembly(CatalogApplicationAssembly.Assembly);

        return services;
    }

    /// <summary>
    /// Registers the module's domain-event -> notification mappings on the shared mapper.
    /// Call once at startup after the provider is built.
    /// </summary>
    public static void MapDomainNotifications(IDomainNotificationsMapper mapper)
    {
        mapper.Register<ProductCreatedDomainEvent>(de =>
            new Application.Products.Events.ProductCreatedNotification(de, de.Id));
    }
}
