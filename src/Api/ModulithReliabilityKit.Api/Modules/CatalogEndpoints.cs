using ModulithReliabilityKit.Modules.Catalog.Application.Contracts;
using ModulithReliabilityKit.Modules.Catalog.Application.Products.CreateProduct;
using ModulithReliabilityKit.Modules.Catalog.Application.Products.GetProduct;
using ModulithReliabilityKit.Modules.Catalog.Application.Products.RenameProduct;

namespace ModulithReliabilityKit.Api.Modules;

internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/catalog/products").WithTags("Catalog");

        group.MapPost("/", async (CreateProductRequest request, ICatalogModule catalogModule, CancellationToken ct) =>
        {
            var id = await catalogModule.ExecuteCommandAsync(new CreateProductCommand(request.Name, request.Price, request.Currency), ct);
            return Results.Created($"/catalog/products/{id}", new { id });
        });

        group.MapGet("/{id:guid}", async (Guid id, ICatalogModule catalogModule, CancellationToken ct) =>
        {
            var product = await catalogModule.ExecuteQueryAsync(new GetProductQuery(id), ct);
            return product is null ? Results.NotFound() : Results.Ok(product);
        });

        group.MapPut("/{id:guid}/name", async (Guid id, RenameProductRequest request, ICatalogModule catalogModule, CancellationToken ct) =>
        {
            await catalogModule.ExecuteCommandAsync(new RenameProductCommand(id, request.NewName), ct);
            return Results.NoContent();
        });

        return app;
    }

    private sealed record CreateProductRequest(string Name, decimal Price, string Currency);

    private sealed record RenameProductRequest(string NewName);
}
