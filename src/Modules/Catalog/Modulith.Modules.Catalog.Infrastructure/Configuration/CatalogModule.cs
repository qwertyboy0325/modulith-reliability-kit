using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulith.BuildingBlocks.Application.Outbox;
using Modulith.BuildingBlocks.Infrastructure.DomainEventsDispatching;
using Modulith.BuildingBlocks.Infrastructure.DependencyInjection;
using Modulith.BuildingBlocks.Infrastructure.ModulePersistence;
using Modulith.Modules.Catalog.Application;
using Modulith.Modules.Catalog.Application.Contracts;
using Modulith.Modules.Catalog.Application.Products.GetProduct;
using Modulith.Modules.Catalog.Domain.Products;
using Modulith.Modules.Catalog.Domain.Products.Events;
using Modulith.Modules.Catalog.Infrastructure.Domain.Products;
using Modulith.Modules.Catalog.Infrastructure.Outbox;
using Modulith.Modules.Catalog.Infrastructure.Processing;

namespace Modulith.Modules.Catalog.Infrastructure.Configuration;

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
