using MediatR;
using ModulithReliabilityKit.Api.Modules;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DependencyInjection;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Diagnostics;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.DomainEventsDispatching;
using ModulithReliabilityKit.BuildingBlocks.Infrastructure.Events;
using ModulithReliabilityKit.Modules.Catalog.Infrastructure.Configuration;
using ModulithReliabilityKit.Modules.Notifications.Infrastructure.Configuration;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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

// Transport is in-memory by default; opt into the durable, cross-process JetStream bus via config.
// The NATS registration is added last, so it overrides the in-memory default when selected.
if (string.Equals(builder.Configuration["Messaging:Transport"], "Nats", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddNatsEventBus(options => builder.Configuration.GetSection("Messaging:Nats").Bind(options));
}

// Modules.
builder.Services.AddCatalogModule(builder.Configuration.GetConnectionString("Catalog")!);
builder.Services.AddNotificationsModule(builder.Configuration.GetConnectionString("Notifications")!);

// Observability: expose the reliability metrics (Prometheus /metrics) and spans. Metrics are always
// recorded; traces are exported only when an OTLP endpoint is configured (Observability:OtlpEndpoint).
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("ModulithReliabilityKit.Api"))
    .WithMetrics(metrics => metrics
        .AddMeter(ReliabilityInstrumentation.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddPrometheusExporter())
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(ReliabilityInstrumentation.ActivitySourceName)
            .AddAspNetCoreInstrumentation();

        var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint));
        }
    });

var app = builder.Build();

// Register each module's domain-event -> notification mappings on the shared mapper.
CatalogModule.MapDomainNotifications(app.Services.GetRequiredService<IDomainNotificationsMapper>());

// Subscribe consumer modules to the integration-event bus (singleton, process lifetime).
NotificationsModule.SubscribeIntegrationEvents(app.Services);

app.UseMiddleware<ModulithReliabilityKit.Api.ExceptionTranslationMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new { service = "ModulithReliabilityKit.Api", status = "running" }))
    .WithName("Root");

app.MapHealthChecks("/health");
app.MapPrometheusScrapingEndpoint();
app.MapCatalogEndpoints();
app.MapNotificationsEndpoints();

app.Run();

// Exposed so integration tests can boot the real host via WebApplicationFactory<Program>.
public partial class Program;
