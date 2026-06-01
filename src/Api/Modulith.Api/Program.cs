using MediatR;
using Modulith.Api.Modules;
using Modulith.BuildingBlocks.Infrastructure.DependencyInjection;
using Modulith.BuildingBlocks.Infrastructure.DomainEventsDispatching;
using Modulith.Modules.Catalog.Infrastructure.Configuration;
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
builder.Services.AddModulithBuildingBlocks(includePersistenceServices: false);

// Modules.
builder.Services.AddCatalogModule(builder.Configuration.GetConnectionString("Catalog")!);

var app = builder.Build();

// Register each module's domain-event -> notification mappings on the shared mapper.
CatalogModule.MapDomainNotifications(app.Services.GetRequiredService<IDomainNotificationsMapper>());

app.UseMiddleware<Modulith.Api.ExceptionTranslationMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new { service = "Modulith.Api", status = "running" }))
    .WithName("Root");

app.MapHealthChecks("/health");
app.MapCatalogEndpoints();

app.Run();
