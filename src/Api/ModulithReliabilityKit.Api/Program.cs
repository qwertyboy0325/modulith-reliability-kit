using MediatR;
using ModulithReliabilityKit.Api.Modules;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DependencyInjection;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure.Configuration;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

// Host-level (non-persistence) Building Blocks: pipeline, bus, mappers, execution context.
// Each module owns its own DbContext, so persistence services are registered per module.
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(typeof(Program).Assembly));
builder.Services.AddModulithReliabilityKitBuildingBlocks(includePersistenceServices: false);

// Modules.
builder.Services.AddCatalogModule(builder.Configuration.GetConnectionString("Catalog")!);

var app = builder.Build();

// Register each module's domain-event -> notification mappings on the shared mapper.
CatalogModule.MapDomainNotifications(app.Services.GetRequiredService<IDomainNotificationsMapper>());

app.UseMiddleware<ModulithReliabilityKit.Api.ExceptionTranslationMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new { service = "ModulithReliabilityKit.Api", status = "running" }))
    .WithName("Root");

app.MapHealthChecks("/health");
app.MapCatalogEndpoints();

app.Run();
