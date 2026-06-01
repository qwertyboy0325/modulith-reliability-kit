using Modulith.BuildingBlocks.Application.Queries;

namespace Modulith.Modules.Catalog.Application.Products.GetProduct;

public sealed record GetProductQuery(Guid ProductId) : IQuery<ProductDto?>;
