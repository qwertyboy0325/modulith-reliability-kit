using ModulithReliabilityKit.BuildingBlocks.Application.Queries;

namespace ModulithReliabilityKit.Modules.Catalog.Application.Products.GetProduct;

public sealed record GetProductQuery(Guid ProductId) : IQuery<ProductDto?>;
