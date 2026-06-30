namespace ModulithReliabilityKit.Modules.Catalog.Application.Products.GetProduct;

public sealed record ProductDto(
    Guid Id,
    string Name,
    decimal Price,
    string Currency,
    bool IsActive);
